using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveDahsboard.Migrations
{
    /// <inheritdoc />
    public partial class lastone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RiderSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RiderId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RiderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CompanyId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Orders = table.Column<int>(type: "int", nullable: false),
                    WorkingHours = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    Wallet = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiderSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RiderSnapshots_CompanyId_Date",
                table: "RiderSnapshots",
                columns: new[] { "CompanyId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderSnapshots_RecordedAtUtc",
                table: "RiderSnapshots",
                column: "RecordedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RiderSnapshots_RiderId_CompanyId_Date",
                table: "RiderSnapshots",
                columns: new[] { "RiderId", "CompanyId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiderSnapshots");
        }
    }
}
