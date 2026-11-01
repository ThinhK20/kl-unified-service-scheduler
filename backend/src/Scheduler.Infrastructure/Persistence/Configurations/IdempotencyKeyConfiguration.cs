using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Infrastructure.Persistence.Entities;

namespace Scheduler.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("IdempotencyKeys");
        builder.HasKey(k => k.Key).HasName(DbConstraintNames.IdempotencyKeysPrimaryKey);
        // Binary collation: keys are compared exactly, so "abc" and "ABC" are different keys
        // (the database default collation is case-insensitive).
        builder.Property(k => k.Key)
            .HasMaxLength(IdempotencyKey.KeyMaxLength)
            .UseCollation(IdempotencyKey.KeyCollation)
            .ValueGeneratedNever();
        builder.Property(k => k.RequestHash).HasMaxLength(IdempotencyKey.RequestHashMaxLength).IsRequired();
        builder.Property(k => k.ResponseJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(k => k.CreatedUtc).IsRequired();

        builder.HasOne(k => k.Appointment)
            .WithMany()
            .HasForeignKey(k => k.AppointmentId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
