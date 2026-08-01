using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocHub.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RepositoryServersAsData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This table used to hold overrides on top of servers declared in
            // configuration; it now holds the servers themselves. A row with no
            // address meant "an administrator cleared the override", which has
            // no meaning any more and cannot become a server — so it goes,
            // rather than surviving as a row with an empty address that fails
            // on the first question.
            migrationBuilder.Sql(
                """DELETE FROM repository_source_settings WHERE "Endpoint" IS NULL;""");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "repository_source_settings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "repository_source_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "repository_source_settings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ToolName",
                table: "repository_source_settings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            // A blank display name would render as an unnamed row. The
            // identifier is not a good label, but it is a true one.
            migrationBuilder.Sql(
                """
                UPDATE repository_source_settings
                SET "DisplayName" = "Name", "CreatedAt" = "UpdatedAt"
                WHERE "DisplayName" = '';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Endpoint",
                table: "repository_source_settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "repository_source_settings");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "repository_source_settings");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "repository_source_settings");

            migrationBuilder.DropColumn(
                name: "ToolName",
                table: "repository_source_settings");

            migrationBuilder.AlterColumn<string>(
                name: "Endpoint",
                table: "repository_source_settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);
        }
    }
}
