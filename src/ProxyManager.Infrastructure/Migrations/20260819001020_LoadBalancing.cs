using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProxyManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LoadBalancing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HealthCheckEnabled",
                table: "ProxyHosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "HealthCheckIntervalSeconds",
                table: "ProxyHosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HealthCheckPath",
                table: "ProxyHosts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoadBalancingPolicy",
                table: "ProxyHosts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProxyDestinations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProxyHostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ForwardHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    ForwardPort = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyDestinations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProxyDestinations_ProxyHosts_ProxyHostId",
                        column: x => x.ProxyHostId,
                        principalTable: "ProxyHosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProxyDestinations_ProxyHostId",
                table: "ProxyDestinations",
                column: "ProxyHostId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProxyDestinations");

            migrationBuilder.DropColumn(
                name: "HealthCheckEnabled",
                table: "ProxyHosts");

            migrationBuilder.DropColumn(
                name: "HealthCheckIntervalSeconds",
                table: "ProxyHosts");

            migrationBuilder.DropColumn(
                name: "HealthCheckPath",
                table: "ProxyHosts");

            migrationBuilder.DropColumn(
                name: "LoadBalancingPolicy",
                table: "ProxyHosts");
        }
    }
}
