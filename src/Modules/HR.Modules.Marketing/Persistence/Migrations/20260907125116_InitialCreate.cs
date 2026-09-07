using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Marketing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "marketing");

            migrationBuilder.CreateTable(
                name: "marketing_features",
                schema: "marketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    icon_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    intro = table.Column<string>(type: "text", nullable: false),
                    detailed_content = table.Column<string>(type: "text", nullable: true),
                    benefits_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    youtube_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    delivery_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_features", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "marketing_products",
                schema: "marketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tagline = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "marketing_roadmap_items",
                schema: "marketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    icon_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    delivery_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_roadmap_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_features_is_published_display_order",
                schema: "marketing",
                table: "marketing_features",
                columns: new[] { "is_published", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_features_product_id",
                schema: "marketing",
                table: "marketing_features",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_marketing_features_slug",
                schema: "marketing",
                table: "marketing_features",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_roadmap_items_is_published_display_order",
                schema: "marketing",
                table: "marketing_roadmap_items",
                columns: new[] { "is_published", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_roadmap_items_product_id",
                schema: "marketing",
                table: "marketing_roadmap_items",
                column: "product_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketing_features",
                schema: "marketing");

            migrationBuilder.DropTable(
                name: "marketing_products",
                schema: "marketing");

            migrationBuilder.DropTable(
                name: "marketing_roadmap_items",
                schema: "marketing");
        }
    }
}
