<#
    LanePets - teste de produtos e estoque (itens 6 e 7 do roadmap).

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-produtos-estoque.ps1

    O QUE ELE PROVA
        - produto com descricao, foto (validada) e "visivel na loja";
        - estoque inicial, entrada, saida e ajuste viram movimentacao no livro;
        - saida maior que o saldo, motivo curto e tipo invalido sao recusados;
        - editar o produto NAO muda o saldo (saldo so muda pelo livro);
        - pedido do cliente baixa o estoque ("venda") e avisa estoque baixo no log;
        - cancelar pedido (cliente enquanto Pendente, painel em qualquer status) devolve o estoque;
        - pedido cancelado nao muda mais de status;
        - produto oculto some da loja e nao aceita pedido;
        - Funcionario nao lanca movimentacao (403);
        - dashboard lista o produto com estoque baixo.

    Cria um produto, um cliente e um funcionario de teste (produto e funcionario removidos no fim).
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

function Produto($id) { @((Chamar GET "/admin/estado?token=$tokenGeral").corpo.data.produtos | Where-Object { $_.id -eq $id })[0] }
function Movs($id) { @((Chamar GET "/admin/estoque/movimentacoes?token=$tokenGeral&produtoId=$id&limite=100").corpo.data) }
function Mov($tipo, $qtd, $motivo, $token = $tokenGeral) { Chamar POST "/admin/produtos/$idProd/movimentacao" @{ token = $token; tipo = $tipo; quantidade = $qtd; motivo = $motivo } }
function SyncProduto($item, $modo = 'atualizados') {
    $corpo = @{ token = $tokenGeral; criados = @(); atualizados = @(); removidos = @() }
    $corpo[$modo] = @($item)
    Chamar POST '/admin/sync/produtos' $corpo
}

# ---------------------------------------------------------------------------
Titulo 'Servidor no ar'
try { Checar 'API respondendo' ((Chamar GET '/health').status -eq 200) }
catch { Write-Host "  [FALHA] Nao consegui falar com $base. Rode 'dotnet run' antes." -ForegroundColor Red; exit 1 }
$login = Chamar POST '/login' @{ email = 'admin@gmail.com'; senha = '123456' }
if ($login.status -ne 200) { Write-Host "  [FALHA] Login do admin falhou: $($login.corpo.error)" -ForegroundColor Red; exit 1 }
$tokenGeral = $login.corpo.data.token

$sufixo = Get-Random -Maximum 99999
$idProd = "PRD-TESTE-$sufixo"
$png = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII='

# ---------------------------------------------------------------------------
Titulo '1. Cadastro do produto'
$prodBase = @{ id = $idProd; codigo = "TST$sufixo"; nome = "Racao Teste $sufixo"; categoria = 'Ração'; valorCompra = 10; valorVenda = 25; controlaEstoque = $true; estoque = 10; estoqueMinimo = 3 }
$r = SyncProduto (@{} + $prodBase + @{ fotoUrl = 'nao-e-imagem' }) 'criados'
Checar 'foto invalida -> 400' ($r.status -eq 400) "($($r.status))"
$r = SyncProduto (@{} + $prodBase + @{ descricao = ('x' * 1001) }) 'criados'
Checar 'descricao com mais de 1000 caracteres -> 400' ($r.status -eq 400)
$r = SyncProduto (@{} + $prodBase + @{ descricao = 'Racao de teste para caes adultos.'; fotoUrl = $png; visivelLoja = $true }) 'criados'
Checar 'cadastro valido -> 200' ($r.status -eq 200) "($($r.corpo.error))"
$p = Produto $idProd
Checar 'descricao, foto e visivel gravados' ($p.descricao -like 'Racao de teste*' -and $p.fotoUrl -like 'data:image/png*' -and $p.visivelLoja -eq $true)
Checar 'saldo inicial 10' ($p.estoque -eq 10)
Checar 'estoque inicial virou movimentacao de entrada' (@(Movs $idProd | Where-Object { $_.tipo -eq 'entrada' -and $_.quantidade -eq 10 }).Count -eq 1)

