using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BarsiG_VirtualCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VirtualCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LinkedAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nickname = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    MaskedNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastFour = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyLimitCents = table.Column<long>(type: "INTEGER", nullable: false),
                    MonthlyLimitCents = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                    FrozenAtUnixMs = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VirtualCards_BankAccounts_LinkedAccountId",
                        column: x => x.LinkedAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VirtualCards_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VirtualCards_LinkedAccountId",
                table: "VirtualCards",
                column: "LinkedAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_VirtualCards_UserId_Status",
                table: "VirtualCards",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VirtualCards");
        }
    }
}
