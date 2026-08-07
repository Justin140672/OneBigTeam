using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Services;

namespace HR.Modules.Documents.Tests;

public class ScanStatusAccessGuardTests
{
    [Fact]
    public void CheckDownloadable_Returns_Null_For_Clean()
    {
        var error = ScanStatusAccessGuard.CheckDownloadable(FileScanStatus.Clean);

        Assert.Null(error);
    }

    // Theory parameters must be a publicly accessible type (xUnit requires public test methods),
    // but FileScanStatus is internal — pass the enum's underlying int value instead and cast.
    [Theory]
    [InlineData((int)FileScanStatus.Pending)]
    [InlineData((int)FileScanStatus.Scanning)]
    public void CheckDownloadable_Returns_BeingChecked_Validation_Error_For_Pending_Or_Scanning(
        int statusValue)
    {
        var error = ScanStatusAccessGuard.CheckDownloadable((FileScanStatus)statusValue);

        Assert.NotNull(error);
        Assert.Equal("validation", error!.Code);
        Assert.Equal("This document is currently being security checked.", error.Message);
    }

    [Theory]
    [InlineData((int)FileScanStatus.Infected)]
    [InlineData((int)FileScanStatus.Failed)]
    public void CheckDownloadable_Returns_FailedScan_Validation_Error_For_Infected_Or_Failed(
        int statusValue)
    {
        var error = ScanStatusAccessGuard.CheckDownloadable((FileScanStatus)statusValue);

        Assert.NotNull(error);
        Assert.Equal("validation", error!.Code);
        Assert.Equal("This document failed a security scan.", error.Message);
    }
}
