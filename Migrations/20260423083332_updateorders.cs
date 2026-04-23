using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveDahsboard.Migrations
{
    /// <inheritdoc />
    public partial class updateorders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrdersSnapshottedBeforeReset",
                table: "RiderStats",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrdersSnapshottedBeforeReset",
                table: "RiderStats");
        }
    }
}
