using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Scheduler.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dealerships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OpenTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    CloseTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dealerships", x => x.Id);
                    table.CheckConstraint("CK_Dealerships_CloseAfterOpen", "[CloseTime] > [OpenTime]");
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Vin = table.Column<string>(type: "nvarchar(17)", maxLength: 17, nullable: false),
                    Make = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vehicles_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceBays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DealershipId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BayType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceBays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceBays_Dealerships_DealershipId",
                        column: x => x.DealershipId,
                        principalTable: "Dealerships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DealershipId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    RequiredSkill = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MinimumTier = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequiredBayType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceTypes", x => x.Id);
                    table.CheckConstraint("CK_ServiceTypes_DurationOnGrid", "[DurationMinutes] > 0 AND [DurationMinutes] % 15 = 0");
                    table.ForeignKey(
                        name: "FK_ServiceTypes_Dealerships_DealershipId",
                        column: x => x.DealershipId,
                        principalTable: "Dealerships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Technicians",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DealershipId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ShiftStart = table.Column<TimeOnly>(type: "time", nullable: false),
                    ShiftEnd = table.Column<TimeOnly>(type: "time", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Technicians", x => x.Id);
                    table.CheckConstraint("CK_Technicians_ShiftEndAfterStart", "[ShiftEnd] > [ShiftStart]");
                    table.ForeignKey(
                        name: "FK_Technicians_Dealerships_DealershipId",
                        column: x => x.DealershipId,
                        principalTable: "Dealerships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    DealershipId = table.Column<int>(type: "int", nullable: false),
                    ServiceTypeId = table.Column<int>(type: "int", nullable: false),
                    TechnicianId = table.Column<int>(type: "int", nullable: false),
                    ServiceBayId = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationSnapshot = table.Column<int>(type: "int", nullable: false),
                    RequiredSkillSnapshot = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequiredBayTypeSnapshot = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.CheckConstraint("CK_Appointments_EndAfterStart", "[EndUtc] > [StartUtc]");
                    table.ForeignKey(
                        name: "FK_Appointments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Dealerships_DealershipId",
                        column: x => x.DealershipId,
                        principalTable: "Dealerships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_ServiceBays_ServiceBayId",
                        column: x => x.ServiceBayId,
                        principalTable: "ServiceBays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_ServiceTypes_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalTable: "ServiceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Technicians_TechnicianId",
                        column: x => x.TechnicianId,
                        principalTable: "Technicians",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TechnicianSkills",
                columns: table => new
                {
                    TechnicianId = table.Column<int>(type: "int", nullable: false),
                    Skill = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Tier = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechnicianSkills", x => new { x.TechnicianId, x.Skill });
                    table.ForeignKey(
                        name: "FK_TechnicianSkills_Technicians_TechnicianId",
                        column: x => x.TechnicianId,
                        principalTable: "Technicians",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyKeys",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AppointmentId = table.Column<int>(type: "int", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyKeys", x => x.Key);
                    table.ForeignKey(
                        name: "FK_IdempotencyKeys_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResourceSlots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    SlotStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AppointmentId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceSlots_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Customers",
                columns: new[] { "Id", "Email", "Name", "Phone" },
                values: new object[] { 1, "demo@example.com", "Demo Customer", "+84 900 000 000" });

            migrationBuilder.InsertData(
                table: "Dealerships",
                columns: new[] { "Id", "CloseTime", "IsActive", "Name", "OpenTime", "TimeZoneId" },
                values: new object[,]
                {
                    { 1, new TimeOnly(17, 0, 0), true, "District 1 Service Centre", new TimeOnly(8, 0, 0), "Asia/Ho_Chi_Minh" },
                    { 2, new TimeOnly(18, 0, 0), true, "Thu Duc Service Centre", new TimeOnly(7, 30, 0), "Asia/Ho_Chi_Minh" }
                });

            migrationBuilder.InsertData(
                table: "ServiceBays",
                columns: new[] { "Id", "BayType", "DealershipId", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "Standard", 1, true, "Bay 1" },
                    { 2, "Lift", 1, true, "Bay 2" },
                    { 3, "EV", 1, true, "Bay 3" },
                    { 4, "Standard", 2, true, "Bay A" },
                    { 5, "Lift", 1, false, "Bay 4" }
                });

            migrationBuilder.InsertData(
                table: "ServiceTypes",
                columns: new[] { "Id", "DealershipId", "DurationMinutes", "IsActive", "MinimumTier", "Name", "RequiredBayType", "RequiredSkill" },
                values: new object[,]
                {
                    { 1, 1, 60, true, "Trainee", "Oil change", "Standard", "General" },
                    { 2, 1, 90, true, "Qualified", "Brake inspection", "Lift", "Brakes" },
                    { 3, 1, 30, true, "Trainee", "Tyre rotation", "Lift", "Tyres" },
                    { 4, 1, 60, true, "Senior", "EV battery check", "EV", "EV" },
                    { 5, 2, 60, true, "Trainee", "Oil change", "Standard", "General" }
                });

            migrationBuilder.InsertData(
                table: "Technicians",
                columns: new[] { "Id", "DealershipId", "IsActive", "Name", "ShiftEnd", "ShiftStart" },
                values: new object[,]
                {
                    { 1, 1, true, "Nam", new TimeOnly(17, 0, 0), new TimeOnly(8, 0, 0) },
                    { 2, 1, true, "Lan", new TimeOnly(17, 0, 0), new TimeOnly(8, 0, 0) },
                    { 3, 1, true, "Minh", new TimeOnly(17, 0, 0), new TimeOnly(12, 0, 0) },
                    { 4, 2, true, "Hoa", new TimeOnly(18, 0, 0), new TimeOnly(7, 30, 0) },
                    { 5, 1, false, "Tuan", new TimeOnly(17, 0, 0), new TimeOnly(8, 0, 0) }
                });

            migrationBuilder.InsertData(
                table: "Vehicles",
                columns: new[] { "Id", "CustomerId", "Make", "Model", "Vin", "Year" },
                values: new object[,]
                {
                    { 1, 1, "Toyota", "Vios", "JTDBT923X71000001", 2021 },
                    { 2, 1, "Honda", "CR-V", "7FARW2H58PE000002", 2023 }
                });

            migrationBuilder.InsertData(
                table: "TechnicianSkills",
                columns: new[] { "Skill", "TechnicianId", "Tier" },
                values: new object[,]
                {
                    { "Brakes", 1, "Senior" },
                    { "General", 1, "Master" },
                    { "Tyres", 1, "Senior" },
                    { "EV", 2, "Senior" },
                    { "General", 2, "Qualified" },
                    { "Tyres", 2, "Qualified" },
                    { "Brakes", 3, "Qualified" },
                    { "General", 3, "Trainee" },
                    { "General", 4, "Senior" },
                    { "Brakes", 5, "Master" },
                    { "EV", 5, "Master" },
                    { "General", 5, "Master" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CustomerId_StartUtc",
                table: "Appointments",
                columns: new[] { "CustomerId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DealershipId",
                table: "Appointments",
                column: "DealershipId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ServiceBayId",
                table: "Appointments",
                column: "ServiceBayId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ServiceTypeId",
                table: "Appointments",
                column: "ServiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TechnicianId_StartUtc",
                table: "Appointments",
                columns: new[] { "TechnicianId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_VehicleId",
                table: "Appointments",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKeys_AppointmentId",
                table: "IdempotencyKeys",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceSlots_AppointmentId",
                table: "ResourceSlots",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "UX_ResourceSlots_Resource_Slot",
                table: "ResourceSlots",
                columns: new[] { "ResourceType", "ResourceId", "SlotStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceBays_DealershipId",
                table: "ServiceBays",
                column: "DealershipId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypes_DealershipId",
                table: "ServiceTypes",
                column: "DealershipId");

            migrationBuilder.CreateIndex(
                name: "IX_Technicians_DealershipId",
                table: "Technicians",
                column: "DealershipId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_CustomerId",
                table: "Vehicles",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "UX_Vehicles_Vin",
                table: "Vehicles",
                column: "Vin",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyKeys");

            migrationBuilder.DropTable(
                name: "ResourceSlots");

            migrationBuilder.DropTable(
                name: "TechnicianSkills");

            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropTable(
                name: "ServiceBays");

            migrationBuilder.DropTable(
                name: "ServiceTypes");

            migrationBuilder.DropTable(
                name: "Technicians");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropTable(
                name: "Dealerships");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
