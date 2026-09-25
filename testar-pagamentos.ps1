<#
    LanePets - teste de pagamentos (item 8 do roadmap).

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-pagamentos.ps1

    O QUE ELE PROVA
        - agendamento, pedido e contratacao de seguro geram UM pagamento cada;
        - aprovar espelha "Pago" no agendamento; transicoes invalidas sao recusadas;
        - origem cancelada: Pendente vira Cancelado sozinho; Aprovado fica "reembolso pendente";
        - Reembolsado e Cancelado sao finais;
        - o "Pago / A pagar" do painel de Agendamentos continua valendo;
        - o cliente ve os proprios pagamentos (sem dado interno);
        - sem o modulo pagamentos -> 403.

    Cria um cliente e um funcionario de teste (o funcionario e removido no fim).
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

function Pag($origemId) { @((Chamar GET "/admin/pagamentos?token=$tokenGeral&busca=$origemId").corpo.data.itens | Where-Object { $_.origemId -eq $origemId })[0] }
function Status($idPag, $status, $obs = '') { Chamar POST "/admin/pagamentos/$idPag/status" @{ token = $tokenGeral; status = $status; observacao = $obs } }
function Agendamento($id) { @((Chamar GET "/admin/estado?token=$tokenGeral").corpo.data.agendamentos | Where-Object { $_.id -eq $id })[0] }
function AtualizarNoPainel($id, $mudancas) {
    $ag = Agendamento $id
    foreach ($k in $mudancas.Keys) { $ag | Add-Member -NotePropertyName $k -NotePropertyValue $mudancas[$k] -Force }
    Chamar POST '/admin/sync/agendamentos' @{ token = $tokenGeral; criados = @(); atualizados = @($ag); removidos = @() }
}

Titulo 'Servidor no ar'
try { Checar 'API respondendo' ((Chamar GET '/health').status -eq 200) }
catch { Write-Host "  [FALHA] Nao consegui falar com $base. Rode 'dotnet run' antes." -ForegroundColor Red; exit 1 }
$login = Chamar POST '/login' @{ email = 'admin@gmail.com'; senha = '123456' }
if ($login.status -ne 200) { Write-Host "  [FALHA] Login do admin falhou: $($login.corpo.error)" -ForegroundColor Red; exit 1 }
$tokenGeral = $login.corpo.data.token
Checar 'GET /admin/pagamentos -> 200' ((Chamar GET "/admin/pagamentos?token=$tokenGeral").status -eq 200)

# ---------------------------------------------------------------------------
Titulo 'Preparacao'
$email = "pag.$([Guid]::NewGuid().ToString('N').Substring(0, 8))@lanepets.test"
$cad = Chamar POST '/cliente/cadastro' @{ nome = 'Teste Pagamento'; email = $email; senha = 'Senha1234'; telefone = '11999990000'; pet = 'Rex'; tipo = 'Cachorro' }
$tokenCli = $cad.corpo.data.token
$petId = @((Chamar GET '/cliente/conta' $null $tokenCli).corpo.data.pets)[0].id
$catalogo = (Chamar GET '/cliente/catalogo' $null $tokenCli).corpo.data
$uni = @($catalogo.unidades)[0]
$servico = @($catalogo.servicos | Where-Object { @($uni.servicos).Count -eq 0 -or @($uni.servicos) -contains $_.id })[0]
$dia = (Get-Date).AddDays(60 + (Get-Random -Maximum 300)).ToString('yyyy-MM-dd')
function NovoAgendamento($hora) {
    (Chamar POST '/cliente/agendamentos' @{ petId = $petId; servicoId = $servico.id; unidade = $uni.nome; data = $dia; horario = $hora; transporte = 'Cliente leva'; formaPagamento = 'Pix' } $tokenCli).corpo.data
}

# ---------------------------------------------------------------------------
Titulo '1. Agendamento gera pagamento'
$a1 = NovoAgendamento '09:00'
$p1 = Pag $a1.id
Checar 'pagamento criado, Pendente, com o valor do agendamento' ($p1 -and $p1.status -eq 'Pendente' -and [decimal]$p1.valor -eq [decimal]$a1.total -and $p1.origem -eq 'agendamento') "($($p1.status) $($p1.valor))"
$r = Status $p1.id 'Aprovado' 'Pix confirmado'
Checar 'aprovar -> 200' ($r.status -eq 200) "($($r.corpo.error))"
Checar 'agendamento passa a mostrar "Pago" no painel' ((Agendamento $a1.id).pagamentoStatus -eq 'Pago')
Checar 'mesmo status de novo -> 400' ((Status $p1.id 'Aprovado').status -eq 400)
Checar 'Aprovado -> Recusado nao e permitido -> 400' ((Status $p1.id 'Recusado').status -eq 400)
Checar 'status desconhecido -> 400' ((Status $p1.id 'Voando').status -eq 400)

# ---------------------------------------------------------------------------
Titulo '2. Origem cancelada depois de pago'
Chamar POST "/cliente/agendamentos/$($a1.id)/cancelar" $null $tokenCli | Out-Null
$p1 = Pag $a1.id
Checar 'continua Aprovado, com reembolso pendente' ($p1.status -eq 'Aprovado' -and $p1.reembolsoPendente -eq $true)
Checar 'nao da para "desfazer" para aprovar de novo sem decidir o reembolso' ((Status $p1.id 'Aprovado').status -eq 400)
$resumo = (Chamar GET "/admin/pagamentos?token=$tokenGeral&reembolso=true").corpo.data
Checar 'filtro "so reembolso pendente" traz o pagamento' (@($resumo.itens | Where-Object { $_.id -eq $p1.id }).Count -eq 1 -and $resumo.resumo.reembolsosPendentes -ge 1)
$r = Status $p1.id 'Reembolsado' 'Estorno feito no Pix'
Checar 'reembolsar -> 200 e limpa o alerta' ($r.status -eq 200 -and (Pag $a1.id).reembolsoPendente -eq $false)
Checar 'Reembolsado e final -> 400' ((Status $p1.id 'Pendente').status -eq 400)

