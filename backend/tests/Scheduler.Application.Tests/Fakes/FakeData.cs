using Scheduler.Application.Tests.TestSupport;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Tests.Fakes;

/// <summary>In-memory copy of the SPEC seed data (three demo customers, five vehicles).</summary>
public sealed class FakeData
{
    public const string TimeZone = "Asia/Ho_Chi_Minh";

    public FakeData()
    {
        Customers.Add(Customer.Create("An Nguyen", "an.nguyen@example.com", "+84 900 000 001").WithId(1));
        Customers.Add(Customer.Create("Binh Tran", "binh.tran@example.com", "+84 900 000 002").WithId(2));
        Customers.Add(Customer.Create("Chi Le", "chi.le@example.com", "+84 900 000 003").WithId(3));

        Dealerships.Add(Dealership.Create("District 1 Service Centre", TimeZone, new TimeOnly(8, 0), new TimeOnly(17, 0)).WithId(1));
        Dealerships.Add(Dealership.Create("Thu Duc Service Centre", TimeZone, new TimeOnly(7, 30), new TimeOnly(18, 0)).WithId(2));

        Vehicles.Add(Vehicle.Create(1, "JTDBT923X71000001", "Toyota", "Vios", 2021).WithId(1));
        Vehicles.Add(Vehicle.Create(1, "7FARW2H58PE000002", "Honda", "CR-V", 2023).WithId(2));
        Vehicles.Add(Vehicle.Create(2, "RLLV8EAA5RV000003", "VinFast", "VF 8", 2024).WithId(3));
        Vehicles.Add(Vehicle.Create(3, "JM3KFBCM1N0000004", "Mazda", "CX-5", 2022).WithId(4));
        Vehicles.Add(Vehicle.Create(3, "KNABE511AL0000005", "Kia", "Morning", 2020).WithId(5));

        ServiceTypes.Add(ServiceType.Create(1, "Oil change", 60, Skill.General, SkillTier.Trainee, BayType.Standard).WithId(1));
        ServiceTypes.Add(ServiceType.Create(1, "Brake inspection", 90, Skill.Brakes, SkillTier.Qualified, BayType.Lift).WithId(2));
        ServiceTypes.Add(ServiceType.Create(1, "Tyre rotation", 30, Skill.Tyres, SkillTier.Trainee, BayType.Lift).WithId(3));
        ServiceTypes.Add(ServiceType.Create(1, "EV battery check", 60, Skill.EV, SkillTier.Senior, BayType.EV).WithId(4));
        ServiceTypes.Add(ServiceType.Create(2, "Oil change", 60, Skill.General, SkillTier.Trainee, BayType.Standard).WithId(5));

        Technicians.Add(Tech(1, 1, "Nam", 8, 0, 17, 0, (Skill.General, SkillTier.Master), (Skill.Brakes, SkillTier.Senior), (Skill.Tyres, SkillTier.Senior)));
        Technicians.Add(Tech(2, 1, "Lan", 8, 0, 17, 0, (Skill.General, SkillTier.Qualified), (Skill.Tyres, SkillTier.Qualified), (Skill.EV, SkillTier.Senior)));
        Technicians.Add(Tech(3, 1, "Minh", 12, 0, 17, 0, (Skill.General, SkillTier.Trainee), (Skill.Brakes, SkillTier.Qualified)));
        Technicians.Add(Tech(4, 2, "Hoa", 7, 30, 18, 0, (Skill.General, SkillTier.Senior)));
        var tuan = Tech(5, 1, "Tuan", 8, 0, 17, 0, (Skill.General, SkillTier.Master), (Skill.Brakes, SkillTier.Master), (Skill.EV, SkillTier.Master));
        tuan.Deactivate();
        Technicians.Add(tuan);

        ServiceBays.Add(ServiceBay.Create(1, "Bay 1", BayType.Standard).WithId(1));
        ServiceBays.Add(ServiceBay.Create(1, "Bay 2", BayType.Lift).WithId(2));
        ServiceBays.Add(ServiceBay.Create(1, "Bay 3", BayType.EV).WithId(3));
        ServiceBays.Add(ServiceBay.Create(2, "Bay A", BayType.Standard).WithId(4));
        var bay4 = ServiceBay.Create(1, "Bay 4", BayType.Lift).WithId(5);
        bay4.Deactivate();
        ServiceBays.Add(bay4);
    }

    public List<Customer> Customers { get; } = [];

    public List<Dealership> Dealerships { get; } = [];

    public List<Vehicle> Vehicles { get; } = [];

    public List<ServiceType> ServiceTypes { get; } = [];

    public List<Technician> Technicians { get; } = [];

    public List<ServiceBay> ServiceBays { get; } = [];

    public List<Appointment> Appointments { get; } = [];

    public Dictionary<string, Abstractions.IdempotencyRecord> IdempotencyKeys { get; } = new(StringComparer.Ordinal);

    public Technician GetTechnician(int id) => Technicians.Single(t => t.Id == id);

    public ServiceType GetServiceType(int id) => ServiceTypes.Single(s => s.Id == id);

    public Dealership GetDealership(int id) => Dealerships.Single(d => d.Id == id);

    /// <summary>Fills an appointment's navigation properties, as an EF Include would.</summary>
    public Appointment WithDetails(Appointment appointment)
    {
        Reflect.SetProperty(appointment, nameof(Appointment.Vehicle), Vehicles.Single(v => v.Id == appointment.VehicleId));
        Reflect.SetProperty(appointment, nameof(Appointment.Dealership), GetDealership(appointment.DealershipId));
        Reflect.SetProperty(appointment, nameof(Appointment.ServiceType), GetServiceType(appointment.ServiceTypeId));
        Reflect.SetProperty(appointment, nameof(Appointment.Technician), GetTechnician(appointment.TechnicianId));
        Reflect.SetProperty(appointment, nameof(Appointment.ServiceBay), ServiceBays.Single(b => b.Id == appointment.ServiceBayId));
        return appointment;
    }

    private static Technician Tech(int id, int dealershipId, string name, int sh, int sm, int eh, int em, params (Skill Skill, SkillTier Tier)[] skills)
    {
        var technician = Technician.Create(dealershipId, name, new TimeOnly(sh, sm), new TimeOnly(eh, em));
        foreach (var (skill, tier) in skills)
        {
            technician.WithSkill(skill, tier);
        }

        return technician.WithId(id);
    }
}
