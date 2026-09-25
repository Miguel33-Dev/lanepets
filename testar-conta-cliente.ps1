<#
    LanePets - teste do acesso da conta do CLIENTE (item 2 do roadmap).

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-conta-cliente.ps1

    O QUE ELE PROVA
        1. Trocar e-mail e senha exige a SENHA ATUAL, mesmo com sessao valida.
        2. Depois da troca, o login so funciona com os dados novos.
        3. Trocar a senha derruba as OUTRAS sessoes do cliente e mantem a atual.
        4. O e-mail continua unico: nao da para assumir o e-mail de outra conta.

    O script cria duas contas de teste com e-mail aleatorio e nao encosta em
    nenhum dado que ja exista.
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

function NovaConta($rotulo) {
    $sufixo = [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $email = "teste.$rotulo.$sufixo@lanepets.test"
    $r = Chamar POST '/cliente/cadastro' @{
        nome = "Teste $rotulo"; email = $email; senha = 'SenhaTeste123!';
        telefone = '11999990000'; endereco = 'Rua de teste, 1'; pet = "Pet$rotulo"; tipo = 'Cachorro'
    }
    if ($r.status -ne 200) { throw "Cadastro de $rotulo falhou (HTTP $($r.status)): $($r.corpo.error)" }
    return @{ email = $email; token = $r.corpo.data.token }
}

# ---------------------------------------------------------------------------
Titulo 'Servidor no ar'
try {
    $ping = Chamar GET '/public/servicos'
    Checar 'API respondendo' ($ping.status -lt 500)
} catch {
    Write-Host "  [FALHA] Nao consegui falar com $base. Rode 'dotnet run' antes." -ForegroundColor Red
    exit 1
}

$a = NovaConta 'A'
$b = NovaConta 'B'

# ---------------------------------------------------------------------------
Titulo '1. Alterar senha'
Checar 'sem sessao: 401' ((Chamar PUT '/cliente/conta/senha' @{ senhaAtual = 'SenhaTeste123!'; novaSenha = 'NovaSenha456'; confirmarSenha = 'NovaSenha456' }).status -eq 401)
Checar 'senha atual errada: 400' ((Chamar PUT '/cliente/conta/senha' @{ senhaAtual = 'errada'; novaSenha = 'NovaSenha456'; confirmarSenha = 'NovaSenha456' } $a.token).status -eq 400)
Checar 'nova senha curta: 400'   ((Chamar PUT '/cliente/conta/senha' @{ senhaAtual = 'SenhaTeste123!'; novaSenha = 'curta'; confirmarSenha = 'curta' } $a.token).status -eq 400)
Checar 'confirmacao diferente: 400' ((Chamar PUT '/cliente/conta/senha' @{ senhaAtual = 'SenhaTeste123!'; novaSenha = 'NovaSenha456'; confirmarSenha = 'OutraCoisa1' } $a.token).status -eq 400)
Checar 'nova igual a atual: 400' ((Chamar PUT '/cliente/conta/senha' @{ senhaAtual = 'SenhaTeste123!'; novaSenha = 'SenhaTeste123!'; confirmarSenha = 'SenhaTeste123!' } $a.token).status -eq 400)

# segunda sessao da conta A, "em outro aparelho"
$outroAparelho = (Chamar POST '/cliente/login' @{ email = $a.email; senha = 'SenhaTeste123!' }).corpo.data.token
$troca = Chamar PUT '/cliente/conta/senha' @{ senhaAtual = 'SenhaTeste123!'; novaSenha = 'NovaSenha456'; confirmarSenha = 'NovaSenha456' } $a.token
Checar 'troca com senha atual certa: 200' ($troca.status -eq 200) "-> $($troca.corpo.error)"
Checar 'sessao que trocou continua valida'   ((Chamar GET '/cliente/conta' $null $a.token).status -eq 200)
Checar 'sessao do outro aparelho caiu (401)' ((Chamar GET '/cliente/conta' $null $outroAparelho).status -eq 401)
Checar 'login com a senha antiga falha'      ((Chamar POST '/cliente/login' @{ email = $a.email; senha = 'SenhaTeste123!' }).status -ne 200)
Checar 'login com a senha nova funciona'     ((Chamar POST '/cliente/login' @{ email = $a.email; senha = 'NovaSenha456' }).status -eq 200)

# ---------------------------------------------------------------------------
Titulo '2. Alterar e-mail'
$novoEmail = "novo.$([Guid]::NewGuid().ToString('N').Substring(0, 8))@lanepets.test"
Checar 'e-mail invalido: 400'      ((Chamar PUT '/cliente/conta/email' @{ novoEmail = 'nao-e-email'; senhaAtual = 'NovaSenha456' } $a.token).status -eq 400)
Checar 'senha atual errada: 400'   ((Chamar PUT '/cliente/conta/email' @{ novoEmail = $novoEmail; senhaAtual = 'errada' } $a.token).status -eq 400)
Checar 'e-mail de outra conta: 400' ((Chamar PUT '/cliente/conta/email' @{ novoEmail = $b.email; senhaAtual = 'NovaSenha456' } $a.token).status -eq 400)
$trocaEmail = Chamar PUT '/cliente/conta/email' @{ novoEmail = $novoEmail; senhaAtual = 'NovaSenha456' } $a.token
Checar 'troca valida: 200' ($trocaEmail.status -eq 200) "-> $($trocaEmail.corpo.error)"
Checar 'GET conta mostra o e-mail novo'   ((Chamar GET '/cliente/conta' $null $a.token).corpo.data.email -eq $novoEmail)
Checar 'login com o e-mail antigo falha'  ((Chamar POST '/cliente/login' @{ email = $a.email; senha = 'NovaSenha456' }).status -ne 200)
Checar 'login com o e-mail novo funciona' ((Chamar POST '/cliente/login' @{ email = $novoEmail; senha = 'NovaSenha456' }).status -eq 200)
Checar 'conta B continua intacta'         ((Chamar POST '/cliente/login' @{ email = $b.email; senha = 'SenhaTeste123!' }).status -eq 200)

# ---------------------------------------------------------------------------
Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
