using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Domain.Entities;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.Infrastructure.Persistence.Configurations;

internal sealed class ServiceTypeConfiguration : IEntityTypeConfiguration<ServiceType>
{
    public void Configure(EntityTypeBuilder<ServiceType> builder)
    {
        builder.ToTable("ServiceTypes", t =>
            t.HasCheckConstraint(
                "CK_ServiceTypes_DurationOnGrid",
                "[DurationMinutes] > 0 AND [DurationMinutes] % 15 = 0"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.DurationMinutes).IsRequired();
        builder.Property(s => s.RequiredSkill).IsRequired();
        builder.Property(s => s.MinimumTier).IsRequired();
        builder.Property(s => s.RequiredBayType).IsRequired();
        builder.Property(s => s.IsActive).IsRequired();

        builder.Ignore(s => s.Duration);

        builder.HasOne<Dealership>()
            .WithMany()
            .HasForeignKey(s => s.DealershipId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(SeedData.ServiceTypes);
    }
}
