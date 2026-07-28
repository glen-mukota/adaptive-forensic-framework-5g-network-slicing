#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Write-Host 'Enabling the Windows components required by WSL2 and Docker Desktop for the OAI laboratory...'
dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart

Write-Host ''
Write-Host 'Windows features have been requested. Restart Windows before installing Ubuntu or Docker Desktop.' -ForegroundColor Yellow
Write-Host 'Before restarting, enable Intel Virtualization Technology (VT-x) in the Lenovo firmware/BIOS if it is currently disabled.' -ForegroundColor Yellow
Write-Host 'After restart, run: wsl.exe --install -d Ubuntu-24.04'
Write-Host 'Then install Docker Desktop with its WSL2 backend, and rerun the LabBootstrap preflight command.'
