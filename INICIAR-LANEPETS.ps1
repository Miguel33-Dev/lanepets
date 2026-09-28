$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
# Acentos certos no console do Windows (log do app em UTF-8).
chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
dotnet run --launch-profile LanePets
