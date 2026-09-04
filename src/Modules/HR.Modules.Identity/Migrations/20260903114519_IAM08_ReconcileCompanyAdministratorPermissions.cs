using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class IAM08_ReconcileCompanyAdministratorPermissions : Migration
    {
        // Company Administrator role id.
        private const string CompanyAdministratorRoleId = "00000000-0000-0000-0000-000000000006";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IAM-08: reconcile legacy databases whose Company Administrator role still carries
            // obsolete role_permissions rows (employee.*, leave.*, sickness.*, document.*,
            // hr-settings.*, reporting.*, recruitment.*, role.assign, ...). The current seed
            // (RolePermissionConfiguration + migrations 20260710120353 / 20260826105820) already
            // restricts this role to company-configuration permissions only, but databases created
            // before those migrations — or drifted by an aborted/rolled-back migration, a restored
            // snapshot or manual seeding — can retain the broad grant set and thereby leak
            // employee visibility to a "Company Administrator only" user.
            //
            // Expressed as a single set-based DELETE with an explicit allow-list so it also removes
            // any unknown drift, and so re-running it is a no-op (idempotent). The allow-list is:
            //   ...0011 company.read      ...0012 company.edit
            //   ...0019 onboarding.view   ...0020 onboarding.manage
            //   ...0021 subscription.manage
            //   ...0042 support.manage
            migrationBuilder.Sql($"""
                DELETE FROM identity.role_permissions
                WHERE role_id = '{CompanyAdministratorRoleId}'
                  AND permission_id NOT IN (
                    '00000000-0000-0000-0001-000000000011',
                    '00000000-0000-0000-0001-000000000012',
                    '00000000-0000-0000-0001-000000000019',
                    '00000000-0000-0000-0001-000000000020',
                    '00000000-0000-0000-0001-000000000021',
                    '00000000-0000-0000-0001-000000000042'
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: this migration only removes permission grants that were never part of the
            // current Company Administrator definition. Restoring the historical broad grant set
            // would re-introduce the IAM-08 defect, so the down migration deliberately does nothing.
        }
    }
}
