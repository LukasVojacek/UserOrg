using System;
using System.Collections.Generic;
using System.Text;

namespace WebsocketStompClient;

internal static class StompHeader
{
    internal const string ContentLength = "content-length";
    internal const string ContentType = "content-type";
    internal const string AcceptVersion = "accept-version";
    internal const string Version = "version";
    internal const string Host = "host";
    internal const string Login = "login";
    internal const string Passcode = "passcode";
    internal const string Heartbeat = "heart-beat";
    internal const string Id = "id";
    internal const string Acknowledge = "ack";
    internal const string Transaction = "transaction";
    internal const string MessageId = "message-id";
    internal const string ReceiptId = "receipt-id";
    internal const string Receipt = "receipt";

    internal const string Destination = "destination";
    internal const string Subscription = "subscription";
}