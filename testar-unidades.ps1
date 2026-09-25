<#
    LanePets - teste do gerenciamento de unidades (item 5 do roadmap).

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-unidades.ps1

    O QUE ELE PROVA
        - cadastro com validacao (nome repetido, capacidade 1..20, servico inexistente);
        - edicao, desativar/ativar e o reflexo no site publico;
        - capacidade por horario no site do cliente (horarios e agendar) e no painel (sync);
        - servico nao oferecido na unidade e recusado;
        - pedido exige unidade de retirada quando ha mais de uma ativa;
        - Funcionario nao gerencia unidades (403), mas le a lista leve.

    Deixa para tras: uma unidade de teste DESATIVADA (nao existe exclusao,
    por decisao do projeto), uma conta de cliente de teste e um pedido dela.
    Os agendamentos de teste sao cancelados no fim.
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

# ---------------------------------------------------------------------------
Titulo 'Servidor no ar'
try { Checar 'API respondendo' ((Chamar GET '/health').status -eq 200) }
catch { Write-Host "  [FALHA] Nao consegui falar com $base. Rode 'dotnet run' antes." -ForegroundColor Red; exit 1 }

$login = Chamar POST '/login' @{ email = 'admin@gmail.com'; senha = '123456' }
if ($login.status -ne 200) { Write-Host "  [FALHA] Login do admin falhou: $($login.corpo.error)" -ForegroundColor Red; exit 1 }
$tokenGeral = $login.corpo.data.token

# ---------------------------------------------------------------------------
Titulo '1. Listagem'
$lista = Chamar GET "/admin/unidades?token=$tokenGeral"
Checar 'GET /admin/unidades -> 200' ($lista.status -eq 200) "($($lista.corpo.error))"
$servicos = @($lista.corpo.data.servicos)
Checar 'traz a lista de servicos para o formulario' ($servicos.Count -gt 0)
Checar 'cada unidade traz capacidade' (@($lista.corpo.data.unidades | Where-Object { $_.capacidade -ge 1 }).Count -eq @($lista.corpo.data.unidades).Count)
$servicoA = $servicos[0].id
$servicoB = if ($servicos.Count -gt 1) { $servicos[1].id } else { $null }
Checar 'GET /admin/unidades/lista -> 200' ((Chamar GET "/admin/unidades/lista?token=$tokenGeral").status -eq 200)

# ---------------------------------------------------------------------------
Titulo '2. Cadastro e validacao'
$sufixo = Get-Random -Maximum 99999
$nome = "Unidade Teste $sufixo"
$r = Chamar POST '/admin/unidades' @{ token = $tokenGeral; nome = 'Franco da Rocha'; capacidade = 1 }
Checar 'nome repetido -> 400' ($r.status -eq 400) "($($r.status) $($r.corpo.error))"
$r = Chamar POST '/admin/unidades' @{ token = $tokenGeral; nome = $nome; capacidade = 0 }
Checar 'capacidade 0 -> 400' ($r.status -eq 400)
$r = Chamar POST '/admin/unidades' @{ token = $tokenGeral; nome = $nome; capacidade = 21 }
Checar 'capacidade 21 -> 400' ($r.status -eq 400)
$r = Chamar POST '/admin/unidades' @{ token = $tokenGeral; nome = $nome; capacidade = 2; servicos = @('SERVICO-QUE-NAO-EXISTE') }
Checar 'servico inexistente -> 400' ($r.status -eq 400)
$r = Chamar POST '/admin/unidades' @{ token = $tokenGeral; nome = 'AB'; capacidade = 1 }
Checar 'nome curto -> 400' ($r.status -eq 400)

$r = Chamar POST '/admin/unidades' @{ token = $tokenGeral; nome = $nome; endereco = 'Rua do Teste, 100'; telefone = '11999990000'; horarioFuncionamento = 'Seg a Sex 9h-18h'; capacidade = 2; servicos = @($servicoA) }
Checar 'cadastro valido -> 200' ($r.status -eq 200) "($($r.corpo.error))"
$idUni = $r.corpo.data.id
Checar 'id gerado a partir do nome' ($idUni -like 'unidade-teste*')

