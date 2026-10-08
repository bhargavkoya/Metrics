using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Metrics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LogSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrencyCodeSnapshot",
                table: "MetricLogValues",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FormulaSnapshot",
                table: "MetricLogValues",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrencyCodeSnapshot",
                table: "MetricLogValues");

            migrationBuilder.DropColumn(
                name: "FormulaSnapshot",
                table: "MetricLogValues");
        }
    }
}
