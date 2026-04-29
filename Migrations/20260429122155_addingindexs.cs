using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveDahsboard.Migrations
{
    /// <inheritdoc />
    public partial class addingindexs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RiderId",
                table: "RiderShiftStats",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "CompanyId",
                table: "RiderShiftStats",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_RiderShiftStats_CompanyId_Date",
                table: "RiderShiftStats",
                columns: new[] { "CompanyId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_RiderShiftStats_RiderId_CompanyId_ActiveShiftStartedAt",
                table: "RiderShiftStats",
                columns: new[] { "RiderId", "CompanyId", "ActiveShiftStartedAt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RiderShiftStats_CompanyId_Date",
                table: "RiderShiftStats");

            migrationBuilder.DropIndex(
                name: "IX_RiderShiftStats_RiderId_CompanyId_ActiveShiftStartedAt",
                table: "RiderShiftStats");

            migrationBuilder.AlterColumn<string>(
                name: "RiderId",
                table: "RiderShiftStats",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "CompanyId",
                table: "RiderShiftStats",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
