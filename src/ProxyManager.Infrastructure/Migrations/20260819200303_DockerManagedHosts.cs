using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProxyManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DockerManagedHosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManagedBy",
                table: "ProxyHosts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagedSource",
                table: "ProxyHosts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManagedBy",
                table: "ProxyHosts");

            migrationBuilder.DropColumn(
                name: "ManagedSource",
                table: "ProxyHosts");
        }
    }
}
