<#
    LanePets - teste do dashboard administrativo (item 9 do roadmap).

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-dashboard.ps1

    O QUE ELE PROVA
        - GET /api/admin/resumo traz os 8 indicadores (clientes, pets, agenda de hoje,
          agendamentos do periodo, faturamento, estoque baixo, pagamentos pendentes,
          servicos mais realizados) e eles batem com os totais;
        - um agendamento novo sobe periodo, faturamento, pendentes e o servico;
        - filtro de unidade; aprovar tira dos pendentes; cancelar tira do periodo;
        - pagamentos vem da entidade Pagamento (reembolso pendente aparece);
        - sem o modulo pagamentos: numeros de dinheiro nulos/zerados e restrito = true;
        - sem sessao 401, sem o modulo dashboard 403.

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

function Resumo($de, $ate, $unidade = 'todas', $tk = $tokenGeral) { Chamar GET "/admin/resumo?token=$tk&de=$de&ate=$ate&unidade=$unidade" }
function Pag($origemId) { @((Chamar GET "/admin/pagamentos?token=$tokenGeral&busca=$origemId").corpo.data.itens | Where-Object { $_.origemId -eq $origemId })[0] }

Titulo 'Servidor no ar'
try { Checar 'API respondendo' ((Chamar GET '/health').status -eq 200) }
catch { Write-Host "  [FALHA] Nao consegui falar com $base. Rode 'dotnet run' antes." -ForegroundColor Red; exit 1 }
$login = Chamar POST '/login' @{ email = 'admin@gmail.com'; senha = '123456' }
if ($login.status -ne 200) { Write-Host "  [FALHA] Login do admin falhou: $($login.corpo.error)" -ForegroundColor Red; exit 1 }
$tokenGeral = $login.corpo.data.token

# ---------------------------------------------------------------------------
Titulo '1. Os 8 indicadores existem'
$hoje = (Get-Date).ToString('yyyy-MM-dd')
$r = Resumo $hoje $hoje
Checar 'GET /admin/resumo -> 200' ($r.status -eq 200) "($($r.corpo.error))"
$ind = $r.corpo.data.indicadores
if ($null -eq $ind) { Write-Host "  [FALHA] resposta sem 'indicadores' - o servidor esta rodando a build antiga? (Ctrl+C, dotnet build, dotnet run)" -ForegroundColor Red; exit 1 }
foreach ($campo in 'clientes','pets','agendamentosHoje','agendamentosPeriodo','faturamento','estoqueBaixo','pagamentosPendentes','servicosMaisRealizados') {
    Checar "indicador '$campo' presente" ($null -ne $ind.PSObject.Properties[$campo])
}
Checar 'restrito = false para o Administrador Geral' ($r.corpo.data.restrito -eq $false)
Checar 'clientes e pets batem com os totais' ($ind.clientes -eq $r.corpo.data.totais.clientes -and $ind.pets -eq $r.corpo.data.totais.pets)
$h = $ind.agendaHoje
Checar 'agenda de hoje = soma dos status (sem cancelados)' ($ind.agendamentosHoje -eq ($h.solicitados + $h.confirmados + $h.emAndamento + $h.concluidos))
Checar 'estoque baixo bate com a lista de estoque baixo' ($ind.estoqueBaixo -eq $r.corpo.data.estoqueBaixoTotal -and $ind.semEstoque -le $ind.estoqueBaixo)

# ---------------------------------------------------------------------------
Titulo '2. Agendamento novo mexe nos indicadores certos'
$email = "dash.$([Guid]::NewGuid().ToString('N').Substring(0, 8))@lanepets.test"
$cad = Chamar POST '/cliente/cadastro' @{ nome = 'Teste Dashboard'; email = $email; senha = 'Senha1234'; telefone = '11999990000'; pet = 'Bidu'; tipo = 'Cachorro' }
$tokenCli = $cad.corpo.data.token
$petId = @((Chamar GET '/cliente/conta' $null $tokenCli).corpo.data.pets)[0].id
$catalogo = (Chamar GET '/cliente/catalogo' $null $tokenCli).corpo.data
$uni = @($catalogo.unidades)[0]
$servico = @($catalogo.servicos | Where-Object { @($uni.servicos).Count -eq 0 -or @($uni.servicos) -contains $_.id })[0]
$dia = (Get-Date).AddDays(400 + (Get-Random -Maximum 200)).ToString('yyyy-MM-dd')

$antes = (Resumo $dia $dia $uni.id).corpo.data.indicadores
$antesGeral = (Resumo $dia $dia).corpo.data.indicadores
$ag = (Chamar POST '/cliente/agendamentos' @{ petId = $petId; servicoId = $servico.id; unidade = $uni.nome; data = $dia; horario = '10:00'; transporte = 'Cliente leva'; formaPagamento = 'Pix' } $tokenCli).corpo.data
Checar 'agendamento criado' ($null -ne $ag -and $ag.id) "($($ag | ConvertTo-Json -Compress))"
$depois = (Resumo $dia $dia $uni.id).corpo.data.indicadores

