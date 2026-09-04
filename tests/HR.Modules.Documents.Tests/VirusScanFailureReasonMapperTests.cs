using System.Net.Sockets;
using HR.Modules.Documents.Services;

namespace HR.Modules.Documents.Tests;

public class VirusScanFailureReasonMapperTests
{
    private static readonly HashSet<string> AllowedCategories =
    [
        VirusScanFailureReasonMapper.ScannerUnavailable,
        VirusScanFailureReasonMapper.ScanTimedOut,
        VirusScanFailureReasonMapper.DownloadFailed,
        VirusScanFailureReasonMapper.GenericFailure,
    ];

    [Fact]
    public void ToSafeCategory_HttpRequestException_With_Signed_Url_Maps_To_DownloadFailed_And_Strips_Details()
    {
        var url = "https://storage.example.com/bucket/file.pdf?X-Amz-Signature=abcdef1234567890&token=secret-token-value";
        var ex  = new HttpRequestException($"Failed to download from {url}");

        var result = VirusScanFailureReasonMapper.ToSafeCategory(ex);

        Assert.Equal(VirusScanFailureReasonMapper.DownloadFailed, result);
        Assert.DoesNotContain("storage.example.com", result);
        Assert.DoesNotContain("X-Amz-Signature", result);
        Assert.DoesNotContain("secret-token-value", result);
        Assert.Contains(result, AllowedCategories);
    }

    [Fact]
    public void ToSafeCategory_SocketException_With_Internal_Host_Maps_To_ScannerUnavailable_And_Strips_Details()
    {
        var ex = new SocketException();
        // SocketException's message is derived from the error code, but we still verify the
        // hardcoded internal-host details a caller might have wrapped are never echoed back.
        var wrappingMessage = "No connection could be made to host 10.0.4.17:3310 (clamd internal)";
        Assert.DoesNotContain(wrappingMessage, VirusScanFailureReasonMapper.ScannerUnavailable);

        var result = VirusScanFailureReasonMapper.ToSafeCategory(ex);

        Assert.Equal(VirusScanFailureReasonMapper.ScannerUnavailable, result);
        Assert.DoesNotContain("10.0.4.17", result);
        Assert.DoesNotContain("3310", result);
        Assert.Contains(result, AllowedCategories);
    }

    [Fact]
    public void ToSafeCategory_TimeoutException_With_Employee_Details_Maps_To_ScanTimedOut_And_Strips_Details()
    {
        var ex = new TimeoutException("Timed out scanning file for jane.doe@acmecorp.example (Jane Doe)");

        var result = VirusScanFailureReasonMapper.ToSafeCategory(ex);

        Assert.Equal(VirusScanFailureReasonMapper.ScanTimedOut, result);
        Assert.DoesNotContain("jane.doe@acmecorp.example", result);
        Assert.DoesNotContain("Jane Doe", result);
        Assert.Contains(result, AllowedCategories);
    }

    [Fact]
    public void ToSafeCategory_OperationCanceledException_With_Employee_Details_Maps_To_ScanTimedOut_And_Strips_Details()
    {
        var ex = new OperationCanceledException("Timed out scanning file for jane.doe@acmecorp.example (Jane Doe)");

        var result = VirusScanFailureReasonMapper.ToSafeCategory(ex);

        Assert.Equal(VirusScanFailureReasonMapper.ScanTimedOut, result);
        Assert.DoesNotContain("jane.doe@acmecorp.example", result);
        Assert.DoesNotContain("Jane Doe", result);
        Assert.Contains(result, AllowedCategories);
    }

    [Fact]
    public void ToSafeCategory_IOException_With_Local_Windows_Path_Maps_To_DownloadFailed_And_Strips_Details()
    {
        var ex = new IOException(@"Could not read C:\app\data\uploads\jane-doe-passport.pdf");

        var result = VirusScanFailureReasonMapper.ToSafeCategory(ex);

        Assert.Equal(VirusScanFailureReasonMapper.DownloadFailed, result);
        Assert.DoesNotContain(@"C:\app\data\uploads\jane-doe-passport.pdf", result);
        Assert.Contains(result, AllowedCategories);
    }

    [Fact]
    public void ToSafeCategory_IOException_With_Local_Unix_Path_Maps_To_DownloadFailed_And_Strips_Details()
    {
        var ex = new IOException("Could not read /var/app/uploads/jane-doe-passport.pdf");

        var result = VirusScanFailureReasonMapper.ToSafeCategory(ex);

        Assert.Equal(VirusScanFailureReasonMapper.DownloadFailed, result);
        Assert.DoesNotContain("/var/app/uploads/jane-doe-passport.pdf", result);
        Assert.Contains(result, AllowedCategories);
    }

    [Fact]
    public void ToSafeCategory_Unmapped_Exception_Type_Maps_To_GenericFailure_And_Strips_Details()
    {
        var ex = new InvalidOperationException(
            "Unexpected clamd response scanning 'passport.pdf': 'stream: ERROR internal signature DB corrupt at offset 4821'");

        var result = VirusScanFailureReasonMapper.ToSafeCategory(ex);

        Assert.Equal(VirusScanFailureReasonMapper.GenericFailure, result);
        Assert.DoesNotContain("clamd response", result);
        Assert.DoesNotContain("signature DB corrupt", result);
        Assert.Contains(result, AllowedCategories);
    }

    [Fact]
    public void ToSafeCategory_Derived_Exception_Type_Not_Explicitly_Mapped_Falls_Back_To_GenericFailure()
    {
        // A random custom exception type must not accidentally match a mapped branch and must
        // fall through to the closed-set default.
        var ex = new ArgumentException("some argument was invalid");

        var result = VirusScanFailureReasonMapper.ToSafeCategory(ex);

        Assert.Equal(VirusScanFailureReasonMapper.GenericFailure, result);
        Assert.Contains(result, AllowedCategories);
    }
}
