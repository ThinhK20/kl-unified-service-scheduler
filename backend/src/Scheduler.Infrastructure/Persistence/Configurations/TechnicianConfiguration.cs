using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Domain.Entities;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.Infrastructure.Persistence.Configurations;

internal sealed class TechnicianConfiguration : IEntityTypeConfiguration<Technician>
{
    public void Configure(EntityTypeBuilder<Technician> builder)
    {
        builder.ToTable("Technicians", t =>
            t.HasCheckConstraint("CK_Technicians_ShiftEndAfterStart", "[ShiftEnd] > [ShiftStart]"));
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.ShiftStart).IsRequired();
        builder.Property(t => t.ShiftEnd).IsRequired();
        builder.Property(t => t.IsActive).IsRequired();

        builder.HasOne<Dealership>()
            .WithMany()
            .HasForeignKey(t => t.DealershipId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // Skills are part of the technician (backing field _skills). Deleting a technician deletes its skill rows;
        // this is the only other cascade and it cannot form multiple cascade paths.
        builder.HasMany(t => t.Skills)
            .WithOne()
            .HasForeignKey(s => s.TechnicianId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Skills).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasData(SeedData.Technicians);
    }
}