Checar 'agendamentos no periodo +1' ($depois.agendamentosPeriodo -eq $antes.agendamentosPeriodo + 1) "($($antes.agendamentosPeriodo) -> $($depois.agendamentosPeriodo))"
Checar 'faturamento do periodo sobe o valor do agendamento' ([decimal]$depois.faturamento -eq [decimal]$antes.faturamento + [decimal]$ag.total) "($($antes.faturamento) -> $($depois.faturamento), total $($ag.total))"
Checar 'pagamentos pendentes +1 (lidos da entidade Pagamento)' ($depois.pagamentosPendentes -eq $antes.pagamentosPendentes + 1) "($($antes.pagamentosPendentes) -> $($depois.pagamentosPendentes))"
Checar 'valor pendente sobe o valor do agendamento' ([decimal]$depois.valorPendente -eq [decimal]$antes.valorPendente + [decimal]$ag.total)
$nomeServico = $servico.nome
Checar "servico '$nomeServico' aparece entre os mais realizados" (@($depois.servicosMaisRealizados | Where-Object { $_.servico -eq $nomeServico -and $_.quantidade -ge 1 }).Count -eq 1)

$outra = @($catalogo.unidades | Where-Object { $_.id -ne $uni.id })[0]
$filtroOutra = if ($outra) { $outra.id } else { 'sem-unidade' }
$naOutra = (Resumo $dia $dia $filtroOutra).corpo.data.indicadores
$depoisGeral = (Resumo $dia $dia).corpo.data.indicadores
Checar "filtro de unidade ($filtroOutra) nao conta o agendamento" ($naOutra.agendamentosPeriodo -eq $depoisGeral.agendamentosPeriodo - $depois.agendamentosPeriodo)
Checar "'todas as unidades' tambem conta +1" ($depoisGeral.agendamentosPeriodo -eq $antesGeral.agendamentosPeriodo + 1)

# ---------------------------------------------------------------------------
Titulo '3. Pagar e cancelar'
$p = Pag $ag.id
Chamar POST "/admin/pagamentos/$($p.id)/status" @{ token = $tokenGeral; status = 'Aprovado'; observacao = 'teste dashboard' } | Out-Null
$aprovado = (Resumo $dia $dia $uni.id).corpo.data.indicadores
Checar 'aprovar o pagamento tira dos pendentes' ($aprovado.pagamentosPendentes -eq $antes.pagamentosPendentes)
Chamar POST "/cliente/agendamentos/$($ag.id)/cancelar" $null $tokenCli | Out-Null
$cancelado = (Resumo $dia $dia $uni.id).corpo.data.indicadores
Checar 'cancelado sai dos agendamentos do periodo' ($cancelado.agendamentosPeriodo -eq $antes.agendamentosPeriodo)
Checar 'pago e cancelado vira reembolso pendente no dashboard' ($cancelado.reembolsosPendentes -ge 1)
Chamar POST "/admin/pagamentos/$($p.id)/status" @{ token = $tokenGeral; status = 'Reembolsado'; observacao = 'teste dashboard' } | Out-Null

# ---------------------------------------------------------------------------
Titulo '4. Permissao'
Checar 'sem sessao -> 401' ((Chamar GET '/admin/resumo?token=invalido').status -eq 401)
$sufixo = Get-Random -Maximum 99999
$f = Chamar POST '/admin/usuarios' @{ token = $tokenGeral; nome = 'Funcionario Dash'; email = "func.dash.$sufixo@lanepets.test"; senha = 'Senha1234'; confirmarSenha = 'Senha1234'; perfil = 'Funcionario'; unidade = $uni.id }
$idFunc = $f.corpo.data.id
$tokenFunc = (Chamar POST '/login' @{ email = "func.dash.$sufixo@lanepets.test"; senha = 'Senha1234' }).corpo.data.token
Checar 'funcionario sem o modulo dashboard -> 403' ((Resumo $hoje $hoje 'todas' $tokenFunc).status -eq 403)
Chamar PUT "/admin/usuarios/$idFunc/permissoes" @{ token = $tokenGeral; acessoTotal = $false; modulos = @(@{ chave = 'dashboard'; visualizar = $true; criar = $false; editar = $false; excluir = $false }) } | Out-Null
$rf = Resumo $dia $dia 'todas' $tokenFunc
Checar 'funcionario com dashboard -> 200' ($rf.status -eq 200) "($($rf.corpo.error))"
$fi = $rf.corpo.data
Checar 'resposta marcada como restrita' ($fi.restrito -eq $true)
Checar 'faturamento e pagamentos pendentes vem nulos (nao zero)' ($null -eq $fi.indicadores.faturamento -and $null -eq $fi.indicadores.pagamentosPendentes -and $null -eq $fi.indicadores.valorPendente)
Checar 'nenhum valor em dinheiro no bloco financeiro' ([decimal]$fi.financeiro.receita -eq 0 -and [decimal]$fi.financeiro.despesas -eq 0 -and @($fi.porUnidade).Count -eq 0 -and @($fi.mensal).Count -eq 0)
Checar 'contagens operacionais continuam (agenda, estoque)' ($null -ne $fi.indicadores.agendamentosHoje -and $null -ne $fi.indicadores.estoqueBaixo)
Chamar DELETE "/admin/usuarios/$idFunc`?token=$tokenGeral" $null | Out-Null

