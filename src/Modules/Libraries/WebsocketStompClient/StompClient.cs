using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using Websocket.Client;

namespace WebsocketStompClient;


public class StompClient : IStompClient
{
    public event EventHandler<StompMessage>? OnConnect;
    public event EventHandler<DisconnectionInfo>? OnClose;
    public event EventHandler<StompMessage>? OnMessage;
    public event EventHandler<ReconnectionInfo>? OnReconnect;
    public event EventHandler<StompMessage>? OnError;

    public StompConnectionState StompState { get; private set; } = StompConnectionState.Closed;
    public string Version { get; private set; } = string.Empty;

    private readonly WebsocketClient _socket;
    private readonly ConcurrentDictionary<string, EventHandler<StompMessage>> _subscribers = new();
    private readonly ConcurrentDictionary<string, EventHandler<StompMessage>> _subscribersById = new();
    private readonly ConcurrentDictionary<string, string> _connectingHeaders = new();

    /// <summary>
    /// StompClient Ctor
    /// </summary>
    /// <param name="url">Url of stomp websocket, start with wss or ws</param>
    /// <param name="reconnectEnable">Set reconnect enable or disable</param>
    /// <param name="stompVersion">Add stomp version in header for connecting, set "1.0,1.1,1.2" if nothing specified</param>
    /// <param name="reconnectTimeOut">Time range in ms, how long to wait before reconnecting if last reconnection failed. Set null to disable this feature</param>
    /// <param name="heartBeat">Set 0,1000 if nothing specified</param>
    public StompClient(string url, bool reconnectEnable = true, string? stompVersion = null, TimeSpan? reconnectTimeOut = null, string? heartBeat = null, IWebProxy? proxy = null)
    {
        Console.WriteLine($"[STOMP] Initializing StompClient with URL: {url}");

        _socket = new WebsocketClient(new Uri(url), () =>
    {
        Console.WriteLine("[STOMP] Creating ClientWebSocket with STOMP subprotocol");
        var ws = new ClientWebSocket();
        // Use the standard 'stomp' subprotocol which RabbitMQ web-stomp expects
        ws.Options.AddSubProtocol("stomp");

        if (proxy != null)
        {
            ws.Options.Proxy = proxy;
        }

        return ws;
    })
        {
            ReconnectTimeout = reconnectTimeOut,
            IsReconnectionEnabled = reconnectEnable,
            ErrorReconnectTimeout = TimeSpan.FromSeconds(2.0),
        };

        _socket.MessageReceived.Subscribe(msg =>
        {
            Console.WriteLine("[STOMP] MessageReceived event fired");
            HandleMessage(msg);
        });

        _socket.DisconnectionHappened.Subscribe(info =>
        {
            Console.WriteLine($"[STOMP] Disconnection event: {info.Type}");
            StompState = StompConnectionState.Closed;
            OnClose?.Invoke(this, info);
            _subscribers.Clear();
            _subscribersById.Clear();
        });

        _socket.ReconnectionHappened.Subscribe(async info =>
        {
            Console.WriteLine($"[STOMP] Reconnection event: {info.Type}");
            if (info.Type == ReconnectionType.Initial)
                return;
            OnReconnect?.Invoke(this, info);
            StompState = StompConnectionState.Reconnecting;
            await Reconnect();
        });

        _connectingHeaders[StompHeader.AcceptVersion] = string.IsNullOrEmpty(stompVersion) ? "1.0,1.1,1.2" : stompVersion!;
        _connectingHeaders[StompHeader.Heartbeat] = string.IsNullOrEmpty(heartBeat) ? "0,1000" : heartBeat!;

        OnConnect += (sender, message) =>
        {
            message.Headers.TryGetValue(StompHeader.Version, out var v);
            Version = v ?? string.Empty;
            Console.WriteLine($"[STOMP] OnConnect handler invoked, Server version: {Version}");
        };

        Console.WriteLine("[STOMP] StompClient initialization complete");
    }

