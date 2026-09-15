using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HR.Modules.Marketing.Persistence.Migrations;

// One-time correction for rows retained by the non-destructive startup seed.
// Values are frozen here so future catalog edits cannot change migration history.
[DbContext(typeof(MarketingDbContext))]
[Migration("20260915190000_OrderPhaseTwoRoadmap")]
public sealed class OrderPhaseTwoRoadmap : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE marketing.marketing_roadmap_items AS item
            SET display_order = seed.position
            FROM (VALUES
                ('c824eda0-862c-4602-b683-000000000001'::uuid, 'Performance Reviews & Appraisals', NULL::text, 0),
                ('c824eda0-862c-4602-b683-000000000002'::uuid, 'Webhooks & Integrations', 'Employee webhooks', 1),
                ('c824eda0-862c-4602-b683-000000000003'::uuid, 'Training & Development', NULL::text, 2),
                ('c824eda0-862c-4602-b683-000000000004'::uuid, 'Qualifications & Certification Compliance', NULL::text, 3),
                ('c824eda0-862c-4602-b683-000000000005'::uuid, 'Configurable offboarding templates', NULL::text, 4),
                ('c824eda0-862c-4602-b683-000000000006'::uuid, 'Employee Engagement & Surveys', NULL::text, 5),
                ('c824eda0-862c-4602-b683-000000000007'::uuid, 'External Tools & Employee Quick Links', NULL::text, 6),
                ('c824eda0-862c-4602-b683-000000000008'::uuid, 'IT Equipment Handover & Recovery', NULL::text, 7),
                ('c824eda0-862c-4602-b683-000000000009'::uuid, 'AI-generated Live Job Descriptions', 'AI-powered position profiles', 8),
                ('c824eda0-862c-4602-b683-000000000010'::uuid, 'AI Help Assistant', 'AI help assistant', 9)
            ) AS seed(id, title, legacy_title, position)
            WHERE item.id = seed.id
               OR lower(item.title) = lower(seed.title)
               OR lower(item.title) = lower(seed.legacy_title);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Previous administrator-defined ordering cannot be reconstructed.
        // Keep the corrected order when rolling back this data-only migration.
    }
}