# ---------------------------------------------------------------------------
Titulo '2. Movimentacoes manuais'
$r = Mov 'saida' 2 'Avaria no transporte'
Checar 'saida de 2 -> 200, saldo 8' ($r.status -eq 200 -and $r.corpo.data.estoque -eq 8) "($($r.corpo.error))"
Checar 'saida maior que o saldo -> 400' ((Mov 'saida' 50 'Teste de saldo').status -eq 400)
Checar 'motivo curto -> 400' ((Mov 'entrada' 1 'x').status -eq 400)
Checar 'tipo invalido -> 400' ((Mov 'venda' 1 'Tentativa de venda manual').status -eq 400)
Checar 'ajuste para o mesmo saldo -> 400' ((Mov 'ajuste' 8 'Inventario igual').status -eq 400)
$r = Mov 'entrada' 4 'Compra do fornecedor NF 123'
Checar 'entrada de 4 -> saldo 12' ($r.status -eq 200 -and $r.corpo.data.estoque -eq 12)
$r = Mov 'ajuste' 9 'Inventario do mes'
Checar 'ajuste para 9 -> saldo 9' ($r.status -eq 200 -and $r.corpo.data.estoque -eq 9)
$m = @(Movs $idProd)[0]
Checar 'ultima movimentacao: ajuste -3, 12 -> 9, com motivo e autor' ($m.tipo -eq 'ajuste' -and $m.quantidade -eq -3 -and $m.saldoAnterior -eq 12 -and $m.saldoNovo -eq 9 -and $m.motivo -eq 'Inventario do mes' -and $m.autor -eq 'admin@gmail.com')

# ---------------------------------------------------------------------------
Titulo '3. Editar o produto nao mexe no saldo'
$p = Produto $idProd
$p | Add-Member -NotePropertyName estoque -NotePropertyValue 999 -Force
$p | Add-Member -NotePropertyName valorVenda -NotePropertyValue 27 -Force
$r = SyncProduto $p
Checar 'edicao -> 200' ($r.status -eq 200) "($($r.corpo.error))"
$p = Produto $idProd
Checar 'preco mudou para 27' ($p.valorVenda -eq 27)
Checar 'saldo continua 9 (o 999 da tela foi ignorado)' ($p.estoque -eq 9)
Checar 'foto mantida na edicao' ($p.fotoUrl -like 'data:image/png*')

# ---------------------------------------------------------------------------
Titulo '4. Pedido do cliente'
$email = "estoque.$([Guid]::NewGuid().ToString('N').Substring(0, 8))@lanepets.test"
$cad = Chamar POST '/cliente/cadastro' @{ nome = 'Teste Estoque'; email = $email; senha = 'Senha1234'; telefone = '11999990000'; pet = 'Rex'; tipo = 'Cachorro' }
$tokenCli = $cad.corpo.data.token
$catalogo = (Chamar GET '/cliente/catalogo' $null $tokenCli).corpo.data
$doCat = @($catalogo.produtos | Where-Object { $_.id -eq $idProd })[0]
Checar 'produto visivel aparece no catalogo com descricao e foto' ($doCat -and $doCat.descricao -like 'Racao*' -and $doCat.fotoUrl -like 'data:image*')
Checar 'catalogo nao expoe custo nem estoque minimo' ($null -eq $doCat.PSObject.Properties['valorCompra'] -and $null -eq $doCat.PSObject.Properties['estoqueMinimo'])
$uni = @($catalogo.unidades)[0].id
$ped = Chamar POST '/cliente/pedidos' @{ produtoId = $idProd; quantidade = 6; formaPagamento = 'Pix'; unidade = $uni } $tokenCli
Checar 'pedido de 6 -> 200' ($ped.status -eq 200) "($($ped.corpo.error))"
$idPed1 = $ped.corpo.data.id
Checar 'saldo 9 -> 3' ((Produto $idProd).estoque -eq 3)
Checar 'baixa registrada como venda ligada ao pedido' (@(Movs $idProd | Where-Object { $_.tipo -eq 'venda' -and $_.pedidoId -eq $idPed1 -and $_.quantidade -eq -6 }).Count -eq 1)
Start-Sleep -Milliseconds 800
$ev = (Chamar GET "/admin/eventos?token=$tokenGeral&busca=$idProd&limite=50").corpo.data
Checar 'cruzar o minimo gerou aviso "Estoque baixo" no log' (@($ev.itens | Where-Object { $_.acao -eq 'Estoque baixo' -and $_.nivel -eq 'aviso' }).Count -ge 1)
$resumo = (Chamar GET "/admin/resumo?token=$tokenGeral&de=$((Get-Date).ToString('yyyy-MM-01'))&ate=$((Get-Date).ToString('yyyy-MM-dd'))").corpo.data
Checar 'dashboard lista o produto em estoque baixo' ($resumo.estoqueBaixoTotal -ge 1 -and (@($resumo.estoqueBaixo | Where-Object { $_.id -eq $idProd }).Count -eq 1 -or $resumo.estoqueBaixoTotal -gt 10))
Checar 'pedido maior que o saldo -> 400' ((Chamar POST '/cliente/pedidos' @{ produtoId = $idProd; quantidade = 5; unidade = $uni } $tokenCli).status -eq 400)

