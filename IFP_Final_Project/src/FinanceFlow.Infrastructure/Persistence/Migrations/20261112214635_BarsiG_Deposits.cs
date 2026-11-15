using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BarsiG_Deposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Deposits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    AnnualRatePercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    TermMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAccruedMonth = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    OpenedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ClosedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deposits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deposits_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Deposits_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DepositInterestAccruals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DepositId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodMonth = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    InterestCents = table.Column<long>(type: "INTEGER", nullable: false),
                    BalanceAfterCents = table.Column<long>(type: "INTEGER", nullable: false),
                    TransferId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccruedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepositInterestAccruals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepositInterestAccruals_Deposits_DepositId",
                        column: x => x.DepositId,
                        principalTable: "Deposits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DepositInterestAccruals_LedgerTransfers_TransferId",
                        column: x => x.TransferId,
                        principalTable: "LedgerTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepositInterestAccruals_DepositId_PeriodMonth",
                table: "DepositInterestAccruals",
                columns: new[] { "DepositId", "PeriodMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DepositInterestAccruals_TransferId",
                table: "DepositInterestAccruals",
                column: "TransferId");

            migrationBuilder.CreateIndex(
                name: "IX_Deposits_BankAccountId",
                table: "Deposits",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Deposits_UserId_Status",
                table: "Deposits",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DepositInterestAccruals");

            migrationBuilder.DropTable(
                name: "Deposits");
        }
    }
}