$r = Chamar PUT "/admin/unidades/$idUni" @{ token = $tokenGeral; nome = "$nome Editada"; endereco = 'Rua do Teste, 200'; telefone = '11999990000'; horarioFuncionamento = 'Seg a Sab'; capacidade = 2; servicos = @($servicoA) }
Checar 'edicao -> 200' ($r.status -eq 200) "($($r.corpo.error))"
$pub = (Chamar GET '/public/unidades').corpo.data
Checar 'unidade ativa aparece no site publico' (@($pub | Where-Object { $_.id -eq $idUni -and $_.nome -eq "$nome Editada" }).Count -eq 1)

# ---------------------------------------------------------------------------
Titulo '3. Capacidade no site do cliente'
$email = "unidade.$([Guid]::NewGuid().ToString('N').Substring(0, 8))@lanepets.test"
$cad = Chamar POST '/cliente/cadastro' @{ nome = 'Teste Unidade'; email = $email; senha = 'Senha1234'; telefone = '11999990000'; pet = 'Rex'; tipo = 'Cachorro' }
Checar 'cliente de teste criado' ($cad.status -eq 200) "($($cad.corpo.error))"
$tokenCli = $cad.corpo.data.token
$petId = @((Chamar GET '/cliente/conta' $null $tokenCli).corpo.data.pets)[0].id
$data = (Get-Date).AddDays(45).ToString('yyyy-MM-dd')
$uniNome = "$nome Editada"
$criadosAg = @()

$h = Chamar GET "/cliente/horarios?unidade=$([uri]::EscapeDataString($uniNome))&data=$data" $null $tokenCli
Checar 'horarios da unidade nova -> 10:00 livre' ($h.status -eq 200 -and @($h.corpo.data) -contains '10:00') "($($h.corpo.error))"

foreach ($n in 1..2) {
    $a = Chamar POST '/cliente/agendamentos' @{ petId = $petId; servicoId = $servicoA; unidade = $uniNome; data = $data; horario = '10:00'; transporte = 'Cliente leva'; formaPagamento = 'Pix' } $tokenCli
    Checar "agendamento $n de 2 no mesmo horario -> 200" ($a.status -eq 200) "($($a.corpo.error))"
    if ($a.status -eq 200) { $criadosAg += $a.corpo.data.id }
}
$a = Chamar POST '/cliente/agendamentos' @{ petId = $petId; servicoId = $servicoA; unidade = $uniNome; data = $data; horario = '10:00' } $tokenCli
Checar 'terceiro no horario (capacidade 2) -> 400' ($a.status -eq 400) "($($a.status) $($a.corpo.error))"
$h = Chamar GET "/cliente/horarios?unidade=$idUni&data=$data" $null $tokenCli
Checar 'horario lotado some da lista (busca pelo id tambem funciona)' ($h.status -eq 200 -and -not (@($h.corpo.data) -contains '10:00'))

if ($servicoB) {
    $a = Chamar POST '/cliente/agendamentos' @{ petId = $petId; servicoId = $servicoB; unidade = $uniNome; data = $data; horario = '11:00' } $tokenCli
    Checar 'servico nao oferecido na unidade -> 400' ($a.status -eq 400) "($($a.status) $($a.corpo.error))"
}

# ---------------------------------------------------------------------------
Titulo '4. Capacidade no painel'
$r = Chamar POST '/admin/sync/agendamentos' @{ token = $tokenGeral; criados = @(@{ id = "AGD-CAP-$sufixo"; unidade = $idUni; dataHora = "$($data)T10:00"; status = 'Pendente'; pet = 'Rex'; dono = 'Teste Unidade'; telefone = '11999990000' }); atualizados = @(); removidos = @() }
Checar 'painel tambem respeita a capacidade -> 400' ($r.status -eq 400 -and "$($r.corpo.error)" -like '*lotado*') "($($r.status) $($r.corpo.error))"

