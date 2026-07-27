using System.Text;
using HR.Modules.DataImport.Services;

namespace HR.Modules.DataImport.Features.DownloadImportTemplate;

/// <summary>
/// Generates a blank employee import CSV template directly from
/// <see cref="StandardEmployeeColumnMapping.Default"/>, so the downloadable template can never
/// drift out of sync with the headers the parser/validator actually expect.
///
/// The Employee Number column's behaviour is mode-dependent (required + format/duplicate
/// validated in Manual mode; must be left blank in Automatic mode — see
/// EmployeeStagingRowValidator). This is intentionally NOT expressed as an extra leading row in
/// the CSV itself: EmployeeImportFileParser always treats row 1 as the header row and every
/// subsequent line as a data row, so injecting an instructions line here would corrupt re-uploads
/// of this same template (off-by-one row numbers, a bogus "data row" full of validation errors).
/// The mode-dependent instructions are instead surfaced by the UI's import screen (see the
/// Data Import UI wave) next to the template download action.
/// </summary>
internal sealed class DownloadImportTemplateHandler
{
    public byte[] Handle()
    {
        var headers = StandardEmployeeColumnMapping.Default.TargetFieldToHeaderName.Values;

        var csv = new StringBuilder();
        csv.Append(string.Join(',', headers.Select(CsvEscape))).Append('\n');

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuoting)
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
