using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Domain.Entities;

namespace Scheduler.Infrastructure.Persistence.Configurations;

internal sealed class ResourceSlotConfiguration : IEntityTypeConfiguration<ResourceSlot>
{
    public void Configure(EntityTypeBuilder<ResourceSlot> builder)
    {
        builder.ToTable("ResourceSlots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();
        builder.Property(s => s.ResourceType).IsRequired();
        builder.Property(s => s.ResourceId).IsRequired();
        builder.Property(s => s.SlotStartUtc).IsRequired();

        // ResourceId points to a technician or a bay depending on ResourceType, so it has no foreign key.
        builder.HasIndex(s => new { s.ResourceType, s.ResourceId, s.SlotStartUtc })
            .IsUnique()
            .HasDatabaseName(DbConstraintNames.UniqueResourceSlot);

        builder.HasIndex(s => s.AppointmentId).HasDatabaseName("IX_ResourceSlots_AppointmentId");
    }
}
