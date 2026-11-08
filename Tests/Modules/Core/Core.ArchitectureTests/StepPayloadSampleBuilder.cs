using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Core.ArchitectureTests;

/// <summary>
/// Builds one filled-in sample per discovered step type by walking its payload constructor. Reflection-driven
/// on purpose: a hand-written sample per step would drift away from <see cref="WorkflowStepPayloads.StepTypes"/>
/// the moment a step gains a member, and the round trip would stop covering it.
/// <para>
/// Every member is populated — no nulls, no default enum, two elements in every collection — so a member the
/// round trip drops shows up as a difference instead of matching a default on both sides.
/// </para>
/// </summary>
internal sealed class StepPayloadSampleBuilder
{
    private const int MaxDepth = 12;

    private int _seed;

    internal static object Build(Type stepType) =>
        new StepPayloadSampleBuilder().Create(stepType, stepType.Name, 0)
        ?? throw new InvalidOperationException($"No sample could be built for {stepType.Name}.");

    private object? Create(Type type, string path, int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidOperationException($"{path} nests deeper than {MaxDepth} levels — cyclic payload?");

        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Create(underlying, path, depth);

        if (type == typeof(string))
            return path;

        if (type == typeof(bool))
            return true;

        if (type == typeof(Guid))
            return new Guid(Next(), 0, 0, [1, 2, 3, 4, 5, 6, 7, 8]);

        if (type == typeof(DateTimeOffset))
            return new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)).AddMinutes(Next());

        if (type == typeof(DateTime))
            return new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddMinutes(Next());

        if (type == typeof(TimeSpan))
            return TimeSpan.FromMinutes(Next());

        if (type == typeof(JsonDocument))
            return JsonDocument.Parse($$"""{"{{path}}":{{Next()}}}""");

        if (type.IsEnum)
            return NonDefaultEnumValue(type);

        if (type.IsPrimitive || type == typeof(decimal))
            return Convert.ChangeType(Next(), type, CultureInfo.InvariantCulture);

        if (ElementTypeOf(type) is { } elementType)
            return CreateList(type, elementType, path, depth);

        var constructor = WorkflowStepPayloads.PayloadConstructorOrNull(type)
            ?? type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"{path} : {type.Name} cannot be constructed — no accessible constructor.");

        return CreateFromConstructor(type, constructor, path, depth);
    }

    private object CreateFromConstructor(Type type, ConstructorInfo constructor, string path, int depth)
    {
        var arguments = constructor.GetParameters()
            .Select(p => Create(p.ParameterType, $"{path}.{p.Name}", depth + 1))
            .ToArray();

        return constructor.Invoke(arguments)
            ?? throw new InvalidOperationException($"{path} : {type.Name} produced no instance.");
    }

    private object CreateList(Type type, Type elementType, string path, int depth)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        for (var i = 0; i < 2; i++)
            list.Add(Create(elementType, $"{path}[{i}]", depth + 1));

        if (!type.IsArray)
            return list;

        var array = Array.CreateInstance(elementType, list.Count);
        list.CopyTo(array, 0);
        return array;
    }

    private int Next() => ++_seed;

    // The declared collection shapes a payload may use: arrays, List<T> and the read-only interfaces the
    // architecture rule allows. Everything else falls through to constructor binding.
    private static Type? ElementTypeOf(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (!type.IsGenericType)
            return null;

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(List<>)
            || definition == typeof(IReadOnlyList<>)
            || definition == typeof(IReadOnlyCollection<>)
            || definition == typeof(IList<>)
            || definition == typeof(ICollection<>)
            || definition == typeof(IEnumerable<>)
                ? type.GetGenericArguments()[0]
                : null;
    }

    private static object NonDefaultEnumValue(Type type)
    {
        var values = Enum.GetValues(type).Cast<object>().ToArray();
        return values.FirstOrDefault(v => Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0)
            ?? values[0];
    }
}
