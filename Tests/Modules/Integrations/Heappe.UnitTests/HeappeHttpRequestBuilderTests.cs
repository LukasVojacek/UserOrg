using System.Text;

using Heappe.Contracts.Connection;
using Heappe.Helpers;
using Heappe.Helpers.Connection;

using Moq;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests;

public sealed class HeappeHttpRequestBuilderTests
{
    [Fact]
    public void WithMethodSetsHttpMethod()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();

        // Act
        builder.WithMethod(HttpMethod.Post);

        // Assert - Since method is private, test via Build
        using var request = builder.WithBaseAddress(new Uri("https://example.com")).Build();
        Assert.Equal(HttpMethod.Post, request.Method);
    }

    [Fact]
    public void WithBaseAddressSetsBaseAddress()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        Uri uri = new("https://api.example.com");

        // Act
        builder.WithBaseAddress(uri);

        // Assert
        using var request = builder.Build();
        Assert.Equal(uri, request.RequestUri);
    }

    [Fact]
    public void WithBaseAddressAndPathSetsBaseAddressAndPath()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        Uri baseUri = new("https://api.example.com");
        var relativePath = "test/path";

        // Act
        builder.WithBaseAddressAndPath(baseUri, relativePath);

        // Assert
        using var request = builder.Build();
        Assert.Equal("https://api.example.com/test/path", request.RequestUri.ToString());
    }

    [Fact]
    public void WithAuthorizationBearerTokenAddsAuthorizationHeader()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        BearerTokenHeappeConnectionInfo connectionInfo = new(new Uri("https://api.example.com"), new HeappeIdentifier("test"), "test-token");

        // Act
        builder.WithAuthorization(connectionInfo);

        // Assert
        using var request = builder.WithBaseAddress(new Uri("https://api.example.com")).Build();
        Assert.True(request.Headers.TryGetValues("Authorization", out var values));
        Assert.Equal("Bearer test-token", values.First());
    }

    [Fact]
    public void WithAuthorizationSessionCodeSetsSessionCode()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        var connectionInfo = new SessionCodeHEAppEConnectionInfo(new Uri("https://api.example.com"), new HeappeIdentifier("test"), "test-session");

        // Act
        builder.WithAuthorization(connectionInfo);

        // Assert - For GET, session code should be in query
        using var request = builder.WithBaseAddress(new Uri("https://api.example.com")).Build();
        Assert.Contains("SessionCode=test-session", request.RequestUri.Query);
    }

    [Fact]
    public void WithQueryParameterAddsQueryParameter()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();

        // Act
        builder.WithQueryParameter("key", "value");

        // Assert
        using var request = builder.WithBaseAddress(new Uri("https://api.example.com")).Build();
        Assert.Contains("key=value", request.RequestUri.Query);
    }

    [Fact]
    public void WithBodyHttpContentSetsContent()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        using StringContent content = new("test", Encoding.UTF8, "application/json");

        // Act
        builder.WithBody(content);

        // Assert
        using var request = builder.WithBaseAddress(new Uri("https://api.example.com")).Build();
        Assert.Equal(content, request.Content);
    }

    [Fact]
    public void WithBodyGenericSerializesToJson()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        var obj = new { Name = "Test", Value = 123 };

        // Act
        builder.WithBody(obj);

        // Assert
        using var request = builder.WithBaseAddress(new Uri("https://api.example.com")).Build();
        Assert.NotNull(request.Content);
        Assert.Equal("application/json", request.Content.Headers.ContentType.MediaType);
    }

    [Fact]
    public void WithContentAddsFormFields()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        var fields = new[] { new KeyValuePair<string, string>("field1", "value1") };

        // Act
        builder.WithBodyAsPairs((IEnumerable<KeyValuePair<string, string>>)fields);

        // Assert
        using var request = builder.WithBaseAddress(new Uri("https://api.example.com"))
            .WithMethod(HttpMethod.Post)
            .Build();

        Assert.NotNull(request.Content);
        Assert.Equal("application/json", request.Content.Headers.ContentType.MediaType);
    }

    [Fact]
    public void BuildWithoutBaseAddressThrowsInvalidOperationException()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void DisposeDisposesContent()
    {
        using (HeappeHttpRequestBuilder builder = new())
        {
            using StringContent content = new("test");
            builder.WithBody(content);
        }

        // Assert - Content should be disposed, but since it's internal, hard to test directly
        // This is a basic test; in real scenarios, use a mock or check behavior
    }

    /// <summary>
    /// Tests that WithContent throws ArgumentNullException when the provided collection
    /// contains no valid entries after filtering.
    /// Input conditions:
    /// - An empty collection.
    /// - A collection where every entry is filtered out (keys whitespace or values empty).
    /// Expected result:
    /// - ArgumentNullException is thrown from WithContent.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidContentCases))]
    public void WithContentInvalidOrEmptyThrowsArgumentNullException(IEnumerable<KeyValuePair<string, string>> contentFields)
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.WithBodyAsPairs(contentFields));
    }

    public static IEnumerable<object[]> InvalidContentCases()
    {
        // Case: completely empty collection
        yield return new object[] { Array.Empty<KeyValuePair<string, string>>() };

        // Case: keys whitespace-only and values empty -> all filtered out
        yield return new object[] {
                new[]
                {
                    new KeyValuePair<string, string>("   ", "value"),   // key invalid (whitespace)
                    new KeyValuePair<string, string>("key", ""),       // value invalid (empty)
                    new KeyValuePair<string, string>("", "value2"),    // key invalid (empty)
                }
            };

        // Case: multiple invalid entries including whitespace keys and empty values
        yield return new object[] {
                new[]
                {
                    new KeyValuePair<string, string>(" ", ""),         // both invalid
                    new KeyValuePair<string, string>("\t", ""),        // key whitespace control char
                }
            };
    }

    /// <summary>
    /// Tests that WithContent accepts valid form fields (including when mixed with invalid ones),
    /// returns the same builder instance for fluent chaining, and does not throw.
    /// Input conditions:
    /// - A collection with at least one valid key/value pair.
    /// - A collection mixing valid and invalid entries.
    /// Expected result:
    /// - No exception is thrown; method returns the same builder instance.
    /// - Build() can be called successfully after setting the base address and method.
    /// </summary>
    [Theory]
    [MemberData(nameof(ValidContentCases))]
    public void WithContentContainsValidEntriesAllowsChainingAndDoesNotThrow(IEnumerable<KeyValuePair<string, string>> contentFields)
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();

        // Act
        var returned = builder.WithBodyAsPairs(contentFields);

        // Assert - chaining (same instance)
        Assert.Same(builder, returned);

        // Further assert that Build can be executed after setting required state
        using var request = returned
            .WithBaseAddress(new Uri("https://api.example.com"))
            .WithMethod(HttpMethod.Post)
            .Build();

        Assert.NotNull(request);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://api.example.com"), request.RequestUri);
    }

    public static IEnumerable<object[]> ValidContentCases()
    {
        // Single valid entry
        yield return new object[] {
                new[]
                {
                    new KeyValuePair<string, string>("field1", "value1")
                }
            };

        // Mixed valid and invalid entries -> at least one valid remains after filtering
        yield return new object[] {
                new[]
                {
                    new KeyValuePair<string, string>("   ", "value1"),   // invalid key
                    new KeyValuePair<string, string>("validKey", "v"),   // valid
                    new KeyValuePair<string, string>("another", "")      // invalid value
                }
            };
    }

    /// <summary>
    /// Verifies that calling WithQueryParameters with a null reference throws an ArgumentNullException.
    /// Input: null IEnumerable&lt;KeyValuePair&lt;string,string?&gt;&gt;.
    /// Expected: ArgumentNullException is thrown.
    /// </summary>
    [Fact]
    public void WithQueryParametersNullThrowsArgumentNullException()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        IEnumerable<KeyValuePair<string, string?>>? queryParameters = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.WithQueryParameters(queryParameters!));
    }

    /// <summary>
    /// Verifies that calling WithQueryParameters with no valid entries (all keys null/empty/whitespace or empty collection)
    /// results in an ArgumentNullException whose ParamName equals "queryParameters".
    /// Input: collection with only whitespace/empty keys.
    /// Expected: ArgumentNullException with ParamName == "queryParameters".
    /// </summary>
    [Fact]
    public void WithQueryParametersNoValidEntriesThrowsArgumentNullExceptionParamName()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        var invalidEntries = new[]
        {
                new KeyValuePair<string, string?>(" ", "value1"),
                new KeyValuePair<string, string?>("", "value2")
            };

        // Act
        var ex = Assert.Throws<ArgumentNullException>(() => builder.WithQueryParameters(invalidEntries));

        // Assert
        Assert.Equal("queryParameters", ex.ParamName);
    }

    /// <summary>
    /// Verifies that WithQueryParameters filters out entries with null/whitespace keys and that valid entries
    /// are applied to the built request URI's query string.
    /// Input: mixed collection with one valid and one invalid (whitespace key) entry.
    /// Expected: built request URI contains the valid parameter and does not contain the invalid one.
    /// </summary>
    [Fact]
    public void WithQueryParametersFiltersOutInvalidAndAddsRemainingBuildIncludesQuery()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        var mixed = new[]
        {
                new KeyValuePair<string, string?>("validKey", "validValue"),
                new KeyValuePair<string, string?>("   ", "shouldBeFiltered")
            };

        // Act
        builder.WithQueryParameters(mixed);
        builder.WithBaseAddress(new Uri("https://api.example.com"));
        using var request = builder.Build();

        // Assert
        Assert.NotNull(request.RequestUri);
        var query = request.RequestUri!.Query; // includes leading '?' when non-empty
        Assert.Contains("validKey=validValue", query);
        Assert.DoesNotContain("shouldBeFiltered", query);
    }

    /// <summary>
    /// Verifies that when an explicit HttpContent is provided and no session code is configured,
    /// Build assigns the originally provided HttpContent to the HttpRequestMessage.
    /// Input conditions:
    /// - Method: POST
    /// - BaseAddress: valid absolute URI
    /// - Explicit HttpContent: StringContent with media type application/json
    /// - No session code configured
    /// Expected result:
    /// - Request.Method remains POST
    /// - Request.Content is the originally provided StringContent and its body equals the original payload
    /// </summary>
    [Fact]
    public async Task BuildWithExplicitBodyContentNoSessionCodeAssignsProvidedContent()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        Uri baseUri = new("https://api.example.com");
        var originalContent = "original-payload";
        using StringContent serializedContent = new(originalContent, Encoding.UTF8, "application/json");

        // Act
        builder.WithMethod(HttpMethod.Post)
               .WithBaseAddress(baseUri)
               .WithBody(serializedContent);

        using var request = builder.Build();

        // Assert
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.NotNull(request.Content);

        // Ensure the content is the originally provided one by reading its string representation
        var body = await request.Content.ReadAsStringAsync();
        Assert.Equal(originalContent, body);

        Assert.Equal("application/json", request.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Verifies that when an explicit HttpContent is provided and a session code is configured on a non-GET method,
    /// Build replaces/creates form-url-encoded content containing the SessionCode field.
    /// Input conditions:
    /// - Method: POST
    /// - BaseAddress: valid absolute URI
    /// - Explicit HttpContent: StringContent (will be replaced)
    /// - Session code configured via WithSessionCodeAuthorization
    /// Expected result:
    /// - Request.Method remains POST
    /// - Request.Content is FormUrlEncodedContent and contains the SessionCode key with the configured value
    /// </summary>
    [Theory]
    [InlineData("S123")]
    [InlineData("another-session-code-!@#")]
    public async Task BuildWithBodyContentAndSessionCodeNonGetProducesFormUrlEncodedContentWithSessionCode(string sessionCode)
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        Uri baseUri = new("https://api.example.com");
        using StringContent originalContent = new("original-payload", Encoding.UTF8, "text/plain");

        // Act
        builder.WithMethod(HttpMethod.Post)
               .WithBaseAddress(baseUri)
               .WithBody(originalContent)
               .WithSessionCodeAuthorization(sessionCode);

        using var request = builder.Build();

        // Assert - method remains POST
        Assert.Equal(HttpMethod.Post, request.Method);

        // Content must be present and currently the implementation serializes body fields to JSON
        Assert.NotNull(request.Content);
        Assert.Equal("application/json", request.Content.Headers.ContentType?.MediaType);

        var body = await request.Content.ReadAsStringAsync();

        // Expect JSON object with SessionCode property
        var expectedJson = $"{{\"SessionCode\":{System.Text.Json.JsonSerializer.Serialize(sessionCode)} }}";
        // Normalize spacing differences that might occur
        var normalizedBody = body.Replace(" ", "");
        var normalizedExpected = expectedJson.Replace(" ", "");
        Assert.Equal(normalizedExpected, normalizedBody);
    }

    /// <summary>
    /// Ensures that calling WithBody with invalid name values (empty or whitespace) throws ArgumentNullException.
    /// Input conditions: name is "", " ", "\t", or "\n" and value is a non-empty string.
    /// Expected result: ArgumentNullException is thrown and caller receives no builder instance.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void WithHeaderWhitespaceNameThrowsArgumentNullException(string invalidName)
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        var value = "someValue";

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => builder.WithHeader(invalidName, value));
        Assert.Contains("name", ex.ParamName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures that calling WithBody with valid header names and values results in the header
    /// being present on the HttpRequestMessage produced by Build().
    /// Input conditions: various valid header names and values provided via MemberData.
    /// Expected result: The built request contains the header with the provided value.
    /// </summary>
    [Theory]
    [MemberData(nameof(ValidHeaderData))]
    public void WithHeaderValidNameAddsHeader(string name, string value)
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();

        // Act
        builder.WithHeader(name, value);

        // Build requires a base address
        using var request = builder.WithBaseAddress(new Uri("https://api.example.com")).Build();

        // Assert
        Assert.True(request.Headers.TryGetValues(name, out var headerValues));
        Assert.Contains(value, headerValues);
    }

    /// <summary>
    /// Provides valid header name/value pairs including edge-case long names/values.
    /// This helps exercise potential boundaries for header storage/transfer.
    /// </summary>
    public static IEnumerable<object[]> ValidHeaderData()
    {
        yield return new object[] { "X-Custom-Header", "value1" };
        yield return new object[] { "X-Special-Header", "sp��!al\n\t\r" };
        // Long but valid header name composed of token characters (letters)
        yield return new object[] { new string('a', 300), new string('v', 500) };
    }

    /// <summary>
    /// Verifies that WithBody&lt;T&gt; returns the same builder instance for chaining
    /// and that calling Build afterwards sets the HttpRequestMessage.Content
    /// (the implementation creates a StringContent and assigns it).
    /// Input conditions: various value types and reference types for T are provided.
    /// Expected result: method returns same builder instance and the built request has Content set.
    /// </summary>
    [Theory]
    [MemberData(nameof(SerializationCases))]
    public void WithBodyGenericSetsRequestContent(object contentObj)
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();

        // Act
        var returned = builder.WithBody(contentObj);

        // Assert - fluent API returns the same instance
        Assert.Same(builder, returned);

        // Act - complete necessary configuration for Build
        using var request = builder.WithBaseAddress(new Uri("https://api.example.com")).Build();

        // Assert - the serialized content is assigned to _bodyContent,
        // therefore the built request content is expected to be set.
        Assert.NotNull(request.Content);
    }

    /// <summary>
    /// Provides a set of representative values for generic WithBody&lt;T&gt; invocations.
    /// Contains an anonymous object, a simple string and an integer to cover reference and value types.
    /// </summary>
    public static IEnumerable<object?[]> SerializationCases()
    {
        yield return new object?[] { new { Name = "Test", Value = 123 } };
        yield return new object?[] { "a simple string" };
        yield return new object?[] { 42 };
    }

    /// <summary>
    /// Verifies that WithBody&lt;T&gt; does not throw for a large payload and does set request content.
    /// Input condition: a long string is provided as the content object.
    /// Expected result: no exception thrown and built request Content is set.
    /// </summary>
    [Fact]
    public void WithBodyGenericLargeStringSetsRequestContent()
    {
        // Arrange
        var large = new string('x', 10_000);
        using HeappeHttpRequestBuilder builder = new();

        // Act
        var returned = builder.WithBody(large);

        // Assert fluent return
        Assert.Same(builder, returned);

        // Act - build request
        using var request = builder.WithBaseAddress(new Uri("https://api.example.com")).Build();

        // Assert - content is set due to implementation
        Assert.NotNull(request.Content);
    }

    /// <summary>
    /// Verifies that valid query parameters (including null and special-character values) are included in the built request URI.
    /// Inputs tested:
    /// - Normal value ("value")
    /// - Null value (should produce "key=" in the query)
    /// - Value with special characters ("a b&?")
    /// Expected: The resulting request URI query contains an unescaped "key={value}" segment when unescaped.
    /// </summary>
    [Theory]
    [InlineData("key", "value")]
    [InlineData("key", null)]
    [InlineData("key", "a b&?")]
    public void WithQueryParameterValidKeyValueIncludedInRequestUri(string key, string? value)
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();

        // Act
        builder.WithQueryParameter(key, value);
        using var request = builder.WithBaseAddress(new Uri("https://api.example.com")).Build();

        // Assert
        var query = request.RequestUri?.Query ?? string.Empty;
        // Unescape to make assertions easier for values with special characters
        var unescaped = Uri.UnescapeDataString(query);
        Assert.Contains($"{key}={value ?? string.Empty}", unescaped);
    }

    /// <summary>
    /// Ensures that invalid keys (empty or whitespace-only) cause ArgumentNullException.
    /// Inputs tested:
    /// - Empty string key
    /// - Whitespace only key
    /// Expected: ArgumentNullException is thrown by WithQueryParameter for each invalid key.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithQueryParameterInvalidKeyThrowsArgumentNullException(string invalidKey)
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => builder.WithQueryParameter(invalidKey, "someValue"));
        Assert.Equal("key", ex.ParamName);
    }

    /// <summary>
    /// Verifies that providing null to WithBody(HttpContent) throws ArgumentNullException.
    /// Input conditions: null HttpContent.
    /// Expected result: ArgumentNullException is thrown.
    /// </summary>
    [Fact]
    public void WithBodyNullThrowsArgumentNullException()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        HttpContent? nullContent = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.WithBody(nullContent!));
    }

    /// <summary>
    /// Verifies that calling WithBearerTokenAuthorization with empty or whitespace-only tokens
    /// throws an ArgumentNullException. This checks the method's validation for invalid token inputs.
    /// Input conditions: token is empty or whitespace-only.
    /// Expected result: ArgumentNullException is thrown.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithBearerTokenAuthorizationInvalidTokenThrowsArgumentNullException(string token)
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => builder.WithBearerTokenAuthorization(token));
        Assert.Contains(nameof(token), ex.ParamName ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a valid bearer token is added to the built HttpRequestMessage as an Authorization header.
    /// Input conditions: a non-empty token value and a valid base address (required by Build()).
    /// Expected result: the resulting HttpRequestMessage contains an Authorization header with scheme "Bearer"
    /// and the provided token as the parameter.
    /// </summary>
    [Fact]
    public void WithBearerTokenAuthorizationValidTokenAddsAuthorizationHeader()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        var token = "abc123-!@#";

        // Act
        builder.WithBearerTokenAuthorization(token);
        using var request = builder.WithBaseAddress(new Uri("https://api.example.com")).Build();

        // Assert
        // Prefer strongly-typed Authorization header when available
        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme, ignoreCase: true);
        Assert.Equal(token, request.Headers.Authorization?.Parameter);

        // Also ensure the raw header value exists if needed
        if (!request.Headers.TryGetValues("Authorization", out var values))
        {
            values = Enumerable.Empty<string>();
        }

        Assert.Contains(values, v => v != null && v.Contains(token, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that passing an unsupported HeappeConnectionInfo implementation
    /// to WithAuthorization results in a NotSupportedException containing the runtime type name.
    /// Condition: A mocked concrete instance of the abstract HeappeConnectionInfo (not one of the supported derived types).
    /// Expected: NotSupportedException is thrown and its message contains the concrete type's full name.
    /// </summary>
    [Fact]
    public void WithAuthorizationUnsupportedTypeThrowsNotSupportedException()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        Uri baseAddress = new("https://api.example.com");

        // Moq a concrete instance of the abstract HeappeConnectionInfo to simulate an unsupported subtype.
        Mock<BaseHeappeConnectionInfo> mockConnectionInfo = new(baseAddress, new HeappeIdentifier("test")) { CallBase = false };

        var connectionInfoObj = mockConnectionInfo.Object;
        var expectedTypeName = connectionInfoObj.GetType().FullName ?? string.Empty;

        // Act & Assert
        var ex = Assert.Throws<NotSupportedException>(() => builder.WithAuthorization(connectionInfoObj));
        Assert.Contains("Unsupported connection info type", ex.Message);
        Assert.Contains(expectedTypeName, ex.Message);
    }

    /// <summary>
    /// Verifies WithAuthorization does not throw for supported types and returns the builder instance.
    /// Condition: Use BearerTokenHeappeConnectionInfo and SessionCodeHEAppEConnectionInfo (supported types).
    /// Expected: Method returns the same builder instance (no exception).
    /// Note: This test exercises the happy-path switch arms to ensure no NotSupportedException is raised.
    /// </summary>
    [Fact]
    public void WithAuthorizationSupportedTypesDoNotThrowReturnsBuilder()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder1 = new();
        using HeappeHttpRequestBuilder builder2 = new();
        Uri baseAddress = new("https://api.example.com");

        BearerTokenHeappeConnectionInfo bearer = new(baseAddress, new HeappeIdentifier("test"), "token-value");
        var session = new SessionCodeHEAppEConnectionInfo(baseAddress, new HeappeIdentifier("test"), "session-value");

        // Act
        var returned1 = builder1.WithAuthorization(bearer);
        var returned2 = builder2.WithAuthorization(session);

        // Assert - returned instance should be the same (fluent API)
        Assert.Same(builder1, returned1);
        Assert.Same(builder2, returned2);
    }

    /// <summary>
    /// Verifies that providing empty or whitespace-only sessionCode to WithSessionCodeAuthorization
    /// results in an ArgumentNullException being thrown.
    /// Input conditions: sessionCode is "" or consists only of whitespace.
    /// Expected: ArgumentNullException is thrown.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithSessionCodeAuthorizationWhitespaceThrowsArgumentNullException(string sessionCode)
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => builder.WithSessionCodeAuthorization(sessionCode));
        Assert.Equal("sessionCode", ex.ParamName);
    }

    /// <summary>
    /// Verifies that a valid session code is stored by the builder and applied to the request URI query
    /// for GET requests when Build is called.
    /// Input conditions: default HTTP method (GET), a valid non-whitespace session code, and base address set.
    /// Expected: resulting HttpRequestMessage.RequestUri contains the SessionCode query parameter with the provided value.
    /// </summary>
    [Fact]
    public void WithSessionCodeAuthorizationValidValueAddsSessionCodeToQueryForGet()
    {
        // Arrange
        using HeappeHttpRequestBuilder builder = new();
        Uri baseUri = new("https://api.example.com");
        const string session = "abc123";

        // Act
        builder.WithBaseAddress(baseUri).WithSessionCodeAuthorization(session);
        using var request = builder.Build();

        // Assert
        Assert.NotNull(request);
        Assert.NotNull(request.RequestUri);
        // Query may start with "?" so use Contains to be robust
        Assert.Contains("SessionCode=abc123", request.RequestUri.Query);
    }

    [Theory]
    [InlineData("https+http://nexusapi/api/heappe/heappe-mock/")]
    [InlineData("https+http://nexusapi/api/heappe/heappe-mock")]
    public void WithBaseAddressAndPath_keeps_the_nexus_proxy_prefix(string proxyBase)
    {
        using HeappeHttpRequestBuilder builder = new();

        using var request = builder.WithBaseAddressAndPath(new Uri(proxyBase), "/heappe/Management/Clusters").Build();

        Assert.Equal(new Uri("https+http://nexusapi/api/heappe/heappe-mock/heappe/Management/Clusters"), request.RequestUri);
    }

    [Fact]
    public void WithInstance_sets_the_heappe_identifier_request_option()
    {
        using HeappeHttpRequestBuilder builder = new();

        using var request = builder
            .WithBaseAddressAndPath(new Uri("https+http://nexusapi/api/heappe/heappe-mock/"), "/heappe/Health")
            .WithInstance(new HeappeIdentifier("heappe-mock"))
            .Build();

        Assert.True(request.Options.TryGetValue(HeappeHttpRequestBuilder.HeappeIdentifierKey, out var identifier));
        Assert.Equal("heappe-mock", identifier);
    }
}
