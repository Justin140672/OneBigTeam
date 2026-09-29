using System.Net.Http.Json;
using System.Text;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Tests;

public class OperationalAlertModelsTests
{
    private const string ListJson = """
        {
          "items": [
            {
              "id": "7f2e8a3c-1d4b-4c6e-9a0b-2c3d4e5f6a7b",
              "companyId": "00000000-0000-0000-0000-000000000001",
              "category": "Compliance",
              "severity": "Warning",
              "status": "Open",
              "summary": "Example",
              "occurrenceCount": 1,
              "firstOccurredAt": "2026-09-01T10:00:00+00:00",
              "lastOccurredAt": "2026-09-01T10:00:00+00:00",
              "affectedEntityType": null,
              "affectedEntityId": null,
              "affectedItemCount": null,
              "resolvedAt": null,
              "resolvedByUserId": null,
              "isRead": false
            }
          ],
          "totalCount": 1,
          "page": 1,
          "pageSize": 20
        }
        """;

    [Fact]
    public async Task ListResponse_WithNullAffectedItemCount_Deserializes()
    {
        using var content = new StringContent(ListJson, Encoding.UTF8, "application/json");

        var response = await content.ReadFromJsonAsync<OperationalAlertListResponse>();

        Assert.NotNull(response);
        var item = Assert.Single(response!.Items);
        Assert.Null(item.AffectedItemCount);
    }
}
