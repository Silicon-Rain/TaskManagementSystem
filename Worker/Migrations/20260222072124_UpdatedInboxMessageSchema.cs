using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Worker.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedInboxMessageSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OccuredOn",
                table: "InboxMessages",
                newName: "ReceivedOn");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ProcessedOn",
                table: "InboxMessages",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<Guid>(
                name: "AggregateId",
                table: "InboxMessages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AggregateId",
                table: "InboxMessages");

            migrationBuilder.RenameColumn(
                name: "ReceivedOn",
                table: "InboxMessages",
                newName: "OccuredOn");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ProcessedOn",
                table: "InboxMessages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
