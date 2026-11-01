namespace Scheduler.Infrastructure.Persistence;

/// <summary>
/// Database index and constraint names that code depends on. The configurations use them to name the objects,
/// and <see cref="Repositories.SqlErrorClassifier"/> uses them to tell which unique rule was broken.
/// </summary>
internal static class DbConstraintNames
{
    /// <summary>UNIQUE (ResourceType, ResourceId, SlotStartUtc): the rule that makes double booking impossible.</summary>
    public const string UniqueResourceSlot = "UX_ResourceSlots_Resource_Slot";

    /// <summary>Primary key of IdempotencyKeys (Key).</summary>
    public const string IdempotencyKeysPrimaryKey = "PK_IdempotencyKeys";
}
