using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveDahsboard.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftAccumulationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastSeenOrders",
                table: "RiderStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "LastSeenWorkingHours",
                table: "RiderStats",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "OrdersBase",
                table: "RiderStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "WorkingHoursBase",
                table: "RiderStats",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSeenOrders",
                table: "RiderStats");

            migrationBuilder.DropColumn(
                name: "LastSeenWorkingHours",
                table: "RiderStats");

            migrationBuilder.DropColumn(
                name: "OrdersBase",
                table: "RiderStats");

            migrationBuilder.DropColumn(
                name: "WorkingHoursBase",
                table: "RiderStats");
        }
    }
}
