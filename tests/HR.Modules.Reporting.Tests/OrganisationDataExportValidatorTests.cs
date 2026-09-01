using HR.Modules.Reporting.Features.DownloadOrganisationDataExport;
using HR.Modules.Reporting.Features.GetLatestOrganisationDataExport;
using HR.Modules.Reporting.Features.ListOrganisationDataExports;
using HR.Modules.Reporting.Features.RequestOrganisationDataExport;

namespace HR.Modules.Reporting.Tests;

public class OrganisationDataExportValidatorTests
{
    [Fact]
    public void Request_Validator_Rejects_Empty_CompanyId()
    {
        var result = new RequestOrganisationDataExportValidator()
            .Validate(new RequestOrganisationDataExportRequest { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Request_Validator_Accepts_CompanyId()
    {
        var result = new RequestOrganisationDataExportValidator()
            .Validate(new RequestOrganisationDataExportRequest { CompanyId = Guid.NewGuid() });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GetLatest_Validator_Rejects_Empty_CompanyId()
    {
        Assert.False(new GetLatestOrganisationDataExportValidator()
            .Validate(new GetLatestOrganisationDataExportRequest { CompanyId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void List_Validator_Rejects_Empty_CompanyId()
    {
        Assert.False(new ListOrganisationDataExportsValidator()
            .Validate(new ListOrganisationDataExportsRequest { CompanyId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Download_Validator_Requires_Both_Ids()
    {
        var validator = new DownloadOrganisationDataExportValidator();
        Assert.False(validator.Validate(new DownloadOrganisationDataExportRequest { CompanyId = Guid.NewGuid(), ExportId = Guid.Empty }).IsValid);
        Assert.False(validator.Validate(new DownloadOrganisationDataExportRequest { CompanyId = Guid.Empty, ExportId = Guid.NewGuid() }).IsValid);
        Assert.True(validator.Validate(new DownloadOrganisationDataExportRequest { CompanyId = Guid.NewGuid(), ExportId = Guid.NewGuid() }).IsValid);
    }
}
