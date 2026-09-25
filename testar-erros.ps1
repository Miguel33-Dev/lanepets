<#
    LanePets - teste do tratamento global de erros (item 14 do roadmap).

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-erros.ps1

    O QUE ELE PROVA
        Que toda resposta de erro da API vem no envelope { ok:false, error, codigo }
        com o codigo certo, e sem texto tecnico:
            ERR-4000 regra de negocio (400)   ERR-4001 corpo em formato errado (400)
            ERR-4010 sem sessao (401)         ERR-4040 rota inexistente (404)
            ERR-4050 metodo errado (405)
        O ERR-5001 (falha inesperada) nao tem como ser provocado de fora sem
        quebrar algo de proposito; ele e conferido lendo logs/erros-*.log quando
        acontecer (a referencia da tela e a mesma do arquivo).
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

function Envelope($descricao, $r, $status, $codigo) {
    Checar "$descricao -> HTTP $status" ($r.status -eq $status) "(veio HTTP $($r.status))"
    Checar "$descricao -> envelope ok=false com codigo $codigo" ($null -ne $r.corpo -and $r.corpo.ok -eq $false -and $r.corpo.codigo -eq $codigo) "(codigo: $($r.corpo.codigo))"
    $texto = [string]$r.corpo.error
    Checar "$descricao -> mensagem sem texto tecnico" ($texto.Length -gt 0 -and $texto -notmatch 'Exception|at LanePets|System\.|SQLite|stack' ) "(`"$texto`")"
}

# ---------------------------------------------------------------------------
Titulo 'Servidor no ar'
try {
    $ping = Chamar GET '/health'
    Checar 'API respondendo' ($ping.status -eq 200)
} catch {
    Write-Host "  [FALHA] Nao consegui falar com $base. Rode 'dotnet run' antes." -ForegroundColor Red
    exit 1
}

Titulo 'Codigos de erro'
Envelope 'login do cliente com senha errada' (Chamar POST '/cliente/login' @{ email = 'ninguem@lanepets.test'; senha = 'Errada123' }) 400 'ERR-4000'
Envelope 'numero enviado como texto'         (Chamar POST '/admin/produtos/PRD-X/estoque' @{ token = 'x'; estoque = 'abc'; estoqueMinimo = 0; controlaEstoque = $true }) 400 'ERR-4001'
Envelope 'painel sem sessao'                 (Chamar GET '/admin/me?token=invalido') 401 'ERR-4010'
Envelope 'area do cliente sem sessao'        (Chamar GET '/cliente/conta') 401 'ERR-4010'
Envelope 'rota que nao existe'               (Chamar GET '/rota-que-nao-existe') 404 'ERR-4040'
Envelope 'metodo errado (GET em /login)'     (Chamar GET '/login') 405 'ERR-4050'

# ---------------------------------------------------------------------------
Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
