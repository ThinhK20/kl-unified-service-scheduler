using Scheduler.Domain.Enums;

namespace Scheduler.Application.Abstractions;

/// <summary>A slot row that is already taken (projection of ResourceSlots).</summary>
public sealed record TakenSlot(ResourceType ResourceType, int ResourceId, DateTime SlotStartUtc);
