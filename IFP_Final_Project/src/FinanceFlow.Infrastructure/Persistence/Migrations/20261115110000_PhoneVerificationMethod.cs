using FinanceFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceFlow.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FinanceFlowDbContext))]
[Migration("20261115110000_PhoneVerificationMethod")]
public sealed class PhoneVerificationMethodMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<int>(
            name: "PhoneVerificationMethod",
            table: "Users",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "PhoneVerificationMethod", table: "Users");
}
