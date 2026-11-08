using System.Runtime.CompilerServices;

using Core.Domain.Primitives;

namespace Core.ArchitectureTests;

/// <summary>
/// The shape rule for workflow step payloads. A step is a message that is JSON-serialized into
/// <c>RequestSteps.InputData</c> and rebuilt from that row on replay, possibly days later and always in a
/// different DI scope, so it may carry only data that survives the trip:
/// <list type="bullet">
/// <item>no domain entity or aggregate root — the natural key crosses the boundary, the handler reloads
/// (ADR-0029); an aggregate in the payload deserializes into a detached graph, and a lazily loaded navigation
/// throws on the way out</item>
/// <item>no <c>ValueTuple</c> — its members are fields, which System.Text.Json ignores, so the payload
/// serializes to <c>{}</c> and comes back empty without an error</item>
/// <item>no interfaces other than <see cref="IReadOnlyList{T}"/> / <see cref="IReadOnlyCollection{T}"/> —
/// System.Text.Json has no concrete type to rebuild anything else into</item>
/// </list>
/// The same reflection drives <see cref="StepPayloadRoundTripTests"/>, which proves the rule empirically on a
/// sample of every discovered step; this suite is the cheap version that names the offending member.
/// </summary>
public sealed class WorkflowStepPayloadTests
{
    [Fact]
    public void Should_FindTheStepTypes_When_ScanningTheCoreModule() =>
        Assert.NotEmpty(WorkflowStepPayloads.StepTypes);

    [Theory]
    [MemberData(nameof(EveryStepType))]
    public void Should_CarryOnlyReplayableData_When_TypeIsAStepPayload(Type stepType)
    {
        ArgumentNullException.ThrowIfNull(stepType);

        var violations = new List<string>();

        foreach (var (path, type) in WorkflowStepPayloads.TypeGraph(stepType))
        {
            if (typeof(IEntity).IsAssignableFrom(type) || typeof(AggregateRoot).IsAssignableFrom(type))
                violations.Add($"{path} : {Name(type)} — a domain entity; carry its natural key instead (ADR-0029)");
            else if (IsValueTuple(type))
                violations.Add($"{path} : {Name(type)} — a ValueTuple; System.Text.Json ignores its fields");
            else if (type.IsInterface && !IsReplayableInterface(type))
                violations.Add($"{path} : {Name(type)} — an interface System.Text.Json cannot rebuild");
        }

        Assert.True(
            violations.Count == 0,
            $"{stepType.Name} carries members that do not survive persistence and replay:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", violations)}");
    }

    public static TheoryData<Type> EveryStepType() => new(WorkflowStepPayloads.StepTypes);

    private static bool IsValueTuple(Type type) =>
        type.IsValueType && typeof(ITuple).IsAssignableFrom(type);

    private static bool IsReplayableInterface(Type type) =>
        type.IsGenericType
        && (type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            || type.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>));

    private static string Name(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>"
            : type.Name;
}
