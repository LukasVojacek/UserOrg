using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using WebsocketStompClient;

namespace Waldure.StompClient.Tests.Tests
{
    public class StompClientIntegrationTests
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(300);

        [Fact(DisplayName = "STOMP echo: Send message to unique queue and receive same payload", Skip = "Integration test requires external STOMP server")]
        public async Task SendMessage_ToUniqueQueue_ReceivesSamePayloadAsync()
        {
            // Arrange
            var cancellationToken = TestContext.Current.CancellationToken;
            using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.UserOrg_AppHost>(cancellationToken);
            appHost.Services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
                logging.AddFilter("Aspire.", LogLevel.Debug);
                // To output logs to the xUnit.net ITestOutputHelper, consider adding a package from https://www.nuget.org/packages?q=xunit+logging
            });
            appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
            {
                clientBuilder.AddStandardResilienceHandler();
            });

            await using var app = (await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken));
            await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

            // Act
            await app.ResourceNotifications.WaitForResourceHealthyAsync("rabbitMqStomp", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

            var endpoint = app.GetEndpoint("rabbitMqStomp", "ws");
            var connectedTcs = new TaskCompletionSource<bool>();
            var messageTcs = new TaskCompletionSource<StompMessage>();
            var connectionErrorTcs = new TaskCompletionSource<string>();

            string wsUrl = $"ws://localhost:{endpoint.Port}/ws";

            using var client = new WebsocketStompClient.StompClient(wsUrl);

            // Minimal handlers: mark connected state and propagate errors
            client.OnConnect += (sender, msg) => connectedTcs.TrySetResult(true);
            client.OnError += (sender, msg) =>
            {
                connectionErrorTcs.TrySetResult($"STOMP Error: {msg.TextBody}");
                messageTcs.TrySetException(new Exception($"STOMP Error: {msg.TextBody}"));
            };

            // Connect with credentials (guest/guest) and conservative headers
            var connectHeaders = new Dictionary<string, string>
            {
                { "login", "guest" },
                { "passcode", "guest" },
                { "host", "/" },
                { "heart-beat", "0,0" },
                { "accept-version", "1.2" }
            };

            await client.ConnectAsync(connectHeaders);

            // Wait for connection or error with a short timeout
            var connectionWait = await Task.WhenAny(
                connectedTcs.Task,
                connectionErrorTcs.Task,
                Task.Delay(TimeSpan.FromSeconds(10), cancellationToken)
            );

            if (connectionErrorTcs.Task.IsCompleted)
            {
                var err = await connectionErrorTcs.Task;
                Assert.Fail($"Connection failed: {err}");
            }

            if (connectionWait != connectedTcs.Task || !connectedTcs.Task.IsCompleted)
            {
                Assert.Fail("Connection timeout or not established");
            }

            // Prepare a unique queue and subscribe; subscription handler completes the messageTcs
            var queueName = $"echo-{Guid.NewGuid():N}";
            var destination = $"/queue/{queueName}";
            var subscribeHeaders = new Dictionary<string, string> { { "ack", "auto" } };

            await client.SubscribeAsync(destination, subscribeHeaders, (sender, msg) => messageTcs.TrySetResult(msg));

            // Send payload and wait for it to be echoed back
            var testPayload = $"echo-test-{Guid.NewGuid():N}";
            await client.SendAsync(testPayload, destination, new Dictionary<string, string>());

            var receivedMessage = await messageTcs.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            Assert.NotNull(receivedMessage);
            Assert.Equal("MESSAGE", receivedMessage.Command);

            await client.DisconnectAsync();
            await app.DisposeAsync();
        }

        [Fact(Skip = "Integration test requires external STOMP server")]
        public async Task TestStomp()
        {
            string wsUrl = "wss://allocations.stg.my-eurohpc.eu/rmqws-stomp";
            string userUuid = "ec1010ee847e49d6b5ec10ca1d51f5a8";
            string subscriptionUuid = "4c9c6cc1310640c9bc8d49811fd1d1df";
            string apiKey = "9a73b5b4b7c7bd6ba6f53681eb1d3cdf1c10f800";
            string offeringUuid = "9c01c82dba81450b8c493a3126681fb8";
            string ownId = "sub-01";

            var cancellationToken = TestContext.Current.CancellationToken;
            var connectedTcs = new TaskCompletionSource<bool>();
            var messageTcs = new TaskCompletionSource<StompMessage>();
            var connectionErrorTcs = new TaskCompletionSource<string>();

            using var client = new WebsocketStompClient.StompClient(wsUrl);

            // Minimal handlers: mark connected state and propagate errors
            client.OnConnect += (sender, msg) => connectedTcs.TrySetResult(true);
            client.OnError += (sender, msg) =>
            {
                connectionErrorTcs.TrySetResult($"STOMP Error: {msg.TextBody}");
                messageTcs.TrySetException(new Exception($"STOMP Error: {msg.TextBody}"));
            };

            // Connect with credentials (guest/guest) and conservative headers
            var connectHeaders = new Dictionary<string, string>
            {
                { "accept-version", "1.2" },
                { "host", userUuid },
                { "login", subscriptionUuid },
                { "passcode", apiKey },
                { "heart-beat", "10000,10000" },
            };

            await client.ConnectAsync(connectHeaders);

            var connectionWait = await Task.WhenAny(
               connectedTcs.Task,
               connectionErrorTcs.Task,
               Task.Delay(TimeSpan.FromSeconds(10), cancellationToken)
           );

            if (connectionErrorTcs.Task.IsCompleted)
            {
                var err = await connectionErrorTcs.Task;
                Assert.Fail($"Connection failed: {err}");
            }

            if (connectionWait != connectedTcs.Task || !connectedTcs.Task.IsCompleted)
            {
                Assert.Fail("Connection timeout or not established");
            }

            // Prepare a unique queue and subscribe; subscription handler completes the messageTcs
            string destination = $"/amq/queue/subscription_{subscriptionUuid}_offering_{offeringUuid}_resource";
            var subscribeHeaders = new Dictionary<string, string> {
                { "id", ownId },
                { "ack", "auto" }
            };

            await client.SubscribeAsync(destination, subscribeHeaders, (sender, msg) => messageTcs.TrySetResult(msg));

            // Send payload and wait for it to be echoed back
            var testPayload = $"echo-test-{Guid.NewGuid():N}";
            await client.SendAsync(testPayload, destination, new Dictionary<string, string>());

            var receivedMessage = await messageTcs.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            Assert.NotNull(receivedMessage);
            Assert.Equal("MESSAGE", receivedMessage.Command);

        }
    }
}