# ---------------------------------------------------------------------------
Titulo '5. Pedido com unidade de retirada'
$ativas = @((Chamar GET '/public/unidades').corpo.data)
$produto = @((Chamar GET '/cliente/catalogo' $null $tokenCli).corpo.data.produtos | Where-Object { -not $_.controlaEstoque -or $_.estoque -gt 0 })[0]
if (-not $produto) {
    Write-Host '  [PULADO] nenhum produto disponivel para pedido.' -ForegroundColor Yellow
} else {
    if ($ativas.Count -gt 1) {
        $p = Chamar POST '/cliente/pedidos' @{ produtoId = $produto.id; quantidade = 1; formaPagamento = 'Pix' } $tokenCli
        Checar 'sem unidade com varias ativas -> 400' ($p.status -eq 400) "($($p.status) $($p.corpo.error))"
    }
    $p = Chamar POST '/cliente/pedidos' @{ produtoId = $produto.id; quantidade = 1; formaPagamento = 'Pix'; unidade = $idUni } $tokenCli
    Checar 'com unidade de retirada -> 200' ($p.status -eq 200) "($($p.corpo.error))"
    $pedidos = @((Chamar GET '/cliente/conta' $null $tokenCli).corpo.data.pedidos)
    Checar 'pedido gravou a unidade de retirada' (@($pedidos | Where-Object { $_.unidade -eq $idUni }).Count -eq 1)
}

# ---------------------------------------------------------------------------
Titulo '6. Funcionario'
$emailFunc = "func.$([Guid]::NewGuid().ToString('N').Substring(0, 8))@lanepets.test"
$f = Chamar POST '/admin/usuarios' @{ token = $tokenGeral; nome = 'Funcionario Teste'; email = $emailFunc; senha = 'Senha1234'; confirmarSenha = 'Senha1234'; perfil = 'Funcionario'; unidade = 'franco' }
Checar 'funcionario de teste criado' ($f.status -eq 200) "($($f.corpo.error))"
$idFunc = $f.corpo.data.id
$tokenFunc = (Chamar POST '/login' @{ email = $emailFunc; senha = 'Senha1234' }).corpo.data.token
Checar 'funcionario em /admin/unidades -> 403' ((Chamar GET "/admin/unidades?token=$tokenFunc").status -eq 403)
Checar 'funcionario criando unidade -> 403' ((Chamar POST '/admin/unidades' @{ token = $tokenFunc; nome = "Tentativa $sufixo"; capacidade = 1 }).status -eq 403)
$l = Chamar GET "/admin/unidades/lista?token=$tokenFunc"
Checar 'funcionario le a lista leve (abas e filtros)' ($l.status -eq 200)
Checar 'lista leve informa a unidade do funcionario' ($l.corpo.data.minhaUnidade -eq 'franco')
Chamar DELETE "/admin/usuarios/$idFunc`?token=$tokenGeral" $null | Out-Null

# ---------------------------------------------------------------------------
Titulo '7. Desativar'
foreach ($id in $criadosAg) { Chamar POST "/cliente/agendamentos/$id/cancelar" $null $tokenCli | Out-Null }
$r = Chamar POST "/admin/unidades/$idUni/ativa" @{ token = $tokenGeral; ativa = $false }
Checar 'desativar -> 200' ($r.status -eq 200) "($($r.corpo.error))"
$pub = (Chamar GET '/public/unidades').corpo.data
Checar 'unidade desativada some do site publico' (@($pub | Where-Object { $_.id -eq $idUni }).Count -eq 0)
$h = Chamar GET "/cliente/horarios?unidade=$idUni&data=$data" $null $tokenCli
Checar 'unidade desativada nao aceita agendamento' ($h.status -eq 400)
$lista = (Chamar GET "/admin/unidades?token=$tokenGeral").corpo.data.unidades
Checar 'continua no cadastro, marcada como inativa (historico mantido)' (@($lista | Where-Object { $_.id -eq $idUni -and -not $_.ativa }).Count -eq 1)
Checar 'desativacao virou evento' ((Chamar GET "/admin/eventos?token=$tokenGeral&busca=$idUni").corpo.data.total -ge 2)

# ---------------------------------------------------------------------------
Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
