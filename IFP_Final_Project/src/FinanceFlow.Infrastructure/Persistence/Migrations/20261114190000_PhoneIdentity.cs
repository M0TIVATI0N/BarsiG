using FinanceFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceFlow.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FinanceFlowDbContext))]
[Migration("20261114190000_PhoneIdentity")]
public sealed class PhoneIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PhoneNumber",
            table: "Users",
            type: "TEXT",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsPhoneVerified",
            table: "Users",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_Users_PhoneNumber",
            table: "Users",
            column: "PhoneNumber",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Users_PhoneNumber", table: "Users");
        migrationBuilder.DropColumn(name: "PhoneNumber", table: "Users");
        migrationBuilder.DropColumn(name: "IsPhoneVerified", table: "Users");
    }
}
