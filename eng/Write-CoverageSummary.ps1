param(
    [Parameter(Mandatory = $true)]
    [string[]]$CoverageFiles,
    [string]$MarkdownPath,
    [string]$JsonPath,
    [double]$TargetLineCoverage = 100
)

$totals = [ordered]@{
    linesValid = 0
    linesCovered = 0
    branchesValid = 0
    branchesCovered = 0
    packages = @()
}

foreach ($path in $CoverageFiles) {
    [xml]$coverage = Get-Content ([System.IO.Path]::GetFullPath($path))
    $totals.linesValid += [int]$coverage.coverage.'lines-valid'
    $totals.linesCovered += [int]$coverage.coverage.'lines-covered'
    $totals.branchesValid += [int]$coverage.coverage.'branches-valid'
    $totals.branchesCovered += [int]$coverage.coverage.'branches-covered'

    foreach ($package in $coverage.coverage.packages.package) {
        $totals.packages += [ordered]@{
            name = [string]$package.name
            lineRate = [math]::Round(([double]$package.'line-rate') * 100, 2)
            branchRate = [math]::Round(([double]$package.'branch-rate') * 100, 2)
        }
    }
}

$lineCoverage = if ($totals.linesValid -eq 0) { 0 } else { [math]::Round(($totals.linesCovered / $totals.linesValid) * 100, 2) }
$branchCoverage = if ($totals.branchesValid -eq 0) { 0 } else { [math]::Round(($totals.branchesCovered / $totals.branchesValid) * 100, 2) }

$summary = [ordered]@{
    lineCoverage = $lineCoverage
    branchCoverage = $branchCoverage
    linesCovered = $totals.linesCovered
    linesValid = $totals.linesValid
    branchesCovered = $totals.branchesCovered
    branchesValid = $totals.branchesValid
    targetLineCoverage = $TargetLineCoverage
    packages = @($totals.packages | Sort-Object name)
}

$markdown = @"
# Coverage Summary

- Line coverage: $lineCoverage%
- Branch coverage: $branchCoverage%
- Covered lines: $($totals.linesCovered) / $($totals.linesValid)
- Covered branches: $($totals.branchesCovered) / $($totals.branchesValid)
- Target line coverage: $TargetLineCoverage%
"@

if ($MarkdownPath) {
    $markdownFullPath = [System.IO.Path]::GetFullPath($MarkdownPath)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $markdownFullPath) | Out-Null
    Set-Content -Path $markdownFullPath -Value $markdown -Encoding UTF8
}

if ($JsonPath) {
    $jsonFullPath = [System.IO.Path]::GetFullPath($JsonPath)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $jsonFullPath) | Out-Null
    $summary | ConvertTo-Json -Depth 10 | Set-Content -Path $jsonFullPath -Encoding UTF8
}

$markdown
