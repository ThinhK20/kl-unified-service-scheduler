using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Scheduler.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ThreeDemoCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Email", "Name", "Phone" },
                values: new object[] { "an.nguyen@example.com", "An Nguyen", "+84 900 000 001" });

            migrationBuilder.InsertData(
                table: "Customers",
                columns: new[] { "Id", "Email", "Name", "Phone" },
                values: new object[,]
                {
                    { 2, "binh.tran@example.com", "Binh Tran", "+84 900 000 002" },
                    { 3, "chi.le@example.com", "Chi Le", "+84 900 000 003" }
                });

            migrationBuilder.InsertData(
                table: "Vehicles",
                columns: new[] { "Id", "CustomerId", "Make", "Model", "Vin", "Year" },
                values: new object[,]
                {
                    { 3, 2, "VinFast", "VF 8", "RLLV8EAA5RV000003", 2024 },
                    { 4, 3, "Mazda", "CX-5", "JM3KFBCM1N0000004", 2022 },
                    { 5, 3, "Kia", "Morning", "KNABE511AL0000005", 2020 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback fails if customers 2-3 or vehicles 3-5 already have appointments (the foreign keys use
            // DeleteBehavior.Restrict); delete or move those appointments first.
            migrationBuilder.DeleteData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Vehicles",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Customers",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Customers",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Email", "Name", "Phone" },
                values: new object[] { "demo@example.com", "Demo Customer", "+84 900 000 000" });
        }
    }
}
