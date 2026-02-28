using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Worker.Migrations
{
    /// <inheritdoc />
    public partial class NewIndexInboxMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_AggregateId_Type",
                table: "InboxMessages",
                columns: new[] { "AggregateId", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InboxMessages_AggregateId_Type",
                table: "InboxMessages");
        }
    }
}
