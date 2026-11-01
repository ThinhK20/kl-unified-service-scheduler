using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Domain.Entities;

namespace Scheduler.Infrastructure.Persistence.Configurations;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments", t =>
            t.HasCheckConstraint("CK_Appointments_EndAfterStart", "[EndUtc] > [StartUtc]"));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();
        builder.Property(a => a.StartUtc).IsRequired();
        builder.Property(a => a.EndUtc).IsRequired();
        builder.Property(a => a.Status).IsRequired();
        builder.Property(a => a.CreatedUtc).IsRequired();
        builder.Property(a => a.DurationSnapshot).IsRequired();
        builder.Property(a => a.RequiredSkillSnapshot).IsRequired();
        builder.Property(a => a.RequiredBayTypeSnapshot).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.Ignore(a => a.Range);
        builder.Ignore(a => a.CanBeCancelled);

        // Lookups. No cascades: deleting a customer, dealership, etc. never deletes appointments.
        builder.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Vehicle).WithMany().HasForeignKey(a => a.VehicleId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Dealership).WithMany().HasForeignKey(a => a.DealershipId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.ServiceType).WithMany().HasForeignKey(a => a.ServiceTypeId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Technician).WithMany().HasForeignKey(a => a.TechnicianId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.ServiceBay).WithMany().HasForeignKey(a => a.ServiceBayId).IsRequired().OnDelete(DeleteBehavior.Restrict);

        // The appointment owns its slot rows (backing field _resourceSlots). Removing a slot from the collection
        // deletes the row (orphan delete); this is what Cancel() relies on.
        builder.HasMany(a => a.ResourceSlots)
            .WithOne(s => s.Appointment)
            .HasForeignKey(s => s.AppointmentId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.ResourceSlots).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(a => new { a.CustomerId, a.StartUtc }).HasDatabaseName("IX_Appointments_CustomerId_StartUtc");
        builder.HasIndex(a => new { a.TechnicianId, a.StartUtc }).HasDatabaseName("IX_Appointments_TechnicianId_StartUtc");
    }
}
