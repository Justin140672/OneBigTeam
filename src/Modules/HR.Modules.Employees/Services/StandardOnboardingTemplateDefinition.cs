using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Employees.Services;

internal static class StandardOnboardingTemplateDefinition
{
    public const string Name = "Standard Onboarding";

    public const string Description =
        "Default new-starter checklist covering the essentials for a new hire's first 30 days.";

    public sealed record TaskDefinition(
        string Title,
        string Description,
        TaskPriority Priority,
        OnboardingTemplateTaskAssignTo AssignTo,
        int DueDaysAfterStart,
        int DisplayOrder);

    public static IReadOnlyList<TaskDefinition> Tasks { get; } =
    [
        new("Send welcome email and first-day information", "Introduce the company and share start time, location, who to ask for and what to bring.", TaskPriority.High, OnboardingTemplateTaskAssignTo.Manager, 0, 1),
        new("Prepare workstation, equipment, accounts, and system access", "Laptop, accounts, system access and desk setup ready before day one.", TaskPriority.High, OnboardingTemplateTaskAssignTo.Manager, 0, 2),
        new("Complete personal and emergency-contact details", "Confirm your personal details and add at least one emergency contact.", TaskPriority.High, OnboardingTemplateTaskAssignTo.NewHire, 1, 3),
        new("Complete payroll and tax information", "Provide the bank, tax and payroll details needed to set you up for pay.", TaskPriority.High, OnboardingTemplateTaskAssignTo.NewHire, 3, 4),
        new("Complete right-to-work and employment-document checks", "Verify the new hire's right to work and file the required employment documentation.", TaskPriority.Critical, OnboardingTemplateTaskAssignTo.Hr, 1, 5),
        new("Review company policies and required acknowledgements", "Read the company policies and acknowledge those that require sign-off.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.NewHire, 5, 6),
        new("Attend company induction", "Overview of policies, values, and company structure.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.NewHire, 3, 7),
        new("Complete role-specific induction", "Walk the new hire through the tools, processes and expectations specific to their role.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 7, 8),
        new("Meet the team and key stakeholders", "Introductions with the immediate team and key stakeholders.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 5, 9),
        new("Complete security and data-protection training", "Complete the information-security and data-protection training modules.", TaskPriority.High, OnboardingTemplateTaskAssignTo.NewHire, 7, 10),
        new("Complete health-and-safety and mandatory training", "Complete health and safety and any other mandatory or compliance training.", TaskPriority.High, OnboardingTemplateTaskAssignTo.NewHire, 10, 11),
        new("Hold first-week check-in", "Check how the new hire is settling in and resolve any early issues.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 7, 12),
        new("Agree initial objectives and 30-day goals", "Agree initial objectives and success measures for the first 30 days.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 14, 13),
        new("Hold 30-day review and collect feedback", "Review progress against the 30-day goals and collect feedback on the onboarding experience.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 30, 14),
        new("Confirm probation expectations and review schedule", "Explain probation expectations and agree the dates of probation reviews.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 21, 15),
    ];
}
