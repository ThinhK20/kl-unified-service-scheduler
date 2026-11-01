using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Domain.Entities;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.Infrastructure.Persistence.Configurations;

internal sealed class TechnicianSkillConfiguration : IEntityTypeConfiguration<TechnicianSkill>
{
    public void Configure(EntityTypeBuilder<TechnicianSkill> builder)
    {
        builder.ToTable("TechnicianSkills");
        builder.HasKey(s => new { s.TechnicianId, s.Skill });
        builder.Property(s => s.Tier).IsRequired();

        builder.HasData(SeedData.TechnicianSkills);
    }
}
