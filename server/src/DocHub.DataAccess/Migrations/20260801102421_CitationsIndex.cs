using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocHub.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class CitationsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_Citations",
                table: "chat_messages",
                column: "Citations")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_chat_messages_Citations",
                table: "chat_messages");
        }
    }
}
