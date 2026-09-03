param(
    [string]$Version = "0.5.0"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$PublishDir = Join-Path $ProjectRoot "publish\win-x64"
$DistDir = Join-Path $ProjectRoot "dist"
$IssFile = Join-Path $ProjectRoot "installer\ClipFlyout.iss"

if ($Version -notmatch '^0\.\d+\.\d+$') {
    throw "Version must use the pre-1.0 format 0.x.x (for example, 0.4.1)."
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  ClipFlyout Installer & Package Builder  " -ForegroundColor Cyan
Write-Host "  Version: v$Version                      " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Clean output directories
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
if (-not (Test-Path $DistDir)) { New-Item -ItemType Directory -Path $DistDir -Force | Out-Null }

# 2. Publish self-contained single-file win-x64 binary
Write-Host "`n[1/3] Publishing self-contained win-x64 binary..." -ForegroundColor Yellow
$localDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) {
    # Prefer the per-user SDK because a PATH entry can point to the Windows
    # app-host stub, which reports "No .NET SDKs were found".
    $localDotnet
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    (Get-Command dotnet).Source
} else {
    throw "dotnet SDK was not found. Install the .NET 9 SDK first."
}
& $dotnet publish (Join-Path $ProjectRoot "ClipFlyout.csproj") -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:Version=$Version -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed!"
    exit 1
}

# 3. Create Portable Zip
Write-Host "`n[2/3] Creating Portable ZIP package..." -ForegroundColor Yellow
$ZipPath = Join-Path $DistDir "ClipFlyout-v$Version-win-x64.zip"
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -Force
Write-Host "  -> Created: $ZipPath" -ForegroundColor Green

# 4. Locate Inno Setup Compiler (ISCC.exe)
Write-Host "`n[3/3] Building Inno Setup Installer..." -ForegroundColor Yellow
$IsccCandidates = @(
    "ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$IsccPath = $null
foreach ($candidate in $IsccCandidates) {
    if (Get-Command $candidate -ErrorAction SilentlyContinue) {
        $IsccPath = (Get-Command $candidate).Source
        break
    } elseif (Test-Path $candidate) {
        $IsccPath = $candidate
        break
    }
}

if (-not $IsccPath) {
    Write-Warning "ISCC.exe not found! Please install Inno Setup 6 (winget install JRSoftware.InnoSetup)"
} else {
    Write-Host "  Using compiler: $IsccPath" -ForegroundColor Gray
    & "$IsccPath" "/DMyAppVersion=$Version" "$IssFile"
    if ($LASTEXITCODE -eq 0) {
        $SetupExe = Join-Path $DistDir "ClipFlyout-Setup-v$Version.exe"
        Write-Host "  -> Created: $SetupExe" -ForegroundColor Green
    } else {
        Write-Error "ISCC compilation failed!"
        exit 1
    }
}

Write-Host "`n==========================================" -ForegroundColor Cyan
Write-Host "  Build Completed Successfully!           " -ForegroundColor Cyan
Write-Host "  Dist assets in: $DistDir                " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Get-ChildItem -Path $DistDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
