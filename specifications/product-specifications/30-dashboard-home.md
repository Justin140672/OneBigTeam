# 30. Dashboard and Home Experience

## Current status

The application uses role-specific dashboards for operational roles and direct landing pages for users who do not need a dashboard.

There is no universal Employee dashboard or Company Administrator dashboard in V1.

## Landing rules

After authentication, the application chooses an authorised landing destination in this order unless the user has stored an authorised dashboard preference:

1. HR Administrator dashboard
2. Recruitment dashboard
3. Manager dashboard
4. Company administration for a Company Administrator without an operational dashboard role
5. The employee's own profile for an ordinary Employee

A multi-role user may switch between dashboards available to their roles. A stored preference must be ignored if the user no longer holds the required role.

## Ordinary Employee landing

An ordinary Employee lands on their own profile. Self-service information is provided by the profile experience, including where authorised:

- Own tasks
- Own notifications
- Own leave and sickness information
- Own documents and acknowledgements
- Own profile and contact details
- Own salary when the company salary-display setting permits it

This direct profile landing is intentional and should not be replaced with a generic Employee dashboard without a new product decision.

## Manager dashboard

The Manager dashboard surfaces information and actions for the manager's complete reporting hierarchy where the underlying feature permits it.

Implemented areas include:

- Team tasks
- Leave requests
- Probation reviews
- Employee onboarding
- Team sickness
- Return-to-work reviews
- Missing fit notes
- Team overview
- Team reports

Dashboard components must not expose employees outside the caller's authorised hierarchy.

## HR Administrator dashboard

The HR dashboard provides company-wide operational visibility for HR users, including:

- Attention queue and actionable work
- Headcount and workforce charts
- Current sickness absence
- Missing fit notes
- Recent employee changes
- Favourite reports

HR dashboard data remains tenant-scoped and subject to feature-specific permissions.

## Recruitment dashboard

The Recruitment dashboard provides:

- Hiring pipeline analytics
- New-hire trends
- Recruitment summary
- Upcoming interviews
- Offers awaiting response
- Vacancies with no recent activity
- Recruitment pipeline board and list views

Recruitment access does not imply general HR or detailed employee-record access.

## Company Administrator landing

A Company Administrator without HR, Recruitment or Manager access lands in company administration.

Company Administrator is limited to company profile, settings and branding. It does not grant employee management, HR reporting, salary access or other HR permissions.

The initial company creator is an explicit exception because that account also receives HR Administrator.

## Dashboard principles

Dashboards must be:

- Fast
- Relevant to the caller's role
- Actionable
- Permission-aware
- Responsive and accessible

UI component visibility does not replace API authorization. Every widget query and action must enforce tenant, role and resource scope on the server.

## Tasks and notifications

Dashboard task actions must verify that the caller is the assignee or holds an explicit authority to act for the assignee. Merely knowing a task identifier is insufficient.

Notifications displayed or mutated from a dashboard must belong to the authenticated recipient unless an explicit administrative permission applies.

## Performance and accessibility

- Dashboard pages should load within the product performance target.
- Independent widgets should handle loading, empty and failure states.
- Expensive queries should be bounded and independently cancellable.
- Keyboard navigation, screen-reader labels, focus handling and responsive layouts are required.

## Acceptance criteria

1. HR Administrator, Recruiter and Manager roles have dedicated dashboards.
2. Ordinary Employees land on their own profile.
3. Company Administrators without an operational role land in company administration.
4. Multi-role users can select only dashboards they are authorised to use.
5. A removed role invalidates any stored dashboard preference.
6. Dashboard widgets enforce server-side tenant and resource authorization.
7. Manager data is limited to the complete authorised reporting hierarchy.
8. Recruitment access does not expose general HR data.
9. Dashboard task actions enforce assignment or delegated authority.
10. Dashboard components provide accessible loading, empty and error states.
