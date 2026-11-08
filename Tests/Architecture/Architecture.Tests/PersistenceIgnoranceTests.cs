using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Asserts that entity types stay ignorant of how they are persisted: mapping is expressed in
/// <see cref="IEntityTypeConfiguration{TEntity}"/> classes, never as attributes on the entity.
/// </summary>
public sealed class PersistenceIgnoranceTests
{
    private const string EfCore = "Microsoft.EntityFrameworkCore";
    private const string DataAnnotations = "System.ComponentModel.DataAnnotations";

    [Fact]
    public void Core_Domain_types_are_free_of_persistence_concerns()
    {
        var result = Types.InAssembly(SolutionAssemblies.Load(SolutionAssemblies.CoreDomain))
            .ShouldNot()
            .HaveDependencyOnAny(EfCore, DataAnnotations)
            .GetResult();

        AssertSuccess(result, "Core Domain must model the business, not its storage — map it from Infrastructure.DataAccess instead");
    }

    [Fact]
    public void Heappe_persisted_entities_are_free_of_persistence_concerns()
    {
        var entities = Types.InAssembly(SolutionAssemblies.Load("Heappe"))
            .That()
            .ResideInNamespace("Heappe.Persistence.Entities");

        // Guard against the rule passing vacuously if the namespace is ever renamed.
        Assert.NotEmpty(entities.GetTypes());

        var result = entities.ShouldNot().HaveDependencyOnAny(EfCore, DataAnnotations).GetResult();

        AssertSuccess(result, "Heappe entities must stay mapping-free — express it in Persistence/Configurations instead");
    }

    /// <summary>
    /// A module owns its persistence outright: its <see cref="DbContext"/> is an implementation
    /// detail, reachable only through the module's own registration extension and contracts.
    /// </summary>
    [Theory]
    [InlineData("Heappe")]
    [InlineData("Forge.Core")]
    public void A_module_does_not_expose_its_DbContext(string module)
    {
        var contexts = SolutionAssemblies.Load(module)
            .GetTypes()
            .Where(typeof(DbContext).IsAssignableFrom)
            .ToArray();

        // Guard against the rule passing vacuously if the module's DbContext moves or is renamed.
        Assert.NotEmpty(contexts);

        var exposed = contexts.Where(type => type.IsPublic).Select(type => type.FullName).ToArray();

        Assert.True(
            exposed.Length == 0,
            $"Module {module} owns its persistence privately, but these DbContexts are public: {string.Join(", ", exposed)}");
    }

    /// <summary>
    /// Every module DbContext discovers its mappings from the module's own assembly, so an entity
    /// belonging to one module can never be mapped by another module's context.
    /// </summary>
    [Theory]
    [InlineData("Heappe")]
    [InlineData("Forge.Core")]
    public void Module_entity_configurations_live_in_the_module_that_owns_them(string module)
    {
        var assembly = SolutionAssemblies.Load(module);

        var configurations = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .SelectMany(type => type.GetInterfaces(), (type, contract) => (type, contract))
            .Where(pair => pair.contract.IsGenericType
                && pair.contract.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
            .ToArray();

        Assert.NotEmpty(configurations);

        foreach (var (configuration, contract) in configurations)
        {
            var entity = contract.GetGenericArguments()[0];
            Assert.True(
                entity.Assembly == assembly,
                $"{configuration.FullName} maps {entity.FullName}, which belongs to {entity.Assembly.GetName().Name} — a module must only map entities it owns");
        }
    }

    private static void AssertSuccess(NetArchTest.Rules.TestResult result, string because)
    {
        var offenders = result.FailingTypeNames ?? [];
        Assert.True(result.IsSuccessful, $"{because}. Offending types: {string.Join(", ", offenders)}");
    }
}