# ---------------------------------------------------------------------------
Titulo '3. Origem cancelada antes de pagar'
$a2 = NovoAgendamento '13:00'
Chamar POST "/cliente/agendamentos/$($a2.id)/cancelar" $null $tokenCli | Out-Null
Checar 'pagamento Pendente virou Cancelado sozinho' ((Pag $a2.id).status -eq 'Cancelado')
Checar 'Cancelado e final -> 400' ((Status (Pag $a2.id).id 'Aprovado').status -eq 400)

# ---------------------------------------------------------------------------
Titulo '4. "Pago / A pagar" do painel de Agendamentos'
$a3 = NovoAgendamento '14:00'
$r = AtualizarNoPainel $a3.id @{ pagamentoStatus = 'Pago' }
Checar 'marcar Pago no painel -> 200' ($r.status -eq 200) "($($r.corpo.error))"
Checar 'pagamento vira Aprovado' ((Pag $a3.id).status -eq 'Aprovado')
AtualizarNoPainel $a3.id @{ pagamentoStatus = 'A pagar' } | Out-Null
Checar 'voltar para "A pagar" -> pagamento Pendente' ((Pag $a3.id).status -eq 'Pendente')
AtualizarNoPainel $a3.id @{ total = 123.45 } | Out-Null
Checar 'valor acompanha o agendamento enquanto Pendente' ([decimal](Pag $a3.id).valor -eq [decimal]123.45)

# ---------------------------------------------------------------------------
Titulo '5. Pedido da loja'
$produto = @($catalogo.produtos | Where-Object { -not $_.controlaEstoque -or $_.estoque -gt 0 })[0]
if ($produto) {
    $ped = (Chamar POST '/cliente/pedidos' @{ produtoId = $produto.id; quantidade = 1; formaPagamento = 'Pix'; unidade = $uni.id } $tokenCli).corpo.data
    $pp = Pag $ped.id
    Checar 'pedido gerou pagamento Pendente' ($pp -and $pp.status -eq 'Pendente' -and $pp.origem -eq 'pedido')
    Checar 'recusar -> 200' ((Status $pp.id 'Recusado' 'Pix nao caiu').status -eq 200)
    Chamar POST "/admin/pedidos/$($ped.id)/status" @{ token = $tokenGeral; status = 'Cancelado' } | Out-Null
    Checar 'pedido cancelado -> pagamento Recusado vira Cancelado' ((Pag $ped.id).status -eq 'Cancelado')
} else { Write-Host '  [PULADO] nenhum produto disponivel.' -ForegroundColor Yellow }

# ---------------------------------------------------------------------------
Titulo '6. Seguro Pet'
$plano = @((Chamar GET '/public/seguros').corpo.data)[0]
if ($plano) {
    $seg = Chamar POST '/cliente/seguros' @{ planoId = $plano.id; petId = $petId; metodoPagamento = 'PIX' } $tokenCli
    Checar 'contratacao -> 200' ($seg.status -eq 200) "($($seg.corpo.error))"
    $ps = Pag $seg.corpo.data.id
    Checar 'contratacao gerou pagamento Pendente com valor' ($ps -and $ps.status -eq 'Pendente' -and [decimal]$ps.valor -gt 0 -and $ps.origem -eq 'seguro')
    Chamar POST "/cliente/seguros/$($seg.corpo.data.id)/cancelar" $null $tokenCli | Out-Null
    Checar 'seguro cancelado -> pagamento Cancelado' ((Pag $seg.corpo.data.id).status -eq 'Cancelado')
} else { Write-Host '  [PULADO] nenhum plano de seguro ativo.' -ForegroundColor Yellow }

# ---------------------------------------------------------------------------
Titulo '7. O que o cliente ve'
$pags = @((Chamar GET '/cliente/conta' $null $tokenCli).corpo.data.pagamentos)
Checar 'conta traz os pagamentos do cliente' ($pags.Count -ge 3)
Checar 'status novos na conta (Reembolsado, Cancelado)' (@($pags | Where-Object { $_.status -eq 'Reembolsado' }).Count -eq 1 -and @($pags | Where-Object { $_.status -eq 'Cancelado' }).Count -ge 1)
Checar 'nao expoe quem alterou nem observacao interna' ($null -eq $pags[0].PSObject.Properties['atualizadoPor'] -and $null -eq $pags[0].PSObject.Properties['observacao'])

# ---------------------------------------------------------------------------
Titulo '8. Permissao'
Checar 'sem sessao -> 401' ((Chamar GET '/admin/pagamentos?token=invalido').status -eq 401)
$sufixo = Get-Random -Maximum 99999
$f = Chamar POST '/admin/usuarios' @{ token = $tokenGeral; nome = 'Funcionario Pag'; email = "func.pag.$sufixo@lanepets.test"; senha = 'Senha1234'; confirmarSenha = 'Senha1234'; perfil = 'Funcionario'; unidade = $uni.id }
$tokenFunc = (Chamar POST '/login' @{ email = "func.pag.$sufixo@lanepets.test"; senha = 'Senha1234' }).corpo.data.token
Checar 'funcionario sem o modulo pagamentos -> 403' ((Chamar GET "/admin/pagamentos?token=$tokenFunc").status -eq 403)
Chamar DELETE "/admin/usuarios/$($f.corpo.data.id)`?token=$tokenGeral" $null | Out-Null

Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
