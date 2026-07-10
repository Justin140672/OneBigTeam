using System.Text;
using HR.Modules.DataImport.Features.DownloadImportTemplate;
using HR.Modules.DataImport.Services;

namespace HR.Modules.DataImport.Tests;

public class DownloadImportTemplateHandlerTests
{
    [Fact]
    public void Handle_Returns_Csv_With_Exactly_One_Header_Line_Containing_Every_Standard_Header()
    {
        var handler = new DownloadImportTemplateHandler();

        var bytes = handler.Handle();
        var csv = Encoding.UTF8.GetString(bytes);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var line = Assert.Single(lines);

        var headerCells = line.Split(',');

        foreach (var expectedHeader in StandardEmployeeColumnMapping.Default.TargetFieldToHeaderName.Values)
        {
            Assert.Contains(expectedHeader, headerCells);
        }

        Assert.Contains("First Name", headerCells);
        Assert.Contains("Last Name", headerCells);
        Assert.Contains("Work Email", headerCells);
    }
}
