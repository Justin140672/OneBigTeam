using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Modules.Employees.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeSeededStandardOnboardingTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Supabase Data API classification: server-only/direct PostgreSQL. Data-only change to
            // existing employees.onboarding_templates / onboarding_template_tasks rows; no grants,
            // RLS or policy changes.
            // Upgrades only a company's active "Standard Onboarding" template that still exactly
            // matches the originally seeded definition (same description and exactly the seven
            // original tasks, none removed or edited). Customer-edited or replaced templates and
            // already-created onboarding plans are never touched.
            migrationBuilder.Sql(@"
                WITH legacy (title, description, priority, assign_to, due_days_after_start, display_order) AS (
                    VALUES
                        ('Send welcome email', 'Introduce the company, share first-day logistics.', 'High', 'Manager', 0, 1),
                        ('Prepare workstation and equipment', 'Laptop, accounts, desk setup ready before day one.', 'High', 'Manager', 0, 2),
                        ('Complete right-to-work checks', 'Verify and file required employment documentation.', 'Critical', 'Manager', 1, 3),
                        ('Company induction session', 'Overview of policies, values, and company structure.', 'Medium', 'NewHire', 3, 4),
                        ('Meet the team', 'Introductions with immediate team and key stakeholders.', 'Medium', 'Manager', 3, 5),
                        ('Set 30-day goals', 'Agree initial objectives and success measures.', 'Medium', 'Manager', 14, 6),
                        ('Complete mandatory training', 'Health & safety, compliance and any role-specific training.', 'High', 'NewHire', 14, 7)
                ),
                eligible AS (
                    SELECT t.id, t.company_id
                    FROM employees.onboarding_templates AS t
                    WHERE t.name = 'Standard Onboarding'
                      AND t.description = 'Default new-starter checklist covering the essentials for a new hire''s first two weeks.'
                      AND t.is_active
                      AND (SELECT COUNT(*)
                           FROM employees.onboarding_template_tasks AS k
                           WHERE k.onboarding_template_id = t.id) = 7
                      AND (SELECT COUNT(*)
                           FROM employees.onboarding_template_tasks AS k
                           JOIN legacy AS l
                             ON k.title = l.title
                            AND k.description = l.description
                            AND k.priority = l.priority
                            AND k.assign_to = l.assign_to
                            AND k.due_days_after_start = l.due_days_after_start
                            AND k.display_order = l.display_order
                           WHERE k.onboarding_template_id = t.id
                             AND k.is_active) = 7
                ),
                removed AS (
                    DELETE FROM employees.onboarding_template_tasks AS k
                    USING eligible AS e
                    WHERE k.onboarding_template_id = e.id
                    RETURNING k.id
                ),
                updated AS (
                    UPDATE employees.onboarding_templates AS t
                    SET description = 'Default new-starter checklist covering the essentials for a new hire''s first 30 days.',
                        version = t.version + 1,
                        updated_at = now()
                    FROM eligible AS e
                    WHERE t.id = e.id
                    RETURNING t.id
                )
                INSERT INTO employees.onboarding_template_tasks
                    (id, company_id, onboarding_template_id, title, description, priority, assign_to,
                     due_days_after_start, display_order, is_active)
                SELECT gen_random_uuid(), e.company_id, e.id, n.title, n.description, n.priority, n.assign_to,
                       n.due_days_after_start, n.display_order, TRUE
                FROM eligible AS e
                CROSS JOIN (
                    VALUES
                        ('Send welcome email and first-day information', 'Introduce the company and share start time, location, who to ask for and what to bring.', 'High', 'Manager', 0, 1),
                        ('Prepare workstation, equipment, accounts, and system access', 'Laptop, accounts, system access and desk setup ready before day one.', 'High', 'Manager', 0, 2),
                        ('Complete personal and emergency-contact details', 'Confirm your personal details and add at least one emergency contact.', 'High', 'NewHire', 1, 3),
                        ('Complete payroll and tax information', 'Provide the bank, tax and payroll details needed to set you up for pay.', 'High', 'NewHire', 3, 4),
                        ('Complete right-to-work and employment-document checks', 'Verify the new hire''s right to work and file the required employment documentation.', 'Critical', 'Hr', 1, 5),
                        ('Review company policies and required acknowledgements', 'Read the company policies and acknowledge those that require sign-off.', 'Medium', 'NewHire', 5, 6),
                        ('Attend company induction', 'Overview of policies, values, and company structure.', 'Medium', 'NewHire', 3, 7),
                        ('Complete role-specific induction', 'Walk the new hire through the tools, processes and expectations specific to their role.', 'Medium', 'Manager', 7, 8),
                        ('Meet the team and key stakeholders', 'Introductions with the immediate team and key stakeholders.', 'Medium', 'Manager', 5, 9),
                        ('Complete security and data-protection training', 'Complete the information-security and data-protection training modules.', 'High', 'NewHire', 7, 10),
                        ('Complete health-and-safety and mandatory training', 'Complete health and safety and any other mandatory or compliance training.', 'High', 'NewHire', 10, 11),
                        ('Hold first-week check-in', 'Check how the new hire is settling in and resolve any early issues.', 'Medium', 'Manager', 7, 12),
                        ('Agree initial objectives and 30-day goals', 'Agree initial objectives and success measures for the first 30 days.', 'Medium', 'Manager', 14, 13),
                        ('Hold 30-day review and collect feedback', 'Review progress against the 30-day goals and collect feedback on the onboarding experience.', 'Medium', 'Manager', 30, 14),
                        ('Confirm probation expectations and review schedule', 'Explain probation expectations and agree the dates of probation reviews.', 'Medium', 'Manager', 21, 15)
                ) AS n (title, description, priority, assign_to, due_days_after_start, display_order);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
