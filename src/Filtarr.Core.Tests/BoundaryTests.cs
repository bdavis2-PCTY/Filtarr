using System.Reflection;
using Filtarr.Core.Services.Interfaces;

namespace Filtarr.Core.Tests;

/// <summary>Guards the rule that database entities never leave Filtarr.Core: nothing public in the library may expose one.</summary>
public class BoundaryTests
{
    static readonly Assembly Core = typeof(ISyncService).Assembly;

    static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.HasElementType) foreach (var t in Flatten(type.GetElementType()!)) yield return t;
        if (type.IsGenericType) foreach (var arg in type.GetGenericArguments()) foreach (var t in Flatten(arg)) yield return t;
    }

    static bool IsDatabaseType(Type type) =>
        Flatten(type).Any(t => t.Namespace?.StartsWith("Filtarr.Data", StringComparison.Ordinal) == true);

    static IEnumerable<string> Leaks(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var m in type.GetMethods(flags).Where(m => !m.IsSpecialName))
        {
            if (IsDatabaseType(m.ReturnType)) yield return $"{type.Name}.{m.Name} returns a database type";
            foreach (var p in m.GetParameters().Where(p => IsDatabaseType(p.ParameterType))) yield return $"{type.Name}.{m.Name}({p.Name}) takes a database type";
        }
        foreach (var p in type.GetProperties(flags).Where(p => IsDatabaseType(p.PropertyType))) yield return $"{type.Name}.{p.Name} is a database type";
        foreach (var f in type.GetFields(flags).Where(f => IsDatabaseType(f.FieldType))) yield return $"{type.Name}.{f.Name} is a database type";
    }

    [Fact]
    public void The_service_client_catalog_and_engine_contracts_expose_no_database_types()
    {
        var contracts = Core.GetExportedTypes().Where(t => t.IsInterface && t.Namespace!.EndsWith(".Interfaces", StringComparison.Ordinal));
        Assert.Empty(contracts.SelectMany(Leaks));
    }

    [Fact]
    public void The_models_exposed_to_the_api_contain_no_database_types()
    {
        var models = Core.GetExportedTypes().Where(t => t.Namespace == "Filtarr.Core.Models");
        Assert.NotEmpty(models);
        Assert.Empty(models.SelectMany(Leaks));
    }

    [Fact]
    public void Every_service_interface_lives_in_an_interfaces_namespace_next_to_its_implementations()
    {
        var misplaced = Core.GetExportedTypes()
            .Where(t => t.IsInterface && t.Name.StartsWith('I') && !t.Namespace!.EndsWith(".Interfaces", StringComparison.Ordinal))
            .Select(t => t.FullName);
        Assert.Empty(misplaced);
    }

    [Fact]
    public void The_services_namespace_contains_only_services()
    {
        var notServices = Core.GetExportedTypes()
            .Where(t => t.Namespace == "Filtarr.Core.Services" && !t.Name.EndsWith("Service", StringComparison.Ordinal)
                        && t.Name is not ("SettingsUpdater" or "SeriesAdder"))
            .Select(t => t.FullName);
        Assert.Empty(notServices);
    }
}
