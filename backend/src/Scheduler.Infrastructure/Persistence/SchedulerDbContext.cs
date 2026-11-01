using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;
using Scheduler.Infrastructure.Persistence.Conventions;
using Scheduler.Infrastructure.Persistence.Entities;

namespace Scheduler.Infrastructure.Persistence;

/// <summary>EF Core context for the scheduler database (SQL Server). Table mapping lives in Configurations/.</summary>
public sealed class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : DbContext(options)
{
    /// <summary>Max length of every enum column (enums are stored as strings).</summary>
    public const int EnumMaxLength = 30;

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Dealership> Dealerships => Set<Dealership>();

    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();

    public DbSet<Technician> Technicians => Set<Technician>();

    public DbSet<TechnicianSkill> TechnicianSkills => Set<TechnicianSkill>();

    public DbSet<ServiceBay> ServiceBays => Set<ServiceBay>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<ResourceSlot> ResourceSlots => Set<ResourceSlot>();

    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchedulerDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All times are UTC; read them back as DateTimeKind.Utc so JSON gets a "Z".
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

        // Enums are stored as readable strings.
        configurationBuilder.Properties<Skill>().HaveConversion<string>().HaveMaxLength(EnumMaxLength);
        configurationBuilder.Properties<SkillTier>().HaveConversion<string>().HaveMaxLength(EnumMaxLength);
        configurationBuilder.Properties<BayType>().HaveConversion<string>().HaveMaxLength(EnumMaxLength);
        configurationBuilder.Properties<AppointmentStatus>().HaveConversion<string>().HaveMaxLength(EnumMaxLength);
        configurationBuilder.Properties<ResourceType>().HaveConversion<string>().HaveMaxLength(EnumMaxLength);
    }
}
