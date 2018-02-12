﻿using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace Goldmint.DAL.Migrations
{
    public partial class useroplog : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gm_user_oplog",
                columns: table => new
                {
                    id = table.Column<long>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    concurrency_stamp = table.Column<string>(maxLength: 64, nullable: true),
                    message = table.Column<string>(maxLength: 512, nullable: false),
                    ref_id = table.Column<long>(nullable: true),
                    status = table.Column<int>(nullable: false),
                    time_created = table.Column<DateTime>(nullable: false),
                    user_id = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gm_user_oplog", x => x.id);
                    table.ForeignKey(
                        name: "FK_gm_user_oplog_gm_user_oplog_ref_id",
                        column: x => x.ref_id,
                        principalTable: "gm_user_oplog",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gm_user_oplog_gm_user_user_id",
                        column: x => x.user_id,
                        principalTable: "gm_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gm_user_oplog_ref_id",
                table: "gm_user_oplog",
                column: "ref_id");

            migrationBuilder.CreateIndex(
                name: "IX_gm_user_oplog_user_id",
                table: "gm_user_oplog",
                column: "user_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gm_user_oplog");
        }
    }
}
