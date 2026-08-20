using ClosedXML.Excel;
using HR.Modules.DataImport.Services;

namespace HR.Modules.DataImport.Features.DownloadImportTemplate;

/// <summary>
/// Generates a blank employee import XLSX template directly from
/// <see cref="StandardEmployeeColumnMapping.Default"/>, so the downloadable template can never
/// drift out of sync with the headers the parser/validator actually expect.
///
/// The workbook contains:
///  - An "Employee Import" sheet with the header row only (row 1), matching
///    <see cref="EmployeeImportFileParser"/>'s expectation that row 1 is always the header and
///    every subsequent row is data — so the template can be filled in and re-uploaded unchanged.
///  - A cell comment on each header explaining whether the field is required and its expected
///    format, so the instructions travel with the file itself.
///  - Native data validation (dropdown) lists on enum-like columns (currently Salary Type).
///  - An "Instructions" sheet with the full field reference table.
/// </summary>
internal sealed class DownloadImportTemplateHandler
{
    private static readonly (string Field, bool Required, string Format)[] FieldReference =
    [
        ("First Name", true, "Free text"),
        ("Last Name", true, "Free text"),
        ("Work Email", true, "Valid email address"),
        ("Start Date", true, "yyyy-MM-dd or dd/MM/yyyy"),
        ("Preferred Name", false, "Free text"),
        ("Personal Email", false, "Free text"),
        ("Nationality", true, "Free text"),
        ("Gender", true, "Free text"),
        ("Date Of Birth", true, "yyyy-MM-dd or dd/MM/yyyy; must be in the past"),
        ("Continuous Service Date", false, "yyyy-MM-dd or dd/MM/yyyy"),
        ("Probation End Date", false, "yyyy-MM-dd or dd/MM/yyyy"),
        ("Employee Number", false, "Required if the company's Employee Number Mode is Manual; must be left blank if Automatic. Must be unique within the file and within the company"),
        ("Manager Reference", false, "Must match another row's Employee Number/Work Email in the file, or an existing employee's Employee Number/Work Email in the company"),
        ("Department", true, "Name; auto-created with a warning if it doesn't already exist for the company"),
        ("Location", true, "Name; auto-created with a warning if it doesn't already exist for the company"),
        ("Employment Type", true, "Name; auto-created with a warning if it doesn't already exist for the company"),
        ("Position Profile", true, "Title; auto-created with a warning if it doesn't already exist — only when both Department and Location are present and resolvable on that row"),
        ("Salary Amount", true, "Positive number"),
        ("Salary Type", false, "One of: Annual, Hourly, Daily"),
        ("Currency", false, "3-letter currency code (e.g. GBP, USD)"),
        // Hours Per Week and FTE are intentionally not import columns — both are always calculated
        // from Working Days + Hours Per Day rather than imported directly.
        ("Leave Balance Days", false, "Non-negative number; sets the employee's Annual Leave balance (the only leave type imported)"),
        ("Working Days", false, "Comma-separated day names, e.g. Monday,Tuesday,Wednesday,Thursday,Friday"),
        ("Hours Per Day", false, "Positive number"),
    ];

    // Field -> allowed values, for columns that should get a native Excel dropdown.
    private static readonly Dictionary<string, string[]> EnumValuesByHeader = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Salary Type"] = ["Annual", "Hourly", "Daily"],
    };

    public byte[] Handle()
    {
        var headers = StandardEmployeeColumnMapping.Default.TargetFieldToHeaderName.Values.ToList();
        var referenceByField = FieldReference.ToDictionary(f => f.Field, StringComparer.OrdinalIgnoreCase);

        using var workbook = new XLWorkbook();

        var sheet = workbook.Worksheets.Add("Employee Import");
        for (var col = 0; col < headers.Count; col++)
        {
            var header = headers[col];
            var cell = sheet.Cell(1, col + 1);
            cell.Value = header;
            cell.Style.Font.Bold = true;

            if (referenceByField.TryGetValue(header, out var reference))
            {
                var requiredText = reference.Required ? "Required" : "Optional";
                cell.CreateComment().AddText($"{requiredText}\nFormat: {reference.Format}");
            }

            if (EnumValuesByHeader.TryGetValue(header, out var allowedValues))
            {
                var validationRange = sheet.Range(2, col + 1, 1000, col + 1);
                var validation = validationRange.CreateDataValidation();
                // The explicit-list form of Excel's formula1 must be a quoted string literal
                // ("A,B,C") — without the quotes it's parsed as an (invalid) range reference,
                // which is what was triggering Excel's "Removed Feature: Data validation" repair
                // prompt on open.
                validation.List($"\"{string.Join(',', allowedValues)}\"", true);
                validation.InputMessage = $"Choose one of: {string.Join(", ", allowedValues)}";
                validation.ErrorMessage = $"Value must be one of: {string.Join(", ", allowedValues)}";
                validation.ErrorStyle = XLErrorStyle.Stop;
            }
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();

        var instructions = workbook.Worksheets.Add("Instructions");
        instructions.Cell(1, 1).Value = "Field";
        instructions.Cell(1, 2).Value = "Required";
        instructions.Cell(1, 3).Value = "Format";
        instructions.Row(1).Style.Font.Bold = true;

        for (var i = 0; i < FieldReference.Length; i++)
        {
            var (field, required, format) = FieldReference[i];
            var row = i + 2;
            instructions.Cell(row, 1).Value = field;
            instructions.Cell(row, 2).Value = required ? "Required" : "Optional";
            instructions.Cell(row, 3).Value = format;
        }

        instructions.SheetView.FreezeRows(1);
        instructions.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
