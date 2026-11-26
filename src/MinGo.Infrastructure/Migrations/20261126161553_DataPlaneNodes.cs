using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// 「接入通道分组」→「数据面节点」重构：两张表就地改名（数据保留），
    /// 存量路由通道值改写（local/空 → node:default，group:X → node:X），并插入默认节点。
    /// </remarks>
    public partial class DataPlaneNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 表改名。SQLite 的 ALTER TABLE RENAME 会自动更新引用本表的其他表外键。
            migrationBuilder.RenameTable(
                name: "DataPlaneGroups",
                newName: "DataPlaneNodes");

            migrationBuilder.RenameTable(
                name: "InstanceGroupAssignments",
                newName: "InstanceNodeAssignments");

            migrationBuilder.RenameColumn(
                name: "GroupId",
                table: "InstanceNodeAssignments",
                newName: "NodeId");

            // SQLite 重建索引以跟上新表名/新列名（ALTER TABLE RENAME 不重命名索引）
            migrationBuilder.Sql(
                "DROP INDEX \"IX_InstanceGroupAssignments_GroupId\";" +
                "CREATE INDEX \"IX_InstanceNodeAssignments_NodeId\" ON \"InstanceNodeAssignments\" (\"NodeId\");");

            // 默认节点：启动兜底 EnsureDefaultNodeAsync 幂等，这里保证存量库立刻有
            var seedTime = new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);
            migrationBuilder.InsertData(
                table: "DataPlaneNodes",
                columns: new[] { "Id", "Name", "CreatedAt", "UpdatedAt" },
                values: new object[] { "default", "默认节点", seedTime, seedTime });

            // 存量路由通道改写：local/空 → 默认节点；group:X → node:X（组行已随表改名保留为节点）
            migrationBuilder.Sql(
                "UPDATE \"ApiRoutes\" SET \"ConnectionId\" = 'node:default' " +
                "WHERE \"ConnectionId\" = 'local' OR \"ConnectionId\" = '';");
            migrationBuilder.Sql(
                "UPDATE \"ApiRoutes\" SET \"ConnectionId\" = 'node:' || substr(\"ConnectionId\", 7) " +
                "WHERE \"ConnectionId\" LIKE 'group:%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 反向改写（best-effort：无法区分改写后新建的节点路由与原隧道路由以外的取值）
            migrationBuilder.Sql(
                "UPDATE \"ApiRoutes\" SET \"ConnectionId\" = 'local' " +
                "WHERE \"ConnectionId\" = 'node:default';");
            migrationBuilder.Sql(
                "UPDATE \"ApiRoutes\" SET \"ConnectionId\" = 'group:' || substr(\"ConnectionId\", 6) " +
                "WHERE \"ConnectionId\" LIKE 'node:%';");

            migrationBuilder.DeleteData(
                table: "DataPlaneNodes",
                keyColumn: "Id",
                keyValue: "default");

            migrationBuilder.Sql(
                "DROP INDEX \"IX_InstanceNodeAssignments_NodeId\";" +
                "CREATE INDEX \"IX_InstanceGroupAssignments_GroupId\" ON \"InstanceNodeAssignments\" (\"NodeId\");");

            migrationBuilder.RenameColumn(
                name: "NodeId",
                table: "InstanceNodeAssignments",
                newName: "GroupId");

            migrationBuilder.RenameTable(
                name: "InstanceNodeAssignments",
                newName: "InstanceGroupAssignments");

            migrationBuilder.RenameTable(
                name: "DataPlaneNodes",
                newName: "DataPlaneGroups");
        }
    }
}
