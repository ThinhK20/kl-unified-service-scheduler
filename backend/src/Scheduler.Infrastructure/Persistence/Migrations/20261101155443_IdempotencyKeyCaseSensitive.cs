using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduler.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdempotencyKeyCaseSensitive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server cannot change the collation of a primary key column: drop the key, alter, add it back.
            migrationBuilder.DropPrimaryKey(name: "PK_IdempotencyKeys", table: "IdempotencyKeys");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "IdempotencyKeys",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AddPrimaryKey(name: "PK_IdempotencyKeys", table: "IdempotencyKeys", column: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQL Server cannot change the collation of a primary key column: drop the key, alter, add it back.
            migrationBuilder.DropPrimaryKey(name: "PK_IdempotencyKeys", table: "IdempotencyKeys");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "IdempotencyKeys",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AddPrimaryKey(name: "PK_IdempotencyKeys", table: "IdempotencyKeys", column: "Key");
        }
    }
}
