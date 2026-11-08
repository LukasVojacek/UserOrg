namespace WebsocketStompClient;

public sealed record StompClientOptions
{
    public const string SectionName = "StompClient";
    public string Url { get; set; } = string.Empty;
    public bool ReconnectEnable { get; set; } = true;
    public string? StompVersion { get; set; }
    public int? ReconnectTimeoutMs { get; set; }
    public string? HeartBeat { get; set; }
    public string? ProxyUrl { get; set; }
}