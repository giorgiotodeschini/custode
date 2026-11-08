using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Custode.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ServerPrerequisitesWebUi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "Secrets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "CurrentVersion",
                table: "Secrets",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SecretVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProtectedPayload = table.Column<string>(type: "text", nullable: false),
                    ChangeKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceVersionNumber = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecretVersions_Secrets_SecretId",
                        column: x => x.SecretId,
                        principalTable: "Secrets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActivityAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantPolicies",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdleTimeoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    AccessTokenLifetimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    RefreshTokenLifetimeDays = table.Column<int>(type: "integer", nullable: false),
                    ClipboardClearSeconds = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPolicies", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReplacedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_SessionId",
                table: "RefreshTokens",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TenantId_SessionId",
                table: "RefreshTokens",
                columns: new[] { "TenantId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            // Existing secrets start their history at version 1, holding the value they already have.
            // The creator of pre-existing secrets is not recorded, so CreatedByUserId is Guid.Empty.
            migrationBuilder.Sql("""
                INSERT INTO "SecretVersions"
                    ("Id", "TenantId", "SecretId", "Number", "Name", "FileName", "ProtectedPayload", "ChangeKind", "SourceVersionNumber", "CreatedAtUtc", "CreatedByUserId")
                SELECT gen_random_uuid(), "TenantId", "Id", 1, "Name", "FileName", "ProtectedPayload", 'Created', NULL, "CreatedAtUtc", '00000000-0000-0000-0000-000000000000'
                FROM "Secrets";
                """);

            migrationBuilder.Sql("""UPDATE "Secrets" SET "ConcurrencyStamp" = gen_random_uuid();""");

            migrationBuilder.CreateIndex(
                name: "IX_SecretVersions_SecretId_Number",
                table: "SecretVersions",
                columns: new[] { "SecretId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecretVersions_TenantId_SecretId",
                table: "SecretVersions",
                columns: new[] { "TenantId", "SecretId" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_TenantId_UserId",
                table: "Sessions",
                columns: new[] { "TenantId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "SecretVersions");

            migrationBuilder.DropTable(
                name: "TenantPolicies");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Secrets");

            migrationBuilder.DropColumn(
                name: "CurrentVersion",
                table: "Secrets");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AspNetUsers");
        }
    }
}
