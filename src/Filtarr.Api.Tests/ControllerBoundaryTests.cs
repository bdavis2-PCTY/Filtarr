using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Filtarr.Api.Tests;

/// <summary>The API must only exchange Filtarr.Core models with the UI: no database entity may appear in an action's signature.</summary>
public class ControllerBoundaryTests
{
    static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.HasElementType) foreach (var t in Flatten(type.GetElementType()!)) yield return t;
        if (type.IsGenericType) foreach (var arg in type.GetGenericArguments()) foreach (var t in Flatten(arg)) yield return t;
    }

    static bool IsDatabaseType(Type type) =>
        Flatten(type).Any(t => t.Namespace?.StartsWith("Filtarr.Data", StringComparison.Ordinal) == true);

    [Fact]
    public void No_controller_action_takes_or_returns_a_database_type()
    {
        var controllers = typeof(FiltarrOptions).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract).ToList();
        Assert.NotEmpty(controllers);

        var leaks = controllers
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => IsDatabaseType(m.ReturnType) || m.GetParameters().Any(p => IsDatabaseType(p.ParameterType)))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}");
        Assert.Empty(leaks);
    }

    [Fact]
    public void Controllers_do_not_depend_on_repositories_or_clients_directly()
    {
        var controllers = typeof(FiltarrOptions).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);
        var offenders = controllers
            .SelectMany(c => c.GetConstructors().SelectMany(ctor => ctor.GetParameters()).Select(p => (Controller: c.Name, p.ParameterType)))
            .Where(x => x.ParameterType.Namespace is { } ns && (ns.StartsWith("Filtarr.Data", StringComparison.Ordinal) || ns.StartsWith("Filtarr.Core.Clients", StringComparison.Ordinal)))
            .Select(x => $"{x.Controller} <- {x.ParameterType.Name}");
        Assert.Empty(offenders);
    }
}
