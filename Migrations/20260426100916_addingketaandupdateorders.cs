using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveDahsboard.Migrations
{
    /// <inheritdoc />
    public partial class addingketaandupdateorders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrdersDayStart",
                table: "RiderStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "KeetaStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CourierId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CourierName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    OrgId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    FinishedTasks = table.Column<int>(type: "int", nullable: false),
                    DeliveringTasks = table.Column<int>(type: "int", nullable: false),
                    CanceledTasks = table.Column<int>(type: "int", nullable: false),
                    OnlineHours = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeetaStats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KeetaStats_CourierId_OrgId_Date",
                table: "KeetaStats",
                columns: new[] { "CourierId", "OrgId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KeetaStats_OrgId_Date",
                table: "KeetaStats",
                columns: new[] { "OrgId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KeetaStats");

            migrationBuilder.DropColumn(
                name: "OrdersDayStart",
                table: "RiderStats");
        }
    }
}
