using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Domain.Tests.TestSupport;

/// <summary>Builds domain objects that look like the SPEC seed data (Asia/Ho_Chi_Minh, UTC+7).</summary>
internal static class Seed
{
    public const string HoChiMinh = "Asia/Ho_Chi_Minh";

    public static Dealership District1() =>
        Dealership.Create("District 1 Service Centre", HoChiMinh, new TimeOnly(8, 0), new TimeOnly(17, 0)).WithId(1);

    public static Vehicle Vios(int customerId = 1) =>
        Vehicle.Create(customerId, "JTDBT923X71000001", "Toyota", "Vios", 2021).WithId(1);

    public static ServiceType OilChange(int dealershipId = 1) =>
        ServiceType.Create(dealershipId, "Oil change", 60, Skill.General, SkillTier.Trainee, BayType.Standard).WithId(1);

    public static Technician Nam(int dealershipId = 1) =>
        Technician.Create(dealershipId, "Nam", new TimeOnly(8, 0), new TimeOnly(17, 0))
            .WithSkill(Skill.General, SkillTier.Master)
            .WithSkill(Skill.Brakes, SkillTier.Senior)
            .WithId(1);

    public static ServiceBay Bay1(int dealershipId = 1) =>
        ServiceBay.Create(dealershipId, "Bay 1", BayType.Standard).WithId(1);
}
