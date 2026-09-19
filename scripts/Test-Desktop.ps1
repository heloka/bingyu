param([string]$Executable)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Executable) {
    dotnet build (Join-Path $taskRoot 'src\Qiye.App\Qiye.App.csproj') -c Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $Executable = Join-Path $taskRoot 'src\Qiye.App\bin\Release\net10.0-windows\Bingyu.exe'
}
$taskRunId = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
$taskReport = Join-Path $taskRoot ('artifacts\smoke-' + $taskRunId)
$taskProfile = Join-Path $taskRoot ('artifacts\profile-' + $taskRunId)
$taskProcess = Start-Process -FilePath $Executable -ArgumentList @('--smoke-test', ('"' + $taskReport + '"'), '--data-dir', ('"' + $taskProfile + '"')) -WindowStyle Hidden -PassThru
if (-not $taskProcess.WaitForExit(60000)) { throw ('Desktop tests exceeded 60 seconds; inspect PID ' + $taskProcess.Id) }
$taskReportFile = Join-Path $taskReport 'report.json'
if (-not (Test-Path -LiteralPath $taskReportFile)) { throw ('No report generated; inspect ' + $taskReport) }
$taskResult = Get-Content -LiteralPath $taskReportFile -Raw | ConvertFrom-Json
$taskResult.checks | Select-Object name, passed, ms, error
Write-Output ('Report: ' + $taskReportFile)
if ($taskResult.failures -ne 0) { throw ($taskResult.failures.ToString() + ' desktop checks failed.') }
