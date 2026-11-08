using System.Reflection;

using Core.Composition;

using WorkflowEngine.Contracts.Abstractions;

namespace Core.ArchitectureTests;

/// <summary>
/// The single reflection pass both workflow-payload suites are driven by: <see cref="WorkflowStepPayloadTests"/>
/// checks the shape of every discovered step, <see cref="StepPayloadRoundTripTests"/> round-trips a sample of
/// each. A hand-written list on either side would let a thirteenth step land with no round-trip coverage while
/// the shape rule stayed green, which is exactly how T-055 reached production.
/// </summary>
internal static class WorkflowStepPayloads
{
    // Anchored to a public composition type so the Core use-case assembly is loaded (see PublicSurfaceTests).
    private static readonly Assembly CoreAssembly = typeof(ApiAudience).Assembly;

    /// <summary>Every non-abstract <see cref="StepRequest"/> descendant the Core module declares.</summary>
    internal static IReadOnlyList<Type> StepTypes { get; } = CoreAssembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(StepRequest).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// The constructor that defines the payload — a record's primary constructor, never its copy constructor.
    /// It is what System.Text.Json binds to on replay, so it is what both suites reason about.
    /// </summary>
    internal static ConstructorInfo PayloadConstructor(Type type) =>
        PayloadConstructorOrNull(type)
        ?? throw new InvalidOperationException($"{type.Name} has no public constructor to inspect.");

    internal static ConstructorInfo? PayloadConstructorOrNull(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(c => !IsCopyConstructor(c, type))
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

    /// <summary>
    /// The payload's type graph, each entry paired with the member path it was reached through: every
    /// constructor parameter, recursively through generic arguments and array elements, and through the
    /// constructor parameters of the Core-declared records the payload nests.
    /// </summary>
    internal static IReadOnlyList<(string Path, Type Type)> TypeGraph(Type stepType)
    {
        var found = new List<(string, Type)>();
        var visited = new HashSet<Type>();

        foreach (var parameter in PayloadConstructor(stepType).GetParameters())
            Visit($"{stepType.Name}.{parameter.Name}", parameter.ParameterType, visited, found);

        return found;
    }

    private static void Visit(string path, Type type, HashSet<Type> visited, List<(string, Type)> found)
    {
        found.Add((path, type));

        if (!visited.Add(type))
            return;

        foreach (var argument in type.GetGenericArguments())
            Visit($"{path}<{argument.Name}>", argument, visited, found);

        if (type.IsArray && type.GetElementType() is { } element)
            Visit($"{path}[]", element, visited, found);

        if (type.Assembly != CoreAssembly || PayloadConstructorOrNull(type) is not { } constructor)
            return;

        foreach (var parameter in constructor.GetParameters())
            Visit($"{path}.{parameter.Name}", parameter.ParameterType, visited, found);
    }

    private static bool IsCopyConstructor(ConstructorInfo constructor, Type type) =>
        constructor.GetParameters() is [{ } single] && single.ParameterType == type;
}
