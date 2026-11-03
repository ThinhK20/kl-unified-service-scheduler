using Scheduler.Domain.Enums;

namespace Scheduler.Infrastructure.Seeding;

/// <summary>
/// Demo data with fixed ids (SPEC section 4). It is applied with EF <c>HasData</c>, so it is part of the
/// migrations and is idempotent: running the migrations again never adds duplicates.
/// Anonymous objects are used because the domain entities have private setters.
/// </summary>
public static class SeedData
{
    public const string TimeZoneId = "Asia/Ho_Chi_Minh";

    /// <summary>The first demo customer (An Nguyen); the guided demo books as this customer.</summary>
    public const int DemoCustomerId = CustomerIds.AnNguyen;

    public static class CustomerIds
    {
        public const int AnNguyen = 1;
        public const int BinhTran = 2;
        public const int ChiLe = 3;
    }

    public static class DealershipIds
    {
        public const int District1 = 1;
        public const int ThuDuc = 2;
    }

    public static class ServiceTypeIds
    {
        public const int OilChangeDistrict1 = 1;
        public const int BrakeInspection = 2;
        public const int TyreRotation = 3;
        public const int EvBatteryCheck = 4;
        public const int OilChangeThuDuc = 5;
    }

    public static class TechnicianIds
    {
        public const int Nam = 1;
        public const int Lan = 2;
        public const int Minh = 3;
        public const int Hoa = 4;
        public const int Tuan = 5; // inactive
    }

    public static class BayIds
    {
        public const int Bay1Standard = 1;
        public const int Bay2Lift = 2;
        public const int Bay3Ev = 3;
        public const int BayAStandard = 4;
        public const int Bay4LiftInactive = 5;
    }

    public static class VehicleIds
    {
        public const int ToyotaVios = 1;
        public const int HondaCrv = 2;
        public const int VinFastVf8 = 3;
        public const int MazdaCx5 = 4;
        public const int KiaMorning = 5;
    }

    internal static object[] Customers =>
    [
        new { Id = CustomerIds.AnNguyen, Name = "An Nguyen", Email = "an.nguyen@example.com", Phone = "+84 900 000 001" },
        new { Id = CustomerIds.BinhTran, Name = "Binh Tran", Email = "binh.tran@example.com", Phone = "+84 900 000 002" },
        new { Id = CustomerIds.ChiLe, Name = "Chi Le", Email = "chi.le@example.com", Phone = "+84 900 000 003" },
    ];

    internal static object[] Vehicles =>
    [
        new { Id = VehicleIds.ToyotaVios, CustomerId = CustomerIds.AnNguyen, Vin = "JTDBT923X71000001", Make = "Toyota", Model = "Vios", Year = 2021 },
        new { Id = VehicleIds.HondaCrv, CustomerId = CustomerIds.AnNguyen, Vin = "7FARW2H58PE000002", Make = "Honda", Model = "CR-V", Year = 2023 },
        new { Id = VehicleIds.VinFastVf8, CustomerId = CustomerIds.BinhTran, Vin = "RLLV8EAA5RV000003", Make = "VinFast", Model = "VF 8", Year = 2024 },
        new { Id = VehicleIds.MazdaCx5, CustomerId = CustomerIds.ChiLe, Vin = "JM3KFBCM1N0000004", Make = "Mazda", Model = "CX-5", Year = 2022 },
        new { Id = VehicleIds.KiaMorning, CustomerId = CustomerIds.ChiLe, Vin = "KNABE511AL0000005", Make = "Kia", Model = "Morning", Year = 2020 },
    ];

    internal static object[] Dealerships =>
    [
        new
        {
            Id = DealershipIds.District1, Name = "District 1 Service Centre", TimeZoneId,
            OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(17, 0), IsActive = true,
        },
        new
        {
            Id = DealershipIds.ThuDuc, Name = "Thu Duc Service Centre", TimeZoneId,
            OpenTime = new TimeOnly(7, 30), CloseTime = new TimeOnly(18, 0), IsActive = true,
        },
    ];

