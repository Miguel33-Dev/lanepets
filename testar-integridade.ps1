<#
    LanePets - teste da integridade do banco (item 19 do roadmap).

    COMO USAR
        1. Em um terminal:   dotnet run      (a subida faz o backup e cria os indices)
        2. Em outro:         .\testar-integridade.ps1

    O QUE ELE PROVA
        - so o Administrador Geral consulta (401 sem sessao, 403 admin comum);
        - a varredura traz as verificacoes esperadas;
        - um agendamento com cliente inexistente e unidade desconhecida e apontado;
        - a subida criou backup e indices.

    Cria um agendamento "defeituoso" e um admin comum de teste (os dois removidos no fim).
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

function Verificar() { (Chamar GET "/admin/integridade?token=$tokenGeral").corpo.data }
function Achar($dados, $codigo) { @($dados.verificacoes | Where-Object { $_.codigo -eq $codigo })[0] }

Titulo 'Servidor no ar'
try { Checar 'API respondendo' ((Chamar GET '/health').status -eq 200) }
catch { Write-Host "  [FALHA] Nao consegui falar com $base. Rode 'dotnet run' antes." -ForegroundColor Red; exit 1 }
$login = Chamar POST '/login' @{ email = 'admin@gmail.com'; senha = '123456' }
if ($login.status -ne 200) { Write-Host "  [FALHA] Login do admin falhou: $($login.corpo.error)" -ForegroundColor Red; exit 1 }
$tokenGeral = $login.corpo.data.token

# ---------------------------------------------------------------------------
Titulo '1. Quem pode consultar'
Checar 'sem sessao -> 401' ((Chamar GET '/admin/integridade?token=invalido').status -eq 401)
$sufixo = Get-Random -Maximum 99999
$c = Chamar POST '/admin/usuarios' @{ token = $tokenGeral; nome = 'Admin Integridade'; email = "integ.$sufixo@lanepets.test"; senha = 'Senha1234'; confirmarSenha = 'Senha1234' }
$idComum = $c.corpo.data.id
Chamar PUT "/admin/usuarios/$idComum/permissoes" @{ token = $tokenGeral; acessoTotal = $true; modulos = @() } | Out-Null
$tokenComum = (Chamar POST '/login' @{ email = "integ.$sufixo@lanepets.test"; senha = 'Senha1234' }).corpo.data.token
Checar 'admin comum, mesmo com acesso total -> 403' ((Chamar GET "/admin/integridade?token=$tokenComum").status -eq 403)

# ---------------------------------------------------------------------------
Titulo '2. Varredura'
$r = Chamar GET "/admin/integridade?token=$tokenGeral"
Checar 'Administrador Geral -> 200' ($r.status -eq 200) "($($r.corpo.error))"
$d = $r.corpo.data
$esperadas = 'pet-sem-cliente','agendamento-sem-cliente','agendamento-unidade','conta-email-duplicado','cliente-telefone-duplicado','estoque-divergente','pagamento-sem-origem'
foreach ($cod in $esperadas) { Checar "verificacao '$cod' presente" ($null -ne (Achar $d $cod)) }
Checar 'cada verificacao tem nivel valido' (@($d.verificacoes | Where-Object { @('erro','aviso','info','ok') -notcontains $_.nivel }).Count -eq 0)
Checar 'verificacao sem problema vem como "ok" com total 0' (@($d.verificacoes | Where-Object { $_.nivel -eq 'ok' -and $_.total -ne 0 }).Count -eq 0)
$antesCli = (Achar $d 'agendamento-sem-cliente').total
$antesUni = (Achar $d 'agendamento-sem-unidade').total

# ---------------------------------------------------------------------------
Titulo '3. Problema de proposito'
$idAg = "AGD-INTEG-$sufixo"
$s = Chamar POST '/admin/sync/agendamentos' @{ token = $tokenGeral; criados = @(@{ id = $idAg; pet = 'Fantasma'; dono = 'Ninguem'; telefone = '11900000000'; dataHora = '2099-01-01T10:00'; unidade = "Unidade Fantasma $sufixo"; status = 'Solicitado'; clienteId = "CLI-FANTASMA-$sufixo" }); atualizados = @(); removidos = @() }
Checar 'agendamento defeituoso gravado' ($s.status -eq 200) "($($s.corpo.error))"
$d = Verificar
$vCli = Achar $d 'agendamento-sem-cliente'
$vUni = Achar $d 'agendamento-sem-unidade'
Checar 'cliente inexistente apontado (erro)' ($vCli.total -eq $antesCli + 1 -and $vCli.nivel -eq 'erro')
Checar 'o exemplo traz o id do agendamento' (@($vCli.exemplos | Where-Object { $_.id -eq $idAg }).Count -eq 1 -or $vCli.total -gt 20)
Checar 'unidade desconhecida (o painel grava vazia) apontada em "sem unidade"' ($vUni.total -eq $antesUni + 1)
Checar 'a varredura NAO corrigiu nada (so relata)' (@((Chamar GET "/admin/estado?token=$tokenGeral").corpo.data.agendamentos | Where-Object { $_.id -eq $idAg }).Count -eq 1)
Chamar POST '/admin/sync/agendamentos' @{ token = $tokenGeral; criados = @(); atualizados = @(); removidos = @($idAg) } | Out-Null
Checar 'depois de remover, some da varredura' ((Achar (Verificar) 'agendamento-sem-cliente').total -eq $antesCli)

# ---------------------------------------------------------------------------
Titulo '4. Backup e indices'
$b = $d.banco
Checar 'ha pelo menos um backup automatico' (@($b.backups).Count -ge 1) "(pasta: $($b.pastaBackups); erro: $($b.erroBackup))"
Checar 'no maximo 10 backups guardados' (@($b.backups).Count -le 10)
Checar 'indices verificados na subida' (@($b.indices).Count -ge 20)
Checar 'nenhum indice com erro' (@($b.indices | Where-Object { $_.situacao -like 'erro*' }).Count -eq 0) "($(@($b.indices | Where-Object { $_.situacao -like 'erro*' } | ForEach-Object { $_.nome }) -join ', '))"

Titulo 'Limpeza'
Chamar DELETE "/admin/usuarios/$idComum`?token=$tokenGeral" $null | Out-Null
Write-Host '  admin de teste removido'

Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
