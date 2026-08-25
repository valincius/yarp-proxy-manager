using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProxyManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIneffectiveProxyOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Http2Support",
                table: "ProxyHosts");

            migrationBuilder.DropColumn(
                name: "WebSocketsEnabled",
                table: "ProxyHosts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Http2Support",
                table: "ProxyHosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "WebSocketsEnabled",
                table: "ProxyHosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }
    }
}
