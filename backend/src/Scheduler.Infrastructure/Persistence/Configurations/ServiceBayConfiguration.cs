using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Domain.Entities;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.Infrastructure.Persistence.Configurations;

internal sealed class ServiceBayConfiguration : IEntityTypeConfiguration<ServiceBay>
{
    public void Configure(EntityTypeBuilder<ServiceBay> builder)
    {
        builder.ToTable("ServiceBays");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedOnAdd();
        builder.Property(b => b.Name).HasMaxLength(100).IsRequired();
        builder.Property(b => b.BayType).IsRequired();
        builder.Property(b => b.IsActive).IsRequired();

        builder.HasOne<Dealership>()
            .WithMany()
            .HasForeignKey(b => b.DealershipId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(SeedData.ServiceBays);
    }
}
