# Removes I-Team CRM for the current Windows user: the app folder, its
# shortcuts, its entry in Settings > Apps, and this computer's saved sign-in
# and preferences. The company's data stays on the server.
$ErrorActionPreference = 'SilentlyContinue'

$AppName   = 'I-Team CRM'
$Target    = $PSScriptRoot
$UninstKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ITeamCRM'

Get-Process -Name 'I-Team CRM' | Stop-Process -Force
Start-Sleep -Milliseconds 500

Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk") -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk") -Force
Remove-Item $UninstKey -Recurse -Force
Remove-Item 'HKCU:\Software\Classes\iteamcrm' -Recurse -Force
Remove-Item (Join-Path $env:APPDATA $AppName) -Recurse -Force

# Only ever delete our own install folder.
if ($Target -like "*\Programs\$AppName") {
  Set-Location $env:TEMP
  Remove-Item $Target -Recurse -Force
}
Write-Host "$AppName has been removed."
Start-Sleep -Seconds 2
