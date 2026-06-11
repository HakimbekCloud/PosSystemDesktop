using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftUuidToSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bug H1: link each sale to the POS shift it was rung up in so the
            // backend Z-report can reconcile the drawer. Nullable TEXT — legacy
            // rows (and any sale made with no open shift) carry null.
            migrationBuilder.AddColumn<string>(
                name: "ShiftUuid",
                table: "Sales",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShiftUuid",
                table: "Sales");
        }
    }
}
