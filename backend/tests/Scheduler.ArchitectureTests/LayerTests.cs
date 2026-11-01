using System.Reflection;
using NetArchTest.Rules;
using Scheduler.Application.Abstractions;
using Scheduler.Domain.Entities;
using Scheduler.Infrastructure.Time;

namespace Scheduler.ArchitectureTests;

/// <summary>Layer rules from SPEC section 3: Domain → nothing, Application → Domain, Infrastructure → Application + Domain.</summary>
public class LayerTests
{
    private const string Domain = "Scheduler.Domain";
    private const string Application = "Scheduler.Application";
    private const string Infrastructure = "Scheduler.Infrastructure";
    private const string Api = "Scheduler.Api";
    private const string EntityFrameworkCore = "Microsoft.EntityFrameworkCore";

    private static readonly Assembly DomainAssembly = typeof(Appointment).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IClock).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(SystemClock).Assembly;

    [Fact]
    public void Domain_does_not_depend_on_other_layers_or_ef_core()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Application, Infrastructure, Api, EntityFrameworkCore, "Microsoft.AspNetCore", "FluentValidation")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Domain_assembly_references_only_the_base_class_library()
    {
        var references = DomainAssembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        references.Should().OnlyContain(
            name => name.StartsWith("System", StringComparison.Ordinal) || name == "netstandard" || name == "mscorlib",
            "the Domain must reference nothing else: {0}",
            string.Join(", ", references));
    }

    [Fact]
    public void Application_does_not_depend_on_ef_core_infrastructure_or_api()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Infrastructure, Api, EntityFrameworkCore, "Microsoft.AspNetCore", "Microsoft.Data.SqlClient")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Application_assembly_does_not_reference_ef_core_or_outer_layers()
    {
        var references = ApplicationAssembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        references.Should().NotContain(name =>
            name.StartsWith(EntityFrameworkCore, StringComparison.Ordinal)
            || name == Infrastructure
            || name == Api);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(Api)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Domain_entities_have_no_public_setters()
    {
        var withPublicSetters = DomainAssembly.GetTypes()
            .Where(t => t.Namespace == $"{Domain}.Entities")
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToList();

        withPublicSetters.Should().BeEmpty();
    }

    private static string Describe(TestResult result) =>
        result.IsSuccessful ? string.Empty : "these types break the rule: " + string.Join(", ", result.FailingTypeNames ?? []);
}
