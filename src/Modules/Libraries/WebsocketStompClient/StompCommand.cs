namespace WebsocketStompClient;

internal static class StompCommand
{
    internal const string Connect = "CONNECT";
    internal const string Disconnect = "DISCONNECT";
    internal const string Subscribe = "SUBSCRIBE";
    internal const string Unsubscribe = "UNSUBSCRIBE";
    internal const string Send = "SEND";
    internal const string Ack = "ACK";
    internal const string Nack = "NACK";
    internal const string Connected = "CONNECTED";
    internal const string Message = "MESSAGE";
    internal const string Error = "ERROR";

    //Fictional
    internal const string HeartBeat = "HEARTBEAT";
}