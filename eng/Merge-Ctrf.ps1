param(
    [Parameter(Mandatory = $true)]
    [string[]]$InputPaths,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$documents = foreach ($path in $InputPaths) {
    Get-Content ([System.IO.Path]::GetFullPath($path)) -Raw | ConvertFrom-Json -Depth 20
}

$allTests = @($documents | ForEach-Object { $_.results.tests })
$start = ($documents | ForEach-Object { $_.results.summary.start } | Where-Object { $_ } | Sort-Object | Select-Object -First 1)
$stop = ($documents | ForEach-Object { $_.results.summary.stop } | Where-Object { $_ } | Sort-Object | Select-Object -Last 1)

$merged = [ordered]@{
    results = [ordered]@{
        tool = [ordered]@{
            name = "Microsoft Testing Platform + xUnit v3"
        }
        summary = [ordered]@{
            tests = $allTests.Count
            passed = @($allTests | Where-Object status -eq "passed").Count
            failed = @($allTests | Where-Object status -eq "failed").Count
            skipped = @($allTests | Where-Object status -eq "skipped").Count
            pending = 0
            other = @($allTests | Where-Object status -eq "other").Count
            start = $start
            stop = $stop
        }
        tests = $allTests
    }
}

$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFullPath
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$merged | ConvertTo-Json -Depth 20 | Set-Content -Path $outputFullPath -Encoding UTF8
