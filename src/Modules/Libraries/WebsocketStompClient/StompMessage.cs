using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WebsocketStompClient;

public class StompMessage
{
    public IReadOnlyDictionary<string, string> Headers { get; }
    public string? TextBody { get; }
    public byte[]? BinaryBody { get; }
    public string? Command { get; }

    public StompMessageBodyType BodyType { get; }

    public StompMessage(string? command)
        : this(command, string.Empty)
    {
    }

    public StompMessage(string? command, string? textBody)
        : this(command, textBody, new Dictionary<string, string>())
    {
    }

    public StompMessage(string? command, IDictionary<string, string> headers)
        : this(command, string.Empty, headers)
    {

    }

    public StompMessage(string? command, string? textBody, IDictionary<string, string> headers)
    {
        Command = command;
        TextBody = textBody;
        Headers = new ReadOnlyDictionary<string, string>(headers ?? new Dictionary<string, string>());
        BodyType = string.IsNullOrEmpty(textBody) ? StompMessageBodyType.Empty : StompMessageBodyType.Text;
    }

    public StompMessage(string? command, byte[]? binBody)
        : this(command, binBody, new Dictionary<string, string>())
    {
    }

    public StompMessage(string? command, byte[]? binBody, IDictionary<string, string> headers)
    {
        Command = command;
        BinaryBody = binBody;
        Headers = new ReadOnlyDictionary<string, string>(headers ?? new Dictionary<string, string>());
        BodyType = binBody == null || binBody.Length == 0 ? StompMessageBodyType.Empty : StompMessageBodyType.Binary;
    }
}