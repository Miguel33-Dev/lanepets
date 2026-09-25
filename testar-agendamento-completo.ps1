<#
    LanePets - teste do agendamento completo (item 4 do roadmap).

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-agendamento-completo.ps1

    O QUE ELE PROVA
        - status novos: Solicitado -> Confirmado -> Em andamento -> Concluido (+ Cancelado);
        - nome antigo enviado pelo painel (ex.: "Pronto") e gravado com o nome novo;
        - status desconhecido e recusado;
        - funcionario responsavel: so Funcionario ATIVO da MESMA unidade;
        - cliente ve so o PRIMEIRO nome do responsavel, nunca o id;
        - cliente so cancela Solicitado/Confirmado.

    Cria um cliente de teste e dois funcionarios de teste (removidos no fim).
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

function Agendamento($id) {
    @((Chamar GET "/admin/estado?token=$tokenGeral").corpo.data.agendamentos | Where-Object { $_.id -eq $id })[0]
}

# Manda o agendamento inteiro (o sync substitui os campos) com as alteracoes pedidas.
function AtualizarNoPainel($id, $mudancas) {
    $ag = Agendamento $id
    foreach ($k in $mudancas.Keys) { $ag | Add-Member -NotePropertyName $k -NotePropertyValue $mudancas[$k] -Force }
    Chamar POST '/admin/sync/agendamentos' @{ token = $tokenGeral; criados = @(); atualizados = @($ag); removidos = @() }
}

# ---------------------------------------------------------------------------
Titulo 'Servidor no ar'
try { Checar 'API respondendo' ((Chamar GET '/health').status -eq 200) }
catch { Write-Host "  [FALHA] Nao consegui falar com $base. Rode 'dotnet run' antes." -ForegroundColor Red; exit 1 }

$login = Chamar POST '/login' @{ email = 'admin@gmail.com'; senha = '123456' }
if ($login.status -ne 200) { Write-Host "  [FALHA] Login do admin falhou: $($login.corpo.error)" -ForegroundColor Red; exit 1 }
$tokenGeral = $login.corpo.data.token

# ---------------------------------------------------------------------------
Titulo 'Preparacao'
$email = "agcompleto.$([Guid]::NewGuid().ToString('N').Substring(0, 8))@lanepets.test"
$cad = Chamar POST '/cliente/cadastro' @{ nome = 'Teste Agenda'; email = $email; senha = 'Senha1234'; telefone = '11999990000'; pet = 'Rex'; tipo = 'Cachorro' }
Checar 'cliente de teste criado' ($cad.status -eq 200) "($($cad.corpo.error))"
$tokenCli = $cad.corpo.data.token
$petId = @((Chamar GET '/cliente/conta' $null $tokenCli).corpo.data.pets)[0].id
$catalogo = (Chamar GET '/cliente/catalogo' $null $tokenCli).corpo.data
$uni = @($catalogo.unidades)[0]
$outra = @($catalogo.unidades | Where-Object { $_.id -ne $uni.id })[0]
$servico = @($catalogo.servicos | Where-Object { @($uni.servicos).Count -eq 0 -or @($uni.servicos) -contains $_.id })[0]
Write-Host "  unidade: $($uni.nome) · servico: $($servico.nome)"

$sufixo = Get-Random -Maximum 99999
$fA = Chamar POST '/admin/usuarios' @{ token = $tokenGeral; nome = "Ana Teste$sufixo Souza"; email = "ana.$sufixo@lanepets.test"; senha = 'Senha1234'; confirmarSenha = 'Senha1234'; perfil = 'Funcionario'; unidade = $uni.id }
Checar 'funcionaria A criada na unidade' ($fA.status -eq 200) "($($fA.corpo.error))"
$idA = $fA.corpo.data.id
$idB = $null
if ($outra) {
    $fB = Chamar POST '/admin/usuarios' @{ token = $tokenGeral; nome = "Bruno Teste$sufixo"; email = "bruno.$sufixo@lanepets.test"; senha = 'Senha1234'; confirmarSenha = 'Senha1234'; perfil = 'Funcionario'; unidade = $outra.id }
    $idB = $fB.corpo.data.id
}
$lista = (Chamar GET "/admin/unidades/lista?token=$tokenGeral").corpo.data.unidades
Checar 'lista de unidades traz a funcionaria na unidade dela' (@(@($lista | Where-Object { $_.id -eq $uni.id })[0].funcionarios | Where-Object { $_.id -eq $idA }).Count -eq 1)

$dia = (Get-Date).AddDays(60 + (Get-Random -Maximum 300)).ToString('yyyy-MM-dd')
function NovoAgendamento($hora) {
    $r = Chamar POST '/cliente/agendamentos' @{ petId = $petId; servicoId = $servico.id; unidade = $uni.nome; data = $dia; horario = $hora; transporte = 'Cliente leva'; formaPagamento = 'Pix' } $tokenCli
    if ($r.status -ne 200) { Write-Host "  [aviso] agendamento $hora recusado: $($r.corpo.error)" -ForegroundColor Yellow }
    return $r
}

# ---------------------------------------------------------------------------
Titulo '1. Status novos'
$a1 = NovoAgendamento '09:00'
Checar 'agendamento do cliente nasce Solicitado' ($a1.status -eq 200 -and $a1.corpo.data.status -eq 'Solicitado') "($($a1.corpo.data.status))"
$id1 = $a1.corpo.data.id
Checar 'painel le Solicitado' ((Agendamento $id1).status -eq 'Solicitado')

