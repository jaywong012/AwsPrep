using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AwsCertPrep.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExamScopeAndRetirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RetiredAt",
                table: "Questions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetiredReason",
                table: "Questions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScaledScore",
                table: "ExamSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsScored",
                table: "ExamSessionQuestions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExamGuideUrl",
                table: "Certifications",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "Certifications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OutOfScopeTasks",
                table: "Certifications",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PassingScaledScore",
                table: "Certifications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "QuestionTypes",
                table: "Certifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ScoredQuestionCount",
                table: "Certifications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TargetCandidate",
                table: "Certifications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "UnscoredQuestionCount",
                table: "Certifications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Questions_CertificationId_RetiredReason",
                table: "Questions",
                columns: new[] { "CertificationId", "RetiredReason" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Questions_CertificationId_RetiredReason",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "RetiredReason",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "ScaledScore",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "IsScored",
                table: "ExamSessionQuestions");

            migrationBuilder.DropColumn(
                name: "ExamGuideUrl",
                table: "Certifications");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Certifications");

            migrationBuilder.DropColumn(
                name: "OutOfScopeTasks",
                table: "Certifications");

            migrationBuilder.DropColumn(
                name: "PassingScaledScore",
                table: "Certifications");

            migrationBuilder.DropColumn(
                name: "QuestionTypes",
                table: "Certifications");

            migrationBuilder.DropColumn(
                name: "ScoredQuestionCount",
                table: "Certifications");

            migrationBuilder.DropColumn(
                name: "TargetCandidate",
                table: "Certifications");

            migrationBuilder.DropColumn(
                name: "UnscoredQuestionCount",
                table: "Certifications");
        }
    }
}
