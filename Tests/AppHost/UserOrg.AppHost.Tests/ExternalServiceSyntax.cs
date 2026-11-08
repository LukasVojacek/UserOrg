using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.AppHost.Tests;

static class ExternalServiceSyntax
{
    public static readonly string[] GateMethods =
    [
        "WaitFor",
        "WaitForStart",
        "WaitForCompletion",
        "WaitForExit",
        "WaitForHealthy"
    ];

    public static Model AppHost { get; } = Read(ResourceCommandSyntax.AllRoots);

    public static Model Read(IReadOnlyDictionary<string, CompilationUnitSyntax> roots)
    {
        var registrations = roots
            .SelectMany(file => file.Value.Calls("AddExternalService").Select(call => new Registration(file.Key, call)))
            .ToList();

        return new Model(
            registrations,
            ReadIdentifiers(roots, registrations),
            ReadEnvironmentAssignments(roots),
            ReadWaitEdges(roots));
    }

    public static Model ReadSources(IReadOnlyDictionary<string, string> files) =>
        Read(files.ToDictionary(
            file => file.Key,
            file => (CompilationUnitSyntax)AppHostSource.Parse(file.Value).GetRoot()));

    private static IReadOnlyList<string> ReadIdentifiers(
        IReadOnlyDictionary<string, CompilationUnitSyntax> roots,
        IReadOnlyList<Registration> registrations)
    {
        var fromRegistrations = registrations
            .Select(registration => registration.Variable)
            .Where(variable => !string.IsNullOrEmpty(variable))
            .Select(variable => variable!);

        var fromDeclaredTypes = roots.Values
            .SelectMany(root => root.DescendantNodes())
            .SelectMany(node => node switch
            {
                ParameterSyntax parameter when Mentions(parameter.Type) => [parameter.Identifier.ValueText],
                VariableDeclarationSyntax declaration when Mentions(declaration.Type) =>
                    declaration.Variables.Select(variable => variable.Identifier.ValueText),
                PropertyDeclarationSyntax property when Mentions(property.Type) => [property.Identifier.ValueText],
                _ => Array.Empty<string>()
            });

        return [.. fromRegistrations.Concat(fromDeclaredTypes).Distinct()];

        static bool Mentions(TypeSyntax? type) =>
            type is not null && type.ToString().Contains("ExternalServiceResource", StringComparison.Ordinal);
    }

