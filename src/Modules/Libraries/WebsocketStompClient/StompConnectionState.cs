namespace WebsocketStompClient;

public enum StompConnectionState
{
    Closed,
    Connecting,
    Open,
    Reconnecting,
}