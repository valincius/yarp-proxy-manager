using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProxyManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Streams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Streams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Protocol = table.Column<int>(type: "INTEGER", nullable: false),
                    ListenPort = table.Column<int>(type: "INTEGER", nullable: false),
                    ForwardHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    ForwardPort = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Streams", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Streams_ListenPort",
                table: "Streams",
                column: "ListenPort",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Streams");
        }
    }
}
