param(
    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl,

    [Parameter(Mandatory = $false)]
    [string]$BearerToken = "",

    [Parameter(Mandatory = $false)]
    [int]$MaxAttempts = 10,

    [Parameter(Mandatory = $false)]
    [int]$DelaySeconds = 6
)

$ErrorActionPreference = "Stop"

$trimmedBaseUrl = $ApiBaseUrl.TrimEnd('/')
$uri = "$trimmedBaseUrl/health/startup-migrations"
$headers = @{}

if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
    $headers["Authorization"] = "Bearer $BearerToken"
}

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    try {
        $response = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get -TimeoutSec 15

        if ($null -eq $response.companies -or $null -eq $response.identity) {
            throw "Health payload is missing 'companies' or 'identity' sections."
        }

        $companiesStatus = [string]$response.companies.status
        $identityStatus = [string]$response.identity.status

        if ($companiesStatus -eq "succeeded" -and $identityStatus -eq "succeeded") {
            Write-Host "Startup migrations healthy. companies=$companiesStatus identity=$identityStatus"
            exit 0
        }

        throw "Startup migrations not healthy. companies=$companiesStatus identity=$identityStatus"
    }
    catch {
        if ($attempt -eq $MaxAttempts) {
            Write-Error "Health check failed after $MaxAttempts attempts at '$uri'. Last error: $($_.Exception.Message)"
            exit 1
        }

        Write-Host "Attempt $attempt/$MaxAttempts failed for '$uri'. Retrying in $DelaySeconds seconds..."
        Start-Sleep -Seconds $DelaySeconds
    }
}

Write-Error "Health check failed unexpectedly."
exit 1
