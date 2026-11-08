using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowEngine.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.CreateTable(
                name: "Requests",
                schema: "workflow",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Strategy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CurrentState = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CallbackUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.RequestId);
                });

            migrationBuilder.CreateTable(
                name: "RequestSteps",
                schema: "workflow",
                columns: table => new
                {
                    StepId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExecutionStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    InputData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StepRetries = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestSteps", x => x.StepId);
                    table.ForeignKey(
                        name: "FK_RequestSteps_Requests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "workflow",
                        principalTable: "Requests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_IdempotencyKey",
                schema: "workflow",
                table: "Requests",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Status_CreatedAt",
                schema: "workflow",
                table: "Requests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Strategy_Status",
                schema: "workflow",
                table: "Requests",
                columns: new[] { "Strategy", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestSteps_RequestId_StepName",
                schema: "workflow",
                table: "RequestSteps",
                columns: new[] { "RequestId", "StepName" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestSteps_RetryScheduled_NextRetryAt",
                schema: "workflow",
                table: "RequestSteps",
                columns: new[] { "ExecutionStatus", "NextRetryAt" },
                filter: "[ExecutionStatus] = 'RetryScheduled'");

            migrationBuilder.CreateIndex(
                name: "IX_RequestSteps_Running_LockedUntil",
                schema: "workflow",
                table: "RequestSteps",
                columns: new[] { "ExecutionStatus", "LockedUntil" },
                filter: "[ExecutionStatus] = 'Running'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestSteps",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "Requests",
                schema: "workflow");
        }
    }
}