using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AwsCertPrep.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LessonCurriculum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LessonTopics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertificationId = table.Column<int>(type: "int", nullable: false),
                    DomainId = table.Column<int>(type: "int", nullable: true),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    PricingModel = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    DocsUrl = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    PricingUrl = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ServiceTags = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsCore = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonTopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonTopics_CertificationDomains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "CertificationDomains",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LessonTopics_Certifications_CertificationId",
                        column: x => x.CertificationId,
                        principalTable: "Certifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LessonTopicId = table.Column<int>(type: "int", nullable: false),
                    Overview = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    UseCases = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    CostNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Integrations = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    RealWorldExample = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    ExamTraps = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonContents_LessonTopics_LessonTopicId",
                        column: x => x.LessonTopicId,
                        principalTable: "LessonTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LessonTopicId = table.Column<int>(type: "int", nullable: false),
                    UserKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastViewedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonProgress_LessonTopics_LessonTopicId",
                        column: x => x.LessonTopicId,
                        principalTable: "LessonTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonContents_LessonTopicId",
                table: "LessonContents",
                column: "LessonTopicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonProgress_LessonTopicId",
                table: "LessonProgress",
                column: "LessonTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonProgress_UserKey_LessonTopicId",
                table: "LessonProgress",
                columns: new[] { "UserKey", "LessonTopicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonTopics_CertificationId_Slug",
                table: "LessonTopics",
                columns: new[] { "CertificationId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonTopics_DomainId",
                table: "LessonTopics",
                column: "DomainId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonContents");

            migrationBuilder.DropTable(
                name: "LessonProgress");

            migrationBuilder.DropTable(
                name: "LessonTopics");
        }
    }
}
