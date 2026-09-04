param([switch]$InstallSdk)
& (Join-Path $PSScriptRoot 'build-pass1.ps1') -InstallSdk:$InstallSdk
