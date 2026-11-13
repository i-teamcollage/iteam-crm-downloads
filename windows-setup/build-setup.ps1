# Builds I-Team-CRM-Setup.exe on a Windows PC with the .NET Framework
# compiler that ships with Windows (no extra tools).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File build-setup.ps1 -App "<folder>\app" -Version 0.4.0
#
# <folder>\app is the app folder from I-Team-CRM-<version>-Windows.zip (built
# on the Mac with `npm run dist:win:cloud`). Setup.cs, uninstall.ps1 and
# icon.ico sit next to this script (the setup kit zip).
param(
  [Parameter(Mandatory = $true)][string]$App,
  [Parameter(Mandatory = $true)][string]$Version,
  [string]$Out = "I-Team-CRM-Setup.exe"
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 0.4.0" }
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$app = (Resolve-Path $App).Path.TrimEnd('\')
if (-not (Test-Path (Join-Path $app 'I-Team CRM.exe'))) { throw "$app is not the app folder (I-Team CRM.exe is missing)" }
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw ".NET Framework 4 compiler not found at $csc" }

$work = Join-Path $env:TEMP ("iteam-setup-" + [guid]::NewGuid())
New-Item -ItemType Directory $work | Out-Null
try {
  (Get-Content (Join-Path $here 'Setup.cs') -Raw).Replace('@VERSION@', $Version) | Set-Content (Join-Path $work 'Setup.cs') -Encoding UTF8
  Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
  $z = [IO.Compression.ZipFile]::Open((Join-Path $work 'app.zip'), 'Create')
  foreach ($f in Get-ChildItem $app -Recurse -File) {
    $rel = $f.FullName.Substring($app.Length + 1).Replace('\', '/')
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $f.FullName, $rel, 'Optimal')
  }
  [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, (Join-Path $here 'uninstall.ps1'), 'uninstall.ps1', 'Optimal')
  $z.Dispose()
  $outPath = [IO.Path]::GetFullPath($Out)
  & $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$outPath" "/win32icon:$(Join-Path $here 'icon.ico')" `
    "/resource:$(Join-Path $work 'app.zip'),app.zip" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll /r:System.Core.dll (Join-Path $work 'Setup.cs')
  if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
  $mb = [math]::Round((Get-Item $outPath).Length / 1MB, 1)
  Write-Host "Built $outPath ($mb MB), version $Version"
} finally {
  Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
