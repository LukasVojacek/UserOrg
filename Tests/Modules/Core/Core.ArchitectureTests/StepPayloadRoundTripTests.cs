using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace Core.ArchitectureTests;

/// <summary>
/// A workflow step payload is written to <c>RequestSteps.InputData</c> when the step starts and read back
/// verbatim when the worker replays it after a crash, so anything a step carries has to survive
/// System.Text.Json unchanged. This suite performs exactly that round trip — the engine's own runtime-type
/// overloads (<c>SubmitHandler</c>, <c>RequestWorker</c>), not the generic ones, because the difference between
/// them is where T-055 hid — on a sample of every step type <see cref="WorkflowStepPayloads"/> discovers.
/// </summary>
public sealed class StepPayloadRoundTripTests
{
    [Fact]
    public void Should_FindTheStepTypes_When_ScanningTheCoreModule() =>
        Assert.NotEmpty(WorkflowStepPayloads.StepTypes);

    [Theory]
    [MemberData(nameof(EveryStepType))]
    public void Should_SurviveTheJsonRoundTrip_When_StepPayloadIsPersistedAndReplayed(Type stepType)
    {
        ArgumentNullException.ThrowIfNull(stepType);

        var step = StepPayloadSampleBuilder.Build(stepType);

        using var raw = JsonSerializer.SerializeToDocument(step, step.GetType());

        object? replayed = null;
        try
        {
            replayed = JsonSerializer.Deserialize(raw, stepType);
        }
        catch (JsonException exception)
        {
            FailReplay(stepType, exception, raw);
        }
        catch (NotSupportedException exception)
        {
            FailReplay(stepType, exception, raw);
        }

        Assert.NotNull(replayed);
        AssertPayloadEqual(step, replayed, stepType.Name, raw);
    }

    public static TheoryData<Type> EveryStepType() => new(WorkflowStepPayloads.StepTypes);

    private static void FailReplay(Type stepType, Exception exception, JsonDocument raw) =>
        Assert.Fail(
            $"{stepType.Name} cannot be replayed from its persisted payload: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}  payload: {raw.RootElement.GetRawText()}");

    // Record equality compares collection members by reference, so a round-tripped payload never equals its
    // source under `==` regardless of content. The comparison therefore descends member by member and
    // element by element, and names the member it failed on.
    private static void AssertPayloadEqual(object? expected, object? actual, string path, JsonDocument raw)
    {
        if (expected is null || actual is null)
        {
            Assert.True(
                expected is null && actual is null,
                Message(path, expected, actual, raw));
            return;
        }

        switch (expected)
        {
            case JsonDocument document:
                Assert.True(
                    actual is JsonDocument replayed
                    && replayed.RootElement.GetRawText() == document.RootElement.GetRawText(),
                    Message(path, expected, actual, raw));
                return;

            case string:
                Assert.True(expected.Equals(actual), Message(path, expected, actual, raw));
                return;

            case IEnumerable source:
                var expectedItems = source.Cast<object?>().ToArray();
                var actualItems = (actual as IEnumerable)?.Cast<object?>().ToArray() ?? [];
                Assert.True(
                    expectedItems.Length == actualItems.Length,
                    Message($"{path}.Count", expectedItems.Length, actualItems.Length, raw));

                for (var i = 0; i < expectedItems.Length; i++)
                    AssertPayloadEqual(expectedItems[i], actualItems[i], $"{path}[{i}]", raw);
                return;
        }

        var members = expected.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();

        if (expected.GetType().IsValueType || members.Length == 0)
        {
            Assert.True(expected.Equals(actual), Message(path, expected, actual, raw));
            return;
        }

        foreach (var member in members)
            AssertPayloadEqual(member.GetValue(expected), member.GetValue(actual), $"{path}.{member.Name}", raw);
    }

    private static string Message(string path, object? expected, object? actual, JsonDocument raw) =>
        $"{path} did not survive the round trip: persisted {Describe(expected)}, replayed {Describe(actual)}{Environment.NewLine}  payload: {raw.RootElement.GetRawText()}";

    private static string Describe(object? value) => value switch
    {
        null => "<null>",
        JsonDocument document => document.RootElement.GetRawText(),
        IEnumerable sequence and not string => $"[{string.Join(", ", sequence.Cast<object?>().Select(Describe))}]",
        _ => value.ToString() ?? "<null>",
    };
}
