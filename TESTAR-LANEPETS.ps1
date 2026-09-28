# Roda os testes automatizados com os acentos certos no console do Windows.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
# 29/09: com o LanePets aberto o build nao consegue trocar o LanePets.exe (MSB3027). Avisa antes.
$aberto = Get-Process -Name LanePets -ErrorAction SilentlyContinue
if ($aberto) {
    Write-Host "O LanePets esta aberto (processo $($aberto.Id -join ', ')). Feche-o com Ctrl+C no terminal dele e rode de novo." -ForegroundColor Yellow
    Write-Host "Ou encerre direto: Stop-Process -Id $($aberto.Id -join ',')" -ForegroundColor Yellow
    exit 1
}
dotnet test tests/LanePets.Tests
