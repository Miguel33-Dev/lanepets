<#
    LanePets - teste do log de eventos (item 15 do roadmap).

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-eventos.ps1

    O QUE ELE PROVA
        - login recusado (admin e cliente), conta criada, troca de senha e
          alteracao feita no painel viram evento, com autor e nivel certos;
        - a senha digitada NUNCA aparece no log;
        - so o Administrador Geral consulta: sem sessao 401, admin comum 403.

    Cria uma conta de cliente e um admin comum de teste (removido no fim).
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

function Eventos($busca) {
    (Chamar GET "/admin/eventos?token=$tokenGeral&limite=200&busca=$([uri]::EscapeDataString($busca))").corpo.data
}

# ---------------------------------------------------------------------------
Titulo 'Servidor no ar'
try { Checar 'API respondendo' ((Chamar GET '/health').status -eq 200) }
catch { Write-Host "  [FALHA] Nao consegui falar com $base. Rode 'dotnet run' antes." -ForegroundColor Red; exit 1 }

$login = Chamar POST '/login' @{ email = 'admin@gmail.com'; senha = '123456' }
if ($login.status -ne 200) { Write-Host "  [FALHA] Login do admin falhou: $($login.corpo.error)" -ForegroundColor Red; exit 1 }
$tokenGeral = $login.corpo.data.token

# ---------------------------------------------------------------------------
Titulo '1. Autenticacao'
$senhaErrada = "SenhaErradaTeste$(Get-Random)"
Chamar POST '/login' @{ email = 'admin@gmail.com'; senha = $senhaErrada } | Out-Null
$ev = Eventos 'admin@gmail.com'
Checar 'login administrativo recusado registrado (aviso)' (@($ev.itens | Where-Object { $_.acao -eq 'Login administrativo recusado' -and $_.nivel -eq 'aviso' }).Count -gt 0)
Checar 'login administrativo com sucesso registrado'      (@($ev.itens | Where-Object { $_.acao -eq 'Login administrativo' }).Count -gt 0)
Checar 'a senha digitada NAO aparece no log'              ((Eventos $senhaErrada).total -eq 0)

$emailFantasma = "fantasma.$([Guid]::NewGuid().ToString('N').Substring(0, 8))@lanepets.test"
Chamar POST '/cliente/login' @{ email = $emailFantasma; senha = 'Qualquer123' } | Out-Null
$ev = Eventos $emailFantasma
Checar 'login de cliente com e-mail inexistente registrado' (@($ev.itens | Where-Object { $_.acao -eq 'Login de cliente recusado' -and $_.detalhes -like '*não cadastrado*' }).Count -eq 1)

# ---------------------------------------------------------------------------
Titulo '2. Conta do cliente'
$email = "evento.$([Guid]::NewGuid().ToString('N').Substring(0, 8))@lanepets.test"
$cad = Chamar POST '/cliente/cadastro' @{ nome = 'Teste Evento'; email = $email; senha = 'Senha1234'; telefone = '11999990000'; pet = 'Rex'; tipo = 'Cachorro' }
Checar 'cadastro -> 200' ($cad.status -eq 200) "($($cad.corpo.error))"
$tokenCli = $cad.corpo.data.token
Chamar PUT '/cliente/conta/senha' @{ senhaAtual = 'Errada999'; novaSenha = 'Nova12345'; confirmarSenha = 'Nova12345' } $tokenCli | Out-Null
Chamar PUT '/cliente/conta/senha' @{ senhaAtual = 'Senha1234'; novaSenha = 'Nova12345'; confirmarSenha = 'Nova12345' } $tokenCli | Out-Null
$ev = Eventos $email
Checar 'conta de cliente criada registrada' (@($ev.itens | Where-Object { $_.acao -eq 'Conta de cliente criada' }).Count -eq 1)
Checar 'troca de senha recusada registrada' (@($ev.itens | Where-Object { $_.acao -eq 'Troca de senha recusada' -and $_.nivel -eq 'aviso' }).Count -eq 1)
Checar 'senha alterada registrada'          (@($ev.itens | Where-Object { $_.acao -eq 'Senha alterada' }).Count -eq 1)
Checar 'nenhuma das senhas aparece no log'  ((Eventos 'Nova12345').total -eq 0 -and (Eventos 'Errada999').total -eq 0)

# ---------------------------------------------------------------------------
Titulo '3. Painel'
$idCli = "CLI-EVT-$(Get-Random -Maximum 999999)"
Chamar POST '/admin/sync/clientes' @{ token = $tokenGeral; criados = @(@{ id = $idCli; nome = 'Cliente Evento'; telefone = '11988887777' }); atualizados = @(); removidos = @() } | Out-Null
$ev = Eventos $idCli
Checar 'cliente criado pelo painel registrado com o autor' (@($ev.itens | Where-Object { $_.acao -eq 'Cliente criado' -and $_.autor -eq 'admin@gmail.com' -and $_.origem -eq 'admin' }).Count -eq 1)
Chamar POST '/admin/sync/clientes' @{ token = $tokenGeral; criados = @(); atualizados = @(); removidos = @($idCli) } | Out-Null
Checar 'exclusao registrada como aviso' (@((Eventos $idCli).itens | Where-Object { $_.acao -eq 'Cliente excluído' -and $_.nivel -eq 'aviso' }).Count -eq 1)

# ---------------------------------------------------------------------------
Titulo '4. Quem pode consultar'
Checar 'sem sessao -> 401' ((Chamar GET '/admin/eventos?token=invalido').status -eq 401)
$emailComum = "comum.$([Guid]::NewGuid().ToString('N').Substring(0, 8))@lanepets.test"
$criado = Chamar POST '/admin/usuarios' @{ token = $tokenGeral; nome = 'Admin Comum Teste'; email = $emailComum; senha = 'Senha1234'; confirmarSenha = 'Senha1234' }
$idComum = $criado.corpo.data.id
Chamar PUT "/admin/usuarios/$idComum/permissoes" @{ token = $tokenGeral; acessoTotal = $true; modulos = @() } | Out-Null
$tokenComum = (Chamar POST '/login' @{ email = $emailComum; senha = 'Senha1234' }).corpo.data.token
Checar 'admin comum, mesmo com acesso total -> 403' ((Chamar GET "/admin/eventos?token=$tokenComum").status -eq 403)
Start-Sleep -Milliseconds 800   # o evento de acesso negado e gravado em segundo plano
$ev = Eventos 'Acesso negado'
Checar 'o acesso negado tambem virou evento' (@($ev.itens | Where-Object { $_.detalhes -like '*/api/admin/eventos*' }).Count -gt 0)
Chamar DELETE "/admin/usuarios/$idComum`?token=$tokenGeral" $null | Out-Null
Checar 'acoes de administracao espelhadas no log' (@((Eventos $emailComum).itens | Where-Object { $_.categoria -eq 'administracao' }).Count -ge 2)

# ---------------------------------------------------------------------------
Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
