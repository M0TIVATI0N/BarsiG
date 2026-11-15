using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BarsiG_Banking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    BalanceCents = table.Column<long>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ClosedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankAccounts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserSecurityProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    TransactionPinHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    TransactionPinSalt = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    TotpSecret = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSecurityProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSecurityProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LedgerTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FromAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AmountCents = table.Column<long>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    CreatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerTransfers_BankAccounts_FromAccountId",
                        column: x => x.FromAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerTransfers_BankAccounts_ToAccountId",
                        column: x => x.ToAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerTransfers_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransferId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    AmountCents = table.Column<long>(type: "INTEGER", nullable: false),
                    BalanceAfterCents = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_BankAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_LedgerTransfers_TransferId",
                        column: x => x.TransferId,
                        principalTable: "LedgerTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_AccountNumber",
                table: "BankAccounts",
                column: "AccountNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_UserId",
                table: "BankAccounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_UserId_Type",
                table: "BankAccounts",
                columns: new[] { "UserId", "Type" },
                unique: true,
                filter: "\"Type\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId_CreatedAtUnixMs",
                table: "LedgerEntries",
                columns: new[] { "AccountId", "CreatedAtUnixMs" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TransferId_AccountId",
                table: "LedgerEntries",
                columns: new[] { "TransferId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransfers_CreatedAtUnixMs",
                table: "LedgerTransfers",
                column: "CreatedAtUnixMs");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransfers_FromAccountId",
                table: "LedgerTransfers",
                column: "FromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransfers_RequestedByUserId_IdempotencyKey",
                table: "LedgerTransfers",
                columns: new[] { "RequestedByUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransfers_ToAccountId",
                table: "LedgerTransfers",
                column: "ToAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSecurityProfiles_UserId",
                table: "UserSecurityProfiles",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "UserSecurityProfiles");

            migrationBuilder.DropTable(
                name: "LedgerTransfers");

            migrationBuilder.DropTable(
                name: "BankAccounts");
        }
    }
}
