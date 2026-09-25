<#
    LanePets - teste das validacoes do backend (item 13 do roadmap).

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-validacoes.ps1

    O QUE ELE PROVA
        Que as regras valem no SERVIDOR, chamando a API direto (sem tela):
        - senha nova: 8+ caracteres, com letra e numero (cliente e admin);
        - e-mail e telefone validos no cadastro do cliente;
        - produto: preco de venda > 0, custo e estoque nunca negativos.

    Cria uma conta de cliente e um produto de teste com nomes aleatorios e
    remove o produto no final. Nao mexe em nenhum dado existente.
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

function Recusado($descricao, $resposta) {
    Checar "$descricao -> 400" ($resposta.status -eq 400) "(HTTP $($resposta.status): $($resposta.corpo.error))"
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

function Cadastro($senha, $email, $telefone) {
    Chamar POST '/cliente/cadastro' @{ nome = 'Teste Validacao'; email = $email; senha = $senha; telefone = $telefone; pet = 'Bidu'; tipo = 'Cachorro' }
}
function EmailNovo { "valida.$([Guid]::NewGuid().ToString('N').Substring(0, 8))@lanepets.test" }

# ---------------------------------------------------------------------------
Titulo '1. Cadastro do cliente'
Recusado 'senha curta (7)'        (Cadastro 'abc1234' (EmailNovo) '11999990000')
Recusado 'senha sem numero'       (Cadastro 'somenteletras' (EmailNovo) '11999990000')
Recusado 'senha sem letra'        (Cadastro '12345678' (EmailNovo) '11999990000')
Recusado 'e-mail sem dominio'     (Cadastro 'Senha1234' 'fulano@' '11999990000')
Recusado 'e-mail sem ponto'       (Cadastro 'Senha1234' 'fulano@lanepets' '11999990000')
Recusado 'telefone sem DDD (8 digitos)' (Cadastro 'Senha1234' (EmailNovo) '99990000')
Recusado 'telefone com letras'    (Cadastro 'Senha1234' (EmailNovo) '11abc990000')
$emailOk = EmailNovo
$ok = Cadastro 'Senha1234' $emailOk '(11) 99999-0000'
Checar 'cadastro valido (senha com letra e numero, telefone formatado) -> 200' ($ok.status -eq 200) "(HTTP $($ok.status): $($ok.corpo.error))"
$tokenCliente = $ok.corpo.data.token

# ---------------------------------------------------------------------------
Titulo '2. Troca de senha e telefone do cliente'
Recusado 'nova senha sem numero' (Chamar PUT '/cliente/conta/senha' @{ senhaAtual = 'Senha1234'; novaSenha = 'SemNumeroAqui'; confirmarSenha = 'SemNumeroAqui' } $tokenCliente)
Recusado 'perfil com telefone invalido' (Chamar PUT '/cliente/conta' @{ nome = 'Teste Validacao'; telefone = '1234'; endereco = '' } $tokenCliente)
Checar 'login antigo continua valido (senha nao mudou)' ((Chamar POST '/cliente/login' @{ email = $emailOk; senha = 'Senha1234' }).status -eq 200)

# ---------------------------------------------------------------------------
Titulo '3. Administrador'
$login = Chamar POST '/login' @{ email = 'admin@gmail.com'; senha = '123456' }
if ($login.status -ne 200) { Write-Host "  [FALHA] Login do admin falhou: $($login.corpo.error)" -ForegroundColor Red; exit 1 }
$tokenAdmin = $login.corpo.data.token
Checar 'senha antiga do admin (123456) continua entrando' ($login.status -eq 200)
Recusado 'criar admin com senha de 6'      (Chamar POST '/admin/usuarios' @{ token = $tokenAdmin; nome = 'X'; email = (EmailNovo); senha = 'abc123'; confirmarSenha = 'abc123' })
Recusado 'criar admin com senha sem letra' (Chamar POST '/admin/usuarios' @{ token = $tokenAdmin; nome = 'X'; email = (EmailNovo); senha = '12345678'; confirmarSenha = '12345678' })
Recusado 'criar admin com e-mail invalido' (Chamar POST '/admin/usuarios' @{ token = $tokenAdmin; nome = 'X'; email = 'admin@'; senha = 'Senha1234'; confirmarSenha = 'Senha1234' })
Recusado 'criar admin com telefone invalido' (Chamar POST '/admin/usuarios' @{ token = $tokenAdmin; nome = 'X'; email = (EmailNovo); telefone = '123'; senha = 'Senha1234'; confirmarSenha = 'Senha1234' })

# ---------------------------------------------------------------------------
Titulo '4. Produtos'
$idProduto = "PRD-TESTE-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
function SyncProduto($criados, $atualizados) {
    Chamar POST '/admin/sync/produtos' @{ token = $tokenAdmin; criados = @($criados); atualizados = @($atualizados); removidos = @() }
}
Recusado 'criar com preco 0'          (SyncProduto @(@{ id = $idProduto; nome = 'Produto Teste'; categoria = 'Teste'; valorCompra = 1; valorVenda = 0 }) @())
Recusado 'criar com preco negativo'   (SyncProduto @(@{ id = $idProduto; nome = 'Produto Teste'; categoria = 'Teste'; valorCompra = 1; valorVenda = -5 }) @())
Recusado 'criar com custo negativo'   (SyncProduto @(@{ id = $idProduto; nome = 'Produto Teste'; categoria = 'Teste'; valorCompra = -1; valorVenda = 10 }) @())
Recusado 'criar com estoque negativo' (SyncProduto @(@{ id = $idProduto; nome = 'Produto Teste'; categoria = 'Teste'; valorCompra = 1; valorVenda = 10; estoque = -3 }) @())
Recusado 'criar sem nome'             (SyncProduto @(@{ id = $idProduto; nome = ''; categoria = 'Teste'; valorCompra = 1; valorVenda = 10 }) @())
$criado = SyncProduto @(@{ id = $idProduto; nome = 'Produto Teste'; categoria = 'Teste'; valorCompra = 1; valorVenda = 10; estoque = 5 }) @()
Checar 'criar produto valido -> 200' ($criado.status -eq 200) "(HTTP $($criado.status): $($criado.corpo.error))"
Recusado 'editar para preco 0' (SyncProduto @() @(@{ id = $idProduto; nome = 'Produto Teste'; categoria = 'Teste'; valorCompra = 1; valorVenda = 0 }))
Recusado 'ajuste de estoque negativo' (Chamar POST "/admin/produtos/$idProduto/estoque" @{ token = $tokenAdmin; estoque = -1; estoqueMinimo = 0; controlaEstoque = $true })
$prod = @((Chamar GET "/admin/produtos?token=$tokenAdmin").corpo.data) | ForEach-Object { $_ } | Where-Object { $_.id -eq $idProduto }
Checar 'produto continua com o preco valido depois das recusas' ($null -ne $prod -and [decimal]$prod.valorVenda -eq 10 -and [int]$prod.estoque -eq 5)

Chamar POST '/admin/sync/produtos' @{ token = $tokenAdmin; criados = @(); atualizados = @(); removidos = @($idProduto) } | Out-Null

# ---------------------------------------------------------------------------
Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
