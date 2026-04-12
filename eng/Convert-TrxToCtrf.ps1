param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [string]$ToolName = "Microsoft Testing Platform + xUnit v3"
)

$inputFullPath = [System.IO.Path]::GetFullPath($InputPath)
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
[xml]$trx = Get-Content $inputFullPath

$namespace = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
$namespace.AddNamespace("trx", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

$definitionsById = @{}
foreach ($definition in $trx.SelectNodes("//trx:TestDefinitions/trx:UnitTest", $namespace)) {
    $definitionsById[$definition.id] = $definition
}

$tests = @()
foreach ($result in $trx.SelectNodes("//trx:Results/trx:UnitTestResult", $namespace)) {
    $definition = $definitionsById[$result.testId]
    $testMethod = $definition?.TestMethod
    $durationMilliseconds = 0
    if ($result.duration) {
        $durationMilliseconds = [int][Math]::Round([TimeSpan]::Parse([string]$result.duration, [System.Globalization.CultureInfo]::InvariantCulture).TotalMilliseconds)
    }

    $status = switch -Regex ($result.outcome) {
        "^Passed$" { "passed" }
        "^Failed$" { "failed" }
        "^NotExecuted$" { "skipped" }
        "^Skipped$" { "skipped" }
        default { "other" }
    }

    $errorInfo = $result.Output.ErrorInfo
    $tests += [ordered]@{
        name = $result.testName
        suite = $testMethod.className
        status = $status
        rawStatus = $result.outcome
        duration = $durationMilliseconds
        message = if ($errorInfo.Message) { [string]$errorInfo.Message } else { $null }
        trace = if ($errorInfo.StackTrace) { [string]$errorInfo.StackTrace } else { $null }
    }
}

$times = $trx.SelectSingleNode("//trx:Times", $namespace)
$summary = [ordered]@{
    tests = $tests.Count
    passed = @($tests | Where-Object status -eq "passed").Count
    failed = @($tests | Where-Object status -eq "failed").Count
    skipped = @($tests | Where-Object status -eq "skipped").Count
    pending = 0
    other = @($tests | Where-Object status -eq "other").Count
    start = if ($times.start) { [string]$times.start } else { $null }
    stop = if ($times.finish) { [string]$times.finish } else { $null }
}

$ctrf = [ordered]@{
    results = [ordered]@{
        tool = [ordered]@{
            name = $ToolName
        }
        summary = $summary
        tests = $tests
    }
}

$outputDirectory = Split-Path -Parent $outputFullPath
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$ctrf | ConvertTo-Json -Depth 10 | Set-Content -Path $outputFullPath -Encoding UTF8