# ---------------------------------------------------------------------------
Titulo '5. Cancelamento devolve o estoque'
$r = Chamar POST "/cliente/pedidos/$idPed1/cancelar" $null $tokenCli
Checar 'cliente cancela pedido Pendente -> 200, devolve 6' ($r.status -eq 200 -and $r.corpo.data.devolvido -eq 6) "($($r.corpo.error))"
Checar 'saldo volta para 9' ((Produto $idProd).estoque -eq 9)
Checar 'devolucao registrada como "cancelamento"' (@(Movs $idProd | Where-Object { $_.tipo -eq 'cancelamento' -and $_.pedidoId -eq $idPed1 -and $_.quantidade -eq 6 }).Count -eq 1)
Checar 'cancelar de novo -> 400' ((Chamar POST "/cliente/pedidos/$idPed1/cancelar" $null $tokenCli).status -eq 400)
Checar 'painel nao muda status de pedido cancelado -> 400' ((Chamar POST "/admin/pedidos/$idPed1/status" @{ token = $tokenGeral; status = 'Confirmado' }).status -eq 400)

$ped2 = Chamar POST '/cliente/pedidos' @{ produtoId = $idProd; quantidade = 2; unidade = $uni } $tokenCli
$idPed2 = $ped2.corpo.data.id
Checar 'status invalido -> 400' ((Chamar POST "/admin/pedidos/$idPed2/status" @{ token = $tokenGeral; status = 'Voando' }).status -eq 400)
$r = Chamar POST "/admin/pedidos/$idPed2/status" @{ token = $tokenGeral; status = 'Confirmado' }
Checar 'painel confirma o pedido -> 200' ($r.status -eq 200) "($($r.corpo.error))"
Checar 'cliente NAO cancela pedido confirmado -> 400' ((Chamar POST "/cliente/pedidos/$idPed2/cancelar" $null $tokenCli).status -eq 400)
$r = Chamar POST "/admin/pedidos/$idPed2/status" @{ token = $tokenGeral; status = 'Cancelado' }
Checar 'painel cancela -> devolve 2' ($r.status -eq 200 -and $r.corpo.data.devolvido -eq 2) "($($r.corpo.error))"
Checar 'saldo de volta a 9' ((Produto $idProd).estoque -eq 9)

# ---------------------------------------------------------------------------
Titulo '6. Visivel na loja'
$p = Produto $idProd
$p | Add-Member -NotePropertyName visivelLoja -NotePropertyValue $false -Force
Checar 'ocultar -> 200' ((SyncProduto $p).status -eq 200)
Checar 'oculto some do site publico' (@((Chamar GET '/public/produtos').corpo.data | Where-Object { $_.id -eq $idProd }).Count -eq 0)
Checar 'oculto some do catalogo do cliente' (@((Chamar GET '/cliente/catalogo' $null $tokenCli).corpo.data.produtos | Where-Object { $_.id -eq $idProd }).Count -eq 0)
Checar 'pedido de produto oculto -> 400' ((Chamar POST '/cliente/pedidos' @{ produtoId = $idProd; quantidade = 1; unidade = $uni } $tokenCli).status -eq 400)
Checar 'oculto continua no painel' ((Produto $idProd).visivelLoja -eq $false)

# ---------------------------------------------------------------------------
Titulo '7. Funcionario'
$f = Chamar POST '/admin/usuarios' @{ token = $tokenGeral; nome = 'Funcionario Estoque'; email = "func.est.$sufixo@lanepets.test"; senha = 'Senha1234'; confirmarSenha = 'Senha1234'; perfil = 'Funcionario'; unidade = $uni }
$idFunc = $f.corpo.data.id
$tokenFunc = (Chamar POST '/login' @{ email = "func.est.$sufixo@lanepets.test"; senha = 'Senha1234' }).corpo.data.token
Checar 'funcionario NAO lanca movimentacao -> 403' ((Mov 'entrada' 1 'Tentativa do funcionario' $tokenFunc).status -eq 403)

# ---------------------------------------------------------------------------
Titulo 'Limpeza'
Chamar DELETE "/admin/usuarios/$idFunc`?token=$tokenGeral" $null | Out-Null
Chamar POST '/admin/sync/produtos' @{ token = $tokenGeral; criados = @(); atualizados = @(); removidos = @($idProd) } | Out-Null
Write-Host '  produto e funcionario de teste removidos (o historico de estoque e os pedidos ficam registrados)'

Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
