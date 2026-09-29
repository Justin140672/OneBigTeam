using System.Reflection;
using System.Text.RegularExpressions;

using HR.SharedKernel;

namespace HR.Architecture.Tests;

public class EnumDisplayTextTests
{
    private static readonly Regex LowerToUpper = new("[a-z][A-Z]", RegexOptions.Compiled);

    private static readonly Assembly[] Assemblies =
        [
            typeof(HR.Modules.Companies.CompaniesModule).Assembly,
            typeof(HR.Modules.Companies.Contracts.SubscriptionStatus).Assembly,
            typeof(HR.Modules.CompanyOnboarding.CompanyOnboardingModule).Assembly,
            typeof(HR.Modules.DataImport.DataImportModule).Assembly,
            typeof(HR.Modules.Identity.IdentityModule).Assembly,
            typeof(HR.Modules.Employees.EmployeesModule).Assembly,
            typeof(HR.Modules.Employees.Contracts.WorkingDays).Assembly,
            typeof(HR.Modules.Leave.LeaveModule).Assembly,
            typeof(HR.Modules.Documents.DocumentsModule).Assembly,
            typeof(HR.Modules.Tasks.TasksModule).Assembly,
            typeof(HR.Modules.Tasks.Contracts.TaskPriority).Assembly,
            typeof(HR.Modules.Notifications.NotificationsModule).Assembly,
            typeof(HR.Modules.Probation.ProbationModule).Assembly,
            typeof(HR.Modules.Reporting.ReportingModule).Assembly,
            typeof(HR.Modules.Recruitment.RecruitmentModule).Assembly,
            typeof(HR.Modules.Assets.AssetsModule).Assembly,
            typeof(HR.Modules.Sickness.SicknessModule).Assembly,
            typeof(HR.Modules.Onboarding.OnboardingModule).Assembly,
            typeof(HR.Modules.Offboarding.OffboardingModule).Assembly,
            typeof(HR.Modules.Support.SupportModule).Assembly,
            typeof(HR.Modules.Marketing.MarketingModule).Assembly,
            typeof(EnumText).Assembly,
        ];

    private static IEnumerable<(Type Type, string Member)> AllEnumMembers() =>
        Assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsEnum && !t.IsDefined(typeof(FlagsAttribute), false))
            .SelectMany(t => Enum.GetNames(t).Select(n => (t, n)));

    [Fact]
    public void Every_Enum_Member_Humanizes_To_Clean_Display_Text()
    {
        var offenders = new List<string>();

        foreach (var (type, member) in AllEnumMembers())
        {
            var value = (Enum)Enum.Parse(type, member);
            var text = EnumText.Humanize(value);

            var problem =
                string.IsNullOrWhiteSpace(text) ? "empty"
                : text != text.Trim() || text.Contains("  ", StringComparison.Ordinal) ? "stray whitespace"
                : text.Contains('_', StringComparison.Ordinal) ? "underscore"
                : LowerToUpper.IsMatch(text) ? "unsplit PascalCase"
                : !char.IsUpper(text[0]) && !char.IsDigit(text[0]) ? "not capitalised"
                : null;

            if (problem is not null)
            {
                offenders.Add($"{type.FullName}.{member} -> '{text}' ({problem})");
            }
        }

        Assert.True(offenders.Count == 0, "Enum members that do not humanize cleanly:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Trial_Expired_Subscription_Status_Is_Shown_In_Title_Case()
    {
        Assert.Equal("Trial Expired", EnumText.Humanize(HR.Modules.Companies.Contracts.SubscriptionStatus.TrialExpired));
        Assert.Equal("Past Due", EnumText.Humanize(HR.Modules.Companies.Contracts.SubscriptionStatus.PastDue));
    }
}