    private static IReadOnlyList<EnvironmentAssignment> ReadEnvironmentAssignments(
        IReadOnlyDictionary<string, CompilationUnitSyntax> roots)
    {
        var assignments = new List<EnvironmentAssignment>();

        foreach (var (file, root) in roots)
        {
            foreach (var call in root.Calls("WithEnvironment"))
            {
                if (call.ArgumentList.Arguments.Count < 2)
                {
                    continue;
                }

                var name = AppHostSyntax.StringValue(call.ArgumentList.Arguments[0].Expression);

                if (name is not null)
                {
                    assignments.Add(new EnvironmentAssignment(file, name, call.ArgumentList.Arguments[1].Expression, call));
                }
            }

            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is not ElementAccessExpressionSyntax access
                    || !access.Expression.ToString().Contains("EnvironmentVariables", StringComparison.Ordinal)
                    || access.ArgumentList.Arguments.Count != 1)
                {
                    continue;
                }

                var name = AppHostSyntax.StringValue(access.ArgumentList.Arguments[0].Expression);

                if (name is not null)
                {
                    assignments.Add(new EnvironmentAssignment(file, name, assignment.Right, assignment));
                }
            }
        }

        return assignments;
    }

    private static IReadOnlyList<WaitEdge> ReadWaitEdges(IReadOnlyDictionary<string, CompilationUnitSyntax> roots) =>
        [.. roots.SelectMany(file => GateMethods
            .SelectMany(method => file.Value.Calls(method).Select(call => new WaitEdge(file.Key, method, call))))];

    public sealed record Model(
        IReadOnlyList<Registration> Registrations,
        IReadOnlyList<string> Identifiers,
        IReadOnlyList<EnvironmentAssignment> EnvironmentAssignments,
        IReadOnlyList<WaitEdge> WaitEdges)
    {
        public string Inventory =>
            Registrations.Count == 0
                ? "the AppHost registers no external service at all"
                : "registered external services: "
                    + string.Join("; ", Registrations.Select(registration => $"{registration.Name} <- {registration.UrlCode}"));

        /// <summary>
        /// The whole <c>AddParameter("name", ...)</c> call for the parameter an expression reads, or null
        /// when the expression names no parameter declared in the app host.
        /// </summary>
        public string? DeclarationOf(string expression)
        {
            foreach (var call in ResourceCommandSyntax.AllRoots.Values.SelectMany(root => root.Calls("AddParameter")))
            {
                var name = call.ArgumentList.Arguments.FirstOrDefault()?.ToString().Trim('"', ' ');

                if (name is { Length: > 0 } && expression.MentionsWord(name))
                {
                    return call.ArgumentList.Code();
                }
            }

            return null;
        }

        /// <summary>
        /// Parameter expressions read with <c>GetValueAsync</c> inside a <c>WithUrls</c> callback - the
        /// endpoint-URL publishing window DcpExecutor awaits before it creates any resource.
        /// </summary>
        public IReadOnlyList<string> ParametersReadInWithUrlsCallbacks()
        {
            var found = new List<string>();

            foreach (var call in ResourceCommandSyntax.AllRoots.Values.SelectMany(root => root.Calls("WithUrls")))
            {
                foreach (var read in call.Calls("GetValueAsync"))
                {
                    var receiver = (read.Expression as MemberAccessExpressionSyntax)?.Expression.ToString();

                    if (receiver is { Length: > 0 } && !found.Contains(receiver, StringComparer.Ordinal))
                    {
                        found.Add(receiver);
                    }
                }
            }

            return found;
        }

        public IReadOnlyList<Registration> FedBy(params string[] tokens) =>
            [.. Registrations.Where(registration => tokens.Any(token => registration.UrlCode.MentionsWord(token)))];

        public Registration ForUrlFrom(string subject, params string[] tokens)
        {
            var matches = FedBy(tokens);

            Assert.True(
                matches.Count == 1,
                $"expected exactly one `AddExternalService` registration for {subject}, taking its URL from {string.Join(" or ", tokens)}, found {matches.Count} ({Inventory})");

            return matches[0];
        }

        public Registration ForNameContaining(string subject, params string[] tokens)
        {
            var matches = Registrations
                .Where(registration => registration.Name is { } name
                    && tokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            Assert.True(
                matches.Count == 1,
                $"expected exactly one `AddExternalService` registration for {subject}, named after {string.Join(" or ", tokens)}, found {matches.Count} ({Inventory})");

            return matches[0];
        }

        public bool ReferencesAnExternalService(string code) =>
            Identifiers.Any(identifier => code.MentionsWord(identifier));

        public IReadOnlyList<EnvironmentAssignment> Environment(string name) =>
            [.. EnvironmentAssignments.Where(assignment => assignment.Name == name)];

        public EnvironmentAssignment SingleEnvironment(string name)
        {
            var matches = Environment(name);

            Assert.True(
                matches.Count == 1,
                $"expected exactly one assignment of the `{name}` environment variable in the AppHost, found {matches.Count}");

            return matches[0];
        }

        public IReadOnlyList<WaitEdge> GatesOn(Registration registration) =>
            [.. WaitEdges.Where(edge =>
                (registration.Variable is { } variable && edge.ArgumentCode.MentionsWord(variable))
                || (registration.Name is { } name && edge.ArgumentCode.MentionsWord(name)))];
    }

    public sealed record Registration(string File, InvocationExpressionSyntax Call)
    {
        public string? Name =>
            Call.ArgumentList.Arguments.Count > 0
                ? AppHostSyntax.StringValue(Call.ArgumentList.Arguments[0].Expression)
                : null;

        public ExpressionSyntax? UrlArgument =>
            Call.ArgumentList.Arguments.Count > 1 ? Call.ArgumentList.Arguments[1].Expression : null;

        public string UrlCode => UrlArgument?.Code() ?? string.Empty;

        public string? Variable => Call.AssignedVariable();

        public IReadOnlyList<InvocationExpressionSyntax> HealthChecks => Call.AppliedCalls("WithHttpHealthCheck");

        public string HealthCheckCode =>
            string.Join('\n', HealthChecks.Select(check => check.ArgumentList.Code()));

        public IReadOnlyList<string> UrlLiterals =>
            UrlArgument is null
                ? []
                : [.. UrlArgument.DescendantNodesAndSelf()
                    .OfType<LiteralExpressionSyntax>()
                    .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                    .Select(literal => literal.Token.ValueText)];

        public string Label => $"the `{Name ?? "<unnamed>"}` external service in {File} ({Call.Describe()})";
    }

    public sealed record EnvironmentAssignment(string File, string Name, ExpressionSyntax Value, SyntaxNode Node)
    {
        public string ValueCode => Value.Code();

        public string? Receiver => (Node as InvocationExpressionSyntax)?.Receiver();

        public bool IsInsideCallback => Node.IsInsideCallback();

        public string ScopeCode =>
            (Node.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().LastOrDefault() ?? Node).Code();

        public ExpressionSyntax LocalDatabaseArm => Arm(local: true);

        public ExpressionSyntax RemoteDatabaseArm => Arm(local: false);

        public string Label => $"`{Name}` in {File} ({Node.Describe()})";

        private ExpressionSyntax Arm(bool local)
        {
            if (Value is not ConditionalExpressionSyntax conditional
                || !conditional.Condition.Code().Contains("LocalContainer", StringComparison.OrdinalIgnoreCase))
            {
                return Value;
            }

            return local ? conditional.WhenTrue : conditional.WhenFalse;
        }
    }

    public sealed record WaitEdge(string File, string Method, InvocationExpressionSyntax Call)
    {
        public string ArgumentCode => Call.ArgumentList.Code();

        public string? Receiver => Call.Receiver();

        public string Label => $"`{Receiver}.{Method}{ArgumentCode}` in {File} ({Call.Describe()})";
    }
}
