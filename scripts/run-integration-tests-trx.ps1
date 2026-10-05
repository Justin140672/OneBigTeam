param(
    [string]$Filter = "",
    [string]$Configuration = "Debug"
)

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "tests\HR.Integration.Tests\HR.Integration.Tests.csproj"
$results = Join-Path $root "TestResults\integration"
New-Item -ItemType Directory -Force $results | Out-Null

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$trx = "integration-$stamp.trx"

$args = @(
    "test", $project,
    "-c", $Configuration,
    "--results-directory", $results,
    "--logger", "trx;LogFileName=$trx",
    "--logger", "console;verbosity=minimal"
)
if ($Filter) { $args += @("--filter", $Filter) }

$sw = [Diagnostics.Stopwatch]::StartNew()
dotnet @args
$sw.Stop()

$trxPath = Join-Path $results $trx
Write-Host ""
Write-Host "Elapsed: $($sw.Elapsed)"
Write-Host "TRX:     $trxPath"

if (Test-Path $trxPath) {
    [xml]$x = Get-Content $trxPath
    $ns = New-Object Xml.XmlNamespaceManager($x.NameTable)
    $ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
    Write-Host ""
    Write-Host "Slowest 25 tests:"
    $x.SelectNodes("//t:UnitTestResult", $ns) |
        ForEach-Object {
            [pscustomobject]@{
                Seconds = [math]::Round(([TimeSpan]::Parse($_.duration)).TotalSeconds, 2)
                Outcome = $_.outcome
                Test    = $_.testName
            }
        } |
        Sort-Object Seconds -Descending |
        Select-Object -First 25 |
        Format-Table -AutoSize
}