$r = AtualizarNoPainel $id1 @{ status = 'Confirmado' }
Checar 'painel confirma -> 200' ($r.status -eq 200) "($($r.corpo.error))"
Checar 'status gravado Confirmado' ((Agendamento $id1).status -eq 'Confirmado')

$r = AtualizarNoPainel $id1 @{ status = 'Qualquer Coisa' }
Checar 'status desconhecido -> 400' ($r.status -eq 400) "($($r.status))"

# ---------------------------------------------------------------------------
Titulo '2. Funcionario responsavel'
$r = AtualizarNoPainel $id1 @{ responsavelId = $idA }
Checar 'responsavel da mesma unidade -> 200' ($r.status -eq 200) "($($r.corpo.error))"
$ag = Agendamento $id1
Checar 'painel recebe id e nome completo do responsavel' ($ag.responsavelId -eq $idA -and $ag.responsavelNome -like 'Ana Teste*Souza')

if ($idB) {
    $r = AtualizarNoPainel $id1 @{ responsavelId = $idB }
    Checar 'funcionario de OUTRA unidade -> 400' ($r.status -eq 400) "($($r.status) $($r.corpo.error))"
}
$r = AtualizarNoPainel $id1 @{ responsavelId = 'USUARIO-QUE-NAO-EXISTE' }
Checar 'responsavel inexistente -> 400' ($r.status -eq 400)
$idGeral = (Chamar GET "/admin/me?token=$tokenGeral").corpo.data.usuario.id
if ($idGeral) {
    $r = AtualizarNoPainel $id1 @{ responsavelId = $idGeral }
    Checar 'administrador (nao Funcionario) como responsavel -> 400' ($r.status -eq 400)
}
Checar 'responsavel continua o mesmo depois das recusas' ((Agendamento $id1).responsavelId -eq $idA)

$r = AtualizarNoPainel $id1 @{ status = 'Em andamento' }
Checar 'mudar so o status mantem o responsavel' ($r.status -eq 200 -and (Agendamento $id1).responsavelId -eq $idA)

# ---------------------------------------------------------------------------
Titulo '3. O que o cliente ve'
$conta = (Chamar GET '/cliente/conta' $null $tokenCli).corpo.data
$doCliente = @($conta.agendamentos | Where-Object { $_.id -eq $id1 })[0]
Checar 'cliente ve o status novo' ($doCliente.status -eq 'Em andamento')
Checar 'cliente ve so o primeiro nome do responsavel' ($doCliente.responsavelNome -eq 'Ana') "($($doCliente.responsavelNome))"
Checar 'id do responsavel NAO vai para o cliente' ($null -eq $doCliente.PSObject.Properties['responsavelId'])
$r = Chamar POST "/cliente/agendamentos/$id1/cancelar" $null $tokenCli
Checar 'cliente NAO cancela atendimento em andamento -> 400' ($r.status -eq 400)

$r = AtualizarNoPainel $id1 @{ status = 'Pronto' }
Checar 'nome antigo "Pronto" e aceito' ($r.status -eq 200) "($($r.corpo.error))"
Checar '... e gravado como Concluido' ((Agendamento $id1).status -eq 'Concluído')
$r = Chamar POST "/cliente/agendamentos/$id1/cancelar" $null $tokenCli
Checar 'cliente NAO cancela atendimento concluido -> 400' ($r.status -eq 400)

$a2 = NovoAgendamento '13:00'
$id2 = $a2.corpo.data.id
AtualizarNoPainel $id2 @{ status = 'Confirmado' } | Out-Null
$r = Chamar POST "/cliente/agendamentos/$id2/cancelar" $null $tokenCli
Checar 'cliente cancela agendamento Confirmado -> 200' ($r.status -eq 200) "($($r.corpo.error))"
Checar 'status Cancelado' ((Agendamento $id2).status -eq 'Cancelado')

# ---------------------------------------------------------------------------
Titulo '4. Funcionario desativado'
Chamar POST "/admin/usuarios/$idA/status" @{ token = $tokenGeral; ativo = $false } | Out-Null
$a3 = NovoAgendamento '14:00'
$id3 = $a3.corpo.data.id
$r = AtualizarNoPainel $id3 @{ responsavelId = $idA }
Checar 'funcionario desativado nao recebe atendimento -> 400' ($r.status -eq 400) "($($r.status))"
$r = AtualizarNoPainel $id1 @{ obs = 'Obs de teste' }
Checar 'agendamento antigo dele continua editavel' ($r.status -eq 200) "($($r.corpo.error))"
$lista = (Chamar GET "/admin/unidades/lista?token=$tokenGeral").corpo.data.unidades
Checar 'desativado sai da lista de responsaveis' (@(@($lista | Where-Object { $_.id -eq $uni.id })[0].funcionarios | Where-Object { $_.id -eq $idA }).Count -eq 0)

# ---------------------------------------------------------------------------
Titulo 'Limpeza'
foreach ($id in @($id3)) { if ($id) { Chamar POST "/cliente/agendamentos/$id/cancelar" $null $tokenCli | Out-Null } }
Chamar DELETE "/admin/usuarios/$idA`?token=$tokenGeral" $null | Out-Null
if ($idB) { Chamar DELETE "/admin/usuarios/$idB`?token=$tokenGeral" $null | Out-Null }
Write-Host '  funcionarios de teste removidos'

# ---------------------------------------------------------------------------
Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
