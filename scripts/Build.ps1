param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $taskRoot
try {
    dotnet run --project tests/Qiye.Core.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    $taskOutput = Join-Path $taskRoot 'artifacts\release\Bingyu-win-x64'
    if ($SelfContained) { $taskOutput = Join-Path $taskRoot 'artifacts\release\Bingyu-win-x64-self-contained' }
    $taskSelfContained = if ($SelfContained.IsPresent) { 'true' } else { 'false' }
    dotnet publish src/Qiye.App/Qiye.App.csproj -c Release -r win-x64 --self-contained $taskSelfContained -p:SelfContained=$taskSelfContained -p:PublishSelfContained=$taskSelfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $taskOutput
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    foreach ($taskDoc in @('Microsoft.Web.WebView2.Core.xml', 'Microsoft.Web.WebView2.WinForms.xml', 'Microsoft.Web.WebView2.Wpf.xml')) {
        $taskDocPath = Join-Path $taskOutput $taskDoc
        if (Test-Path -LiteralPath $taskDocPath) { Remove-Item -LiteralPath $taskDocPath }
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $taskOutput '使用说明.md') -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'QUICKSTART.md') -Destination (Join-Path $taskOutput 'QUICKSTART.md') -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $taskOutput -Force
    $taskZip = $taskOutput + '.zip'
    Compress-Archive -Path (Join-Path $taskOutput '*') -DestinationPath $taskZip -Force
    Get-ChildItem -LiteralPath $taskOutput | Select-Object Name, Length
    Get-FileHash -LiteralPath $taskZip -Algorithm SHA256
}
finally { Pop-Location }
