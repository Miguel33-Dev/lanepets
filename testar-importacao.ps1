<#
    LanePets - teste da importacao de dados do navegador (item 11.3 do roadmap, 26/09).

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-importacao.ps1

    O QUE ELE PROVA (POST /api/admin/importar, tela migrar-dados.html)
        - simular conta o que seria gravado e NAO grava nada;
        - linha sem dados suficientes e contada como ignorada;
        - importando de verdade o registro aparece no banco e vira evento no Log de eventos;
        - importar de novo o mesmo registro nao duplica (conta como "ja existia");
        - sem sessao -> 401.

    Deixa no banco UM lancamento de teste de R$ 0,01, com data daqui a mais de um ano
    (nao mexe nos numeros do mes atual). Descricao comeca com "Teste importacao".
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
$tokenGeral = $login.corpo.data.token

$dia = (Get-Date).AddDays(400 + (Get-Random -Maximum 200)).ToString('yyyy-MM-dd')
$descricao = "Teste importacao $(Get-Random -Maximum 999999)"
$linha = @{ data = $dia; descricao = $descricao; tipo = 'Entrada'; valor = '0,01'; unidade = '' }
function Importar($simular, $linhas) { Chamar POST '/admin/importar' @{ token = $tokenGeral; simular = $simular; entradasESaidas = $linhas } }
function NoBanco() { @((Chamar GET "/admin/entradas-saidas?token=$tokenGeral&de=$dia&ate=$dia").corpo.data | Where-Object { $_.descricao -eq $descricao }).Count }

# ---------------------------------------------------------------------------
Titulo '1. Simulacao nao grava'
$s = Importar $true @($linha, @{ descricao = 'sem data'; valor = 5 })
Checar 'POST /admin/importar (simular) -> 200' ($s.status -eq 200) "($($s.corpo.error))"
if ($s.status -eq 404) { Write-Host "  [FALHA] 404 - o servidor esta rodando a build antiga? (Ctrl+C, dotnet build, dotnet run)" -ForegroundColor Red; exit 1 }
$rs = $s.corpo.data.relatorio.entradasESaidas
Checar 'resposta marcada como simulacao' ($s.corpo.data.simulacao -eq $true)
Checar 'conta 1 novo' ($rs.novos -eq 1) "($($rs | ConvertTo-Json -Compress))"
Checar 'linha sem data conta como ignorada' ($rs.ignorados -eq 1)
Checar 'as outras colecoes vem zeradas' ($s.corpo.data.relatorio.servicos.novos -eq 0 -and $s.corpo.data.relatorio.pets.novos -eq 0)
Checar 'nada foi gravado' ((NoBanco) -eq 0)

# ---------------------------------------------------------------------------
Titulo '2. Importacao de verdade'
$g = Importar $false @($linha)
Checar 'POST /admin/importar -> 200' ($g.status -eq 200) "($($g.corpo.error))"
Checar 'conta 1 novo' ($g.corpo.data.relatorio.entradasESaidas.novos -eq 1)
Checar 'lancamento esta no banco' ((NoBanco) -eq 1)
$ev = @((Chamar GET "/admin/eventos?token=$tokenGeral&limite=50&busca=$([uri]::EscapeDataString('dados do navegador'))").corpo.data.itens)
Checar 'importacao virou evento no Log de eventos' ($ev.Count -ge 1 -and $ev[0].categoria -eq 'painel') "($($ev.Count) evento(s))"
Checar 'evento traz o resumo (1 novo em entradasESaidas)' ($ev.Count -ge 1 -and $ev[0].detalhes -like '*entradasESaidas: 1 novo*') "($($ev[0].detalhes))"

# ---------------------------------------------------------------------------
Titulo '3. Importar de novo nao duplica'
$d = Importar $false @($linha)
Checar 'segunda vez: 0 novo e 1 ja existia' ($d.corpo.data.relatorio.entradasESaidas.novos -eq 0 -and $d.corpo.data.relatorio.entradasESaidas.jaExistiam -eq 1)
Checar 'continua 1 no banco' ((NoBanco) -eq 1)

# ---------------------------------------------------------------------------
Titulo '4. Permissao'
Checar 'sem sessao -> 401' ((Chamar POST '/admin/importar' @{ token = 'invalido'; simular = $true }).status -eq 401)

Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
