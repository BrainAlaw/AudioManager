param(
    [Parameter(Mandatory = $false)]
    [string] $Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src\AudioManager\AudioManager.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\win-x64"
$installerDir = Join-Path $repoRoot "artifacts\installer"
$zipPath = Join-Path $repoRoot "artifacts\AudioManager-$Version-win-x64.zip"
$innoScript = Join-Path $repoRoot "installer\AudioManager.iss"

$iscc = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
$isccPath = if ($null -ne $iscc) {
    $iscc.Source
} else {
    $candidatePaths = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )

    $candidatePaths | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($isccPath)) {
    throw "Inno Setup compiler was not found. Install Inno Setup 6 and make sure iscc.exe is available in PATH."
}

New-Item -ItemType Directory -Force -Path $publishDir, $installerDir | Out-Null

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -p:AssemblyVersion=$Version `
    -p:FileVersion=$Version `
    -p:InformationalVersion=$Version `
    -o $publishDir

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

& $isccPath `
    "/DAppVersion=$Version" `
    "/DPublishDir=$publishDir" `
    $innoScript

Write-Host "Release artifacts:"
Write-Host "  $zipPath"
Write-Host "  $(Join-Path $installerDir "AudioManager-Setup-$Version.exe")"