    internal static object[] ServiceTypes =>
    [
        Service(ServiceTypeIds.OilChangeDistrict1, DealershipIds.District1, "Oil change", 60, Skill.General, SkillTier.Trainee, BayType.Standard),
        Service(ServiceTypeIds.BrakeInspection, DealershipIds.District1, "Brake inspection", 90, Skill.Brakes, SkillTier.Qualified, BayType.Lift),
        Service(ServiceTypeIds.TyreRotation, DealershipIds.District1, "Tyre rotation", 30, Skill.Tyres, SkillTier.Trainee, BayType.Lift),
        Service(ServiceTypeIds.EvBatteryCheck, DealershipIds.District1, "EV battery check", 60, Skill.EV, SkillTier.Senior, BayType.EV),
        Service(ServiceTypeIds.OilChangeThuDuc, DealershipIds.ThuDuc, "Oil change", 60, Skill.General, SkillTier.Trainee, BayType.Standard),
    ];

    internal static object[] Technicians =>
    [
        Tech(TechnicianIds.Nam, DealershipIds.District1, "Nam", new TimeOnly(8, 0), new TimeOnly(17, 0), isActive: true),
        Tech(TechnicianIds.Lan, DealershipIds.District1, "Lan", new TimeOnly(8, 0), new TimeOnly(17, 0), isActive: true),
        Tech(TechnicianIds.Minh, DealershipIds.District1, "Minh", new TimeOnly(12, 0), new TimeOnly(17, 0), isActive: true),
        Tech(TechnicianIds.Hoa, DealershipIds.ThuDuc, "Hoa", new TimeOnly(7, 30), new TimeOnly(18, 0), isActive: true),
        Tech(TechnicianIds.Tuan, DealershipIds.District1, "Tuan", new TimeOnly(8, 0), new TimeOnly(17, 0), isActive: false),
    ];

    internal static object[] TechnicianSkills =>
    [
        SkillRow(TechnicianIds.Nam, Skill.General, SkillTier.Master),
        SkillRow(TechnicianIds.Nam, Skill.Brakes, SkillTier.Senior),
        SkillRow(TechnicianIds.Nam, Skill.Tyres, SkillTier.Senior),
        SkillRow(TechnicianIds.Lan, Skill.General, SkillTier.Qualified),
        SkillRow(TechnicianIds.Lan, Skill.Tyres, SkillTier.Qualified),
        SkillRow(TechnicianIds.Lan, Skill.EV, SkillTier.Senior),
        SkillRow(TechnicianIds.Minh, Skill.General, SkillTier.Trainee),
        SkillRow(TechnicianIds.Minh, Skill.Brakes, SkillTier.Qualified),
        SkillRow(TechnicianIds.Hoa, Skill.General, SkillTier.Senior),
        SkillRow(TechnicianIds.Tuan, Skill.General, SkillTier.Master),
        SkillRow(TechnicianIds.Tuan, Skill.Brakes, SkillTier.Master),
        SkillRow(TechnicianIds.Tuan, Skill.EV, SkillTier.Master),
    ];

    internal static object[] ServiceBays =>
    [
        Bay(BayIds.Bay1Standard, DealershipIds.District1, "Bay 1", BayType.Standard, isActive: true),
        Bay(BayIds.Bay2Lift, DealershipIds.District1, "Bay 2", BayType.Lift, isActive: true),
        Bay(BayIds.Bay3Ev, DealershipIds.District1, "Bay 3", BayType.EV, isActive: true),
        Bay(BayIds.BayAStandard, DealershipIds.ThuDuc, "Bay A", BayType.Standard, isActive: true),
        Bay(BayIds.Bay4LiftInactive, DealershipIds.District1, "Bay 4", BayType.Lift, isActive: false),
    ];

    private static object Service(
        int id, int dealershipId, string name, int durationMinutes, Skill skill, SkillTier minimumTier, BayType bayType) =>
        new
        {
            Id = id,
            DealershipId = dealershipId,
            Name = name,
            DurationMinutes = durationMinutes,
            RequiredSkill = skill,
            MinimumTier = minimumTier,
            RequiredBayType = bayType,
            IsActive = true,
        };

    private static object Tech(int id, int dealershipId, string name, TimeOnly shiftStart, TimeOnly shiftEnd, bool isActive) =>
        new { Id = id, DealershipId = dealershipId, Name = name, ShiftStart = shiftStart, ShiftEnd = shiftEnd, IsActive = isActive };

    private static object SkillRow(int technicianId, Skill skill, SkillTier tier) =>
        new { TechnicianId = technicianId, Skill = skill, Tier = tier };

    private static object Bay(int id, int dealershipId, string name, BayType bayType, bool isActive) =>
        new { Id = id, DealershipId = dealershipId, Name = name, BayType = bayType, IsActive = isActive };
}
