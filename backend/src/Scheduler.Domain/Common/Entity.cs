namespace Scheduler.Domain.Common;

/// <summary>Base class for entities with an int identity set by the database.</summary>
public abstract class Entity
{
    /// <summary>Database id. It is 0 until the entity is saved.</summary>
    public int Id { get; protected set; }
}
