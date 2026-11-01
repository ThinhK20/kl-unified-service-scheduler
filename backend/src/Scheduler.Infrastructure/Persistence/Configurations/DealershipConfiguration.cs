using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Domain.Entities;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.Infrastructure.Persistence.Configurations;

internal sealed class DealershipConfiguration : IEntityTypeConfiguration<Dealership>
{
    public void Configure(EntityTypeBuilder<Dealership> builder)
    {
        builder.ToTable("Dealerships", t =>
            t.HasCheckConstraint("CK_Dealerships_CloseAfterOpen", "[CloseTime] > [OpenTime]"));
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();
        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(d => d.OpenTime).IsRequired();
        builder.Property(d => d.CloseTime).IsRequired();
        builder.Property(d => d.IsActive).IsRequired();

        // TimeZone is computed from TimeZoneId and cached in the _timeZone field; ignoring the property
        // also keeps EF away from that field.
        builder.Ignore(d => d.TimeZone);

        builder.HasData(SeedData.Dealerships);
    }
}