# ---------------------------------------------------------------------------
Titulo '5. Relatorio Financeiro (item 11.2: entidade Pagamento + dinheiro restrito)'
$rel = Chamar GET "/admin/relatorio?token=$tokenGeral&de=$dia&ate=$dia&unidade=todas"
Checar 'GET /admin/relatorio -> 200' ($rel.status -eq 200) "($($rel.corpo.error))"
$pr = $rel.corpo.data.pagamentosResumo
Checar 'resumo de pagamentos vem da entidade (tem recusados/reembolsados)' ($null -ne $pr -and $null -ne $pr.recusados -and $null -ne $pr.reembolsados)
Checar 'agendamento aprovado e cancelado da secao 2 conta como pago e reembolso pendente' ($pr.pagos -ge 1 -and $pr.reembolsosPendentes -ge 1) "(pagos $($pr.pagos), reembolsos $($pr.reembolsosPendentes))"
Checar 'total recebido inclui o valor do agendamento aprovado' ([decimal]$pr.totalRecebido -ge [decimal]$ag.total) "($($pr.totalRecebido) / $($ag.total))"
$somaFormas = [decimal](@($rel.corpo.data.formasPagamento) | Measure-Object -Property valor -Sum).Sum
Checar 'formas de pagamento nao passam do total recebido' ($somaFormas -le [decimal]$pr.totalRecebido) "($somaFormas / $($pr.totalRecebido))"
Checar 'restrito = false para o Administrador Geral' ($rel.corpo.data.restrito -eq $false -and $null -ne $rel.corpo.data.cards.entradas)

$emailRel = "rel.dash.$sufixo@lanepets.test"
$cr = Chamar POST '/admin/usuarios' @{ token = $tokenGeral; nome = 'Relatorio Sem Dinheiro'; email = $emailRel; senha = 'Senha1234'; confirmarSenha = 'Senha1234'; ativo = $true }
$idRel = $cr.corpo.data.id
Chamar PUT "/admin/usuarios/$idRel/permissoes" @{ token = $tokenGeral; acessoTotal = $false; modulos = @(@{ chave = 'relatorios'; visualizar = $true; criar = $false; editar = $false; excluir = $false }) } | Out-Null
$tokenRel = (Chamar POST '/login' @{ email = $emailRel; senha = 'Senha1234' }).corpo.data.token
$rr = Chamar GET "/admin/relatorio?token=$tokenRel&de=$dia&ate=$dia&unidade=todas"
Checar 'so relatorios (sem pagamentos) -> 200' ($rr.status -eq 200) "($($rr.corpo.error))"
$d = $rr.corpo.data
Checar 'relatorio marcado como restrito' ($d.restrito -eq $true)
Checar 'cards de dinheiro vem nulos (nao zero)' ($null -eq $d.cards.entradas -and $null -eq $d.cards.saidas -and $null -eq $d.cards.saldo -and $null -eq $d.cards.ticketMedio)
Checar 'nenhuma lista de dinheiro (categorias, formas, serie, top produtos)' (@($d.categorias).Count -eq 0 -and @($d.formasPagamento).Count -eq 0 -and @($d.serie).Count -eq 0 -and @($d.topProdutos).Count -eq 0)
Checar 'sem resumo de pagamentos e sem valores de pedido' ($null -eq $d.pagamentosResumo -and $null -eq $d.pedidosResumo.valorRecebido -and $null -eq $d.pedidosResumo.valorTotalPedidos)
Checar 'por unidade sem dinheiro' (@($d.porUnidade | Where-Object { $null -ne $_.receita -or $null -ne $_.saldo }).Count -eq 0)
Checar 'contagens continuam (pedidos, total de pagamentos)' ($null -ne $d.pedidosResumo.total -and $null -ne $d.cards.totalPagamentos)
Chamar DELETE "/admin/usuarios/$idRel`?token=$tokenGeral" $null | Out-Null

Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
