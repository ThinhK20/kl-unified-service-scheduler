using System.Reflection;
using Scheduler.Domain.Common;

namespace Scheduler.Domain.Tests.TestSupport;

/// <summary>Sets the database id of an entity, as EF would after saving.</summary>
internal static class Ids
{
    private static readonly MethodInfo IdSetter =
        typeof(Entity).GetProperty(nameof(Entity.Id))!.GetSetMethod(nonPublic: true)!;

    public static T WithId<T>(this T entity, int id)
        where T : Entity
    {
        IdSetter.Invoke(entity, [id]);
        return entity;
    }
}
