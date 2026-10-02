using System.Globalization;

namespace HR.Web.E2E.Tests.Infrastructure;

public static class E2eDates
{
    public static string DaysFromToday(int days) =>
        DateTime.Today.AddDays(days).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
