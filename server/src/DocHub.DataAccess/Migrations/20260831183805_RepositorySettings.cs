using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocHub.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RepositorySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repository_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ProjectPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Branch = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SubPath = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    HasSubPath = table.Column<bool>(type: "boolean", nullable: false),
                    ProtectedToken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ProtectedWebhookSecret = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repository_settings", x => x.Id);
                    table.CheckConstraint("ck_repository_settings_singleton", "\"Id\" = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repository_settings");
        }
    }
}
