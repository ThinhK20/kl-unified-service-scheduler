using System.Reflection;
using Scheduler.Domain.Common;

namespace Scheduler.Application.Tests.TestSupport;

/// <summary>Sets private state the database would set (ids, loaded navigation properties).</summary>
internal static class Reflect
{
    public static T WithId<T>(this T entity, int id)
        where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.GetSetMethod(nonPublic: true)!.Invoke(entity, [id]);
        return entity;
    }

    public static void SetProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"No property {propertyName} on {target.GetType().Name}.");
        property.GetSetMethod(nonPublic: true)!.Invoke(target, [value]);
    }
}