    public async Task ConnectAsync(IDictionary<string, string> headers)
    {
        Console.WriteLine($"[STOMP] ConnectAsync started, Current state: {StompState}, Socket running: {_socket.IsRunning}");

        try
        {
            if (!_socket.IsRunning)
            {
                Console.WriteLine("[STOMP] Socket not running, starting...");
                // WebsocketClient.Start() returns Task - must await it!
                await _socket.Start();
                Console.WriteLine($"[STOMP] Socket started. IsRunning: {_socket.IsRunning}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STOMP] Failed to start socket: {ex.Message}");
            throw new Exception($"Failed to start WebSocket: {ex.Message}", ex);
        }

        if (!_socket.IsRunning)
        {
            Console.WriteLine("[STOMP] Socket is still not running after Start()");
            throw new Exception("Connection is not open - WebSocket failed to start");
        }

        if (StompState == StompConnectionState.Open || StompState == StompConnectionState.Connecting)
        {
            Console.WriteLine($"[STOMP] Already in state {StompState}, skipping connect");
            return;
        }

        foreach (var header in headers)
        {
            _connectingHeaders[header.Key] = header.Value;
        }

        var connectMessage = new StompMessage(StompCommand.Connect, _connectingHeaders);
        Console.WriteLine($"[STOMP] Sending CONNECT message with headers: {string.Join(", ", _connectingHeaders.Keys)}");

        await _socket.SendInstant(StompTextMessageSerializer.Serialize(connectMessage));
        StompState = StompConnectionState.Connecting;

        Console.WriteLine("[STOMP] CONNECT message sent, waiting for CONNECTED response...");
    }

    public async Task Reconnect()
    {
        if (!_socket.IsRunning)
            await _socket.Start();
        if (StompState == StompConnectionState.Open)
            return;
        var connectMessage = new StompMessage(StompCommand.Connect, _connectingHeaders);
        await _socket.SendInstant(StompTextMessageSerializer.Serialize(connectMessage));
        StompState = StompConnectionState.Open;
    }

    public async Task SendAsync(object body, string destination, IDictionary<string, string> headers)
    {
        var jsonPayload = JsonSerializer.Serialize(body);
        headers.Add(StompHeader.ContentType, "application/json;charset=UTF-8");
        headers.Add(StompHeader.ContentLength, Encoding.UTF8.GetByteCount(jsonPayload).ToString());
        await SendAsync(jsonPayload, destination, headers);
    }

    public async Task SendAsync(string body, string destination, IDictionary<string, string> headers)
    {
        if (StompState != StompConnectionState.Open)
            await Reconnect();

        headers.Add(StompHeader.Destination, destination);
        var connectMessage = new StompMessage(StompCommand.Send, body, headers);
        await _socket.SendInstant(StompTextMessageSerializer.Serialize(connectMessage));
    }

    public async Task SubscribeAsync<T>(string topic, IDictionary<string, string> headers, EventHandler<T> handler)
    {
        await SubscribeAsync(topic, headers, (sender, message) => handler(this, JsonSerializer.Deserialize<T>(message.TextBody ?? string.Empty)!));
    }


    public async Task SubscribeAsync(string topic, IDictionary<string, string> headers, EventHandler<StompMessage> handler)
    {
        if (StompState != StompConnectionState.Open)
            await Reconnect();

        headers[StompHeader.Destination] = topic ?? string.Empty;
        if (!headers.ContainsKey(StompHeader.Id))
            headers[StompHeader.Id] = $"sub-{_subscribersById.Count}";
        var subscribeMessage = new StompMessage(StompCommand.Subscribe, headers);
        await _socket.SendInstant(StompTextMessageSerializer.Serialize(subscribeMessage));
        var id = headers[StompHeader.Id];
        _subscribersById[id] = handler;
        _subscribers[topic ?? string.Empty] = handler;
    }

    public async Task AckAsync(string id, string? transaction = null)
    {
        await Acknowledge(true, id, transaction);
    }

    public async Task NackAsync(string id, string? transaction = null)
    {
        await Acknowledge(false, id, transaction);
    }

    public async Task DisconnectAsync()
    {
        var disconnectMessage = new StompMessage(StompCommand.Disconnect);
        await _socket.SendInstant(StompTextMessageSerializer.Serialize(disconnectMessage));
        StompState = StompConnectionState.Closed;
        if (_socket.IsRunning)
        {
            await _socket.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Disconnect");
        }
        _subscribers.Clear();
        _subscribersById.Clear();
    }

    public void Dispose()
    {
        StompState = StompConnectionState.Closed;
        ((IDisposable)_socket).Dispose();
        _subscribers.Clear();
        _subscribersById.Clear();
    }

    private async Task Acknowledge(bool isPositive, string id, string? transaction = null)
    {
        if (StompState != StompConnectionState.Open)
            await Reconnect();

        var headers = new Dictionary<string, string>()
        {
            { StompHeader.Id, id }
        };
        if (!string.IsNullOrEmpty(transaction))
            headers.Add(StompHeader.Transaction, transaction!);
        var connectMessage = new StompMessage(isPositive ? StompCommand.Ack : StompCommand.Nack, headers);
        await _socket.SendInstant(StompTextMessageSerializer.Serialize(connectMessage));
    }

    private void HandleMessage(ResponseMessage messageEventArgs)
    {
        Console.WriteLine($"[STOMP] HandleMessage: MessageType={messageEventArgs.MessageType}, Length={messageEventArgs.Text?.Length ?? 0}");

        StompMessage message;
        if (messageEventArgs.MessageType == WebSocketMessageType.Text)
        {
            Console.WriteLine($"[STOMP] Deserializing text message: {messageEventArgs.Text?.Substring(0, Math.Min(100, messageEventArgs.Text?.Length ?? 0)) ?? "null"}");
            message = StompTextMessageSerializer.Deserialize(messageEventArgs.Text);
        }
        else
        {
            if (messageEventArgs.Binary.Length > 1)
            {
                message = StompBinaryMessageSerializer.Deserialize(messageEventArgs.Binary);
            }
            else
            {
                message = new StompMessage(StompCommand.HeartBeat);
            }
        }

        Console.WriteLine($"[STOMP] Received message: Command={message.Command}, State={StompState}");

        OnMessage?.Invoke(this, message);

        if (message.Command == StompCommand.Connected)
        {
            Console.WriteLine("[STOMP] CONNECTED response received!");
            if (StompState == StompConnectionState.Connecting)
            {
                StompState = StompConnectionState.Open;
                Console.WriteLine("[STOMP] State changed to Open");
            }
            OnConnect?.Invoke(this, message);
        }

        if (message.Command == StompCommand.Error)
        {
            Console.WriteLine($"[STOMP] ERROR response: {message.TextBody}");
            OnError?.Invoke(this, message);
        }

        if (message.Headers.TryGetValue(StompHeader.Subscription, out var subscription) && _subscribersById.TryGetValue(subscription, out var subById))
        {
            Console.WriteLine($"[STOMP] Forwarding message to subscriber by ID: {subscription}");
            subById(this, message);
        }
        else if (message.Headers.TryGetValue(StompHeader.Destination, out var header))
        {
            if (!_subscribers.ContainsKey(header))
            {
                // Workaround for RabbitMQ subscription to a queue created outside the STOMP gateway
                // https://www.rabbitmq.com/stomp.html#d
                header = $"/amq{header}";
            }

            if (_subscribers.TryGetValue(header, out var subscriber))
            {
                Console.WriteLine($"[STOMP] Forwarding message to subscriber: {header}");
                subscriber(this, message);
            }
        }
    }
}