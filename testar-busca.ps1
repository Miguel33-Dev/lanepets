<#
    LanePets - teste da busca no servidor (item 16 do roadmap).

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-busca.ps1

    O QUE ELE PROVA
        - GET /api/admin/pagamentos?busca= aceita varias palavras (todas precisam aparecer),
          fora de ordem, sem acento e sem diferenca de maiuscula;
        - palavra que nao existe -> lista vazia;
        - os filtros que o Dashboard usa nos links (?status=Pendente, ?reembolso=true) respondem.
    As buscas de Clientes, Pets, Produtos e Pedidos rodam no navegador (js/busca.js) e
    foram testadas no Chromium; este script cobre a unica busca que roda no servidor.
    Nao grava nada.
#>

$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5180/api'

$script:ok = 0
$script:falhou = 0

function Titulo($texto) { Write-Host "`n=== $texto ===" -ForegroundColor Cyan }

function Checar($descricao, $condicao, $detalhe = '') {
    if ($condicao) {
        Write-Host "  [OK]    $descricao" -ForegroundColor Green
        $script:ok++
    } else {
        Write-Host "  [FALHA] $descricao $detalhe" -ForegroundColor Red
        $script:falhou++
    }
}

# Chama a API e devolve @{ status = <codigo http>; corpo = <objeto> }.
# Nao lanca excecao em 4xx: o status E o resultado do teste.
function Chamar($metodo, $caminho, $corpo, $token) {
    $parametros = @{ Method = $metodo; Uri = "$base$caminho"; UseBasicParsing = $true }
    if ($null -ne $corpo) {
        $parametros.Body = ([System.Text.Encoding]::UTF8.GetBytes(($corpo | ConvertTo-Json -Depth 6)))
        $parametros.ContentType = 'application/json; charset=utf-8'
    }
    if ($token) { $parametros.Headers = @{ 'X-LanePets-Client' = $token } }

    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $r = Invoke-WebRequest @parametros -SkipHttpErrorCheck
        $texto = $r.Content
        $status = [int]$r.StatusCode
    } else {
        try {
            $r = Invoke-WebRequest @parametros
            $texto = $r.Content
            $status = [int]$r.StatusCode
        } catch [System.Net.WebException] {
            $resposta = $_.Exception.Response
            if ($null -eq $resposta) { throw }
            $status = [int]$resposta.StatusCode
            $leitor = New-Object System.IO.StreamReader($resposta.GetResponseStream())
            $texto = $leitor.ReadToEnd()
            $leitor.Close()
        }
    }
    $dados = $null
    try { $dados = $texto | ConvertFrom-Json } catch {}
    return @{ status = $status; corpo = $dados }
}

Titulo 'Servidor no ar'
try { Checar 'API respondendo' ((Chamar GET '/health').status -eq 200) }
catch { Write-Host "  [FALHA] Nao consegui falar com $base. Rode 'dotnet run' antes." -ForegroundColor Red; exit 1 }
$login = Chamar POST '/login' @{ email = 'admin@gmail.com'; senha = '123456' }
if ($login.status -ne 200) { Write-Host "  [FALHA] Login do admin falhou: $($login.corpo.error)" -ForegroundColor Red; exit 1 }
$tk = $login.corpo.data.token

function Buscar($texto) { @((Chamar GET "/admin/pagamentos?token=$tk&busca=$([uri]::EscapeDataString($texto))").corpo.data.itens) }

Titulo 'Busca de pagamentos com varias palavras'
$todos = @((Chamar GET "/admin/pagamentos?token=$tk").corpo.data.itens)
$alvo = @($todos | Where-Object { $_.cliente -and $_.cliente.Trim().Contains(' ') })[0]
if (-not $alvo) { $alvo = @($todos | Where-Object { $_.cliente })[0] }
if (-not $alvo) { Write-Host '  [PULADO] nenhum pagamento com cliente no banco. Rode antes o testar-pagamentos.ps1.' -ForegroundColor Yellow }
else {
    $partes = $alvo.cliente.Trim() -split '\s+'
    $invertido = (@($partes[-1], $partes[0]) -join ' ').ToUpperInvariant()
    Checar "nome fora de ordem e em maiuscula ('$invertido') acha o pagamento" (@(Buscar $invertido | Where-Object { $_.id -eq $alvo.id }).Count -eq 1)
    Checar 'nome + codigo da origem acha o pagamento' (@(Buscar "$($partes[0]) $($alvo.origemId)" | Where-Object { $_.id -eq $alvo.id }).Count -eq 1)
    Checar 'todas as palavras precisam aparecer (palavra inexistente -> vazio)' ((Buscar "$($partes[0]) palavraquenaoexiste123").Count -eq 0)
    $semAcento = ($alvo.cliente.Normalize([Text.NormalizationForm]::FormD) -replace '\p{Mn}', '')
    Checar 'sem acento acha igual' (@(Buscar $semAcento | Where-Object { $_.id -eq $alvo.id }).Count -eq 1)
}

Titulo 'Filtros usados pelos links do Dashboard'
$r = Chamar GET "/admin/pagamentos?token=$tk&status=Pendente"
Checar '?status=Pendente -> 200 e so Pendente' ($r.status -eq 200 -and @($r.corpo.data.itens | Where-Object { $_.status -ne 'Pendente' }).Count -eq 0)
$r = Chamar GET "/admin/pagamentos?token=$tk&reembolso=true"
Checar '?reembolso=true -> 200 e so reembolso pendente' ($r.status -eq 200 -and @($r.corpo.data.itens | Where-Object { -not $_.reembolsoPendente }).Count -eq 0)

Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
