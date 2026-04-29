using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveDahsboard.Migrations
{
    /// <inheritdoc />
    public partial class newmodels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RiderShiftStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RiderName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompanyId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActiveShiftStartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Orders = table.Column<int>(type: "int", nullable: false),
                    WorkingHours = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Wallet = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderShiftStats", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiderShiftStats");
        }
    }
}
