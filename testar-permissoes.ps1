<#
    LanePets — teste do sistema de permissoes administrativas.

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-permissoes.ps1

    O QUE ELE PROVA
        Que a permissao vale no BACKEND. Cada teste chama a API direto, sem
        passar por tela nenhuma — exatamente o que alguem faria tentando burlar
        o painel pelo DevTools ou pela barra de enderecos. Onde falta permissao
        a resposta tem de ser 403, nunca 200.
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
function Chamar($metodo, $caminho, $corpo) {
    $parametros = @{ Method = $metodo; Uri = "$base$caminho"; UseBasicParsing = $true }
    if ($null -ne $corpo) {
        $parametros.Body = ([System.Text.Encoding]::UTF8.GetBytes(($corpo | ConvertTo-Json -Depth 6)))
        $parametros.ContentType = 'application/json; charset=utf-8'
    }
    # PowerShell 7 tem -SkipHttpErrorCheck; o 5.1 nao tem e lanca excecao em
    # 4xx. Como 403 e justamente o resultado que queremos medir, o script
    # funciona nos dois: usa o parametro quando existe, e senao le o status
    # de dentro da excecao.
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

function Entrar($email, $senha) {
    $r = Chamar POST '/login' @{ email = $email; senha = $senha }
    if ($r.status -ne 200) { throw "Login de $email falhou (HTTP $($r.status)): $($r.corpo.error)" }
    return $r.corpo.data.token
}

# ---------------------------------------------------------------------------
Titulo 'Servidor no ar'
$saude = Chamar GET '/health' $null
Checar 'GET /api/health responde 200' ($saude.status -eq 200)

# ---------------------------------------------------------------------------
Titulo '1. Administrador Geral (admin@gmail.com)'
$tokenGeral = Entrar 'admin@gmail.com' '123456'
$eu = Chamar GET "/admin/me?token=$tokenGeral" $null
Checar 'perfil gravado no banco e AdminGeral'   ($eu.corpo.data.usuario.perfil -eq 'AdminGeral')
Checar 'possui acesso irrestrito'               ($eu.corpo.data.acessoIrrestrito -eq $true)
Checar 'enxerga Usuarios Administrativos'       ($eu.corpo.data.modulos.usuarios.visualizar -eq $true)
Checar 'enxerga Pagamentos'                     ($eu.corpo.data.modulos.pagamentos.visualizar -eq $true)

# ---------------------------------------------------------------------------
Titulo '2. Criacao de administrador limitado (Joao)'
$emailJoao = "joao.teste+$(Get-Random -Maximum 99999)@lanepets.com"
$criado = Chamar POST '/admin/usuarios' @{
    token = $tokenGeral; nome = 'João Silva'; email = $emailJoao
    senha = 'senha123'; confirmarSenha = 'senha123'; ativo = $true
}
Checar 'criacao aceita (200)' ($criado.status -eq 200) "-> $($criado.corpo.error)"
$idJoao = $criado.corpo.data.id

$tokenJoao = Entrar $emailJoao 'senha123'
$euJoao = Chamar GET "/admin/me?token=$tokenJoao" $null
Checar 'Joao entra no painel'                         ($euJoao.status -eq 200)
Checar 'Joao NAO tem acesso irrestrito'               ($euJoao.corpo.data.acessoIrrestrito -eq $false)
Checar 'Joao nasce sem permissao de clientes'         ($euJoao.corpo.data.modulos.clientes.visualizar -eq $false)
Checar 'Joao nasce sem permissao de produtos'         ($euJoao.corpo.data.modulos.produtos.visualizar -eq $false)

# ---------------------------------------------------------------------------
Titulo '3. Sem permissao, a API responde 403 (nao 200)'
Checar 'GET /api/clientes -> 403'              ((Chamar GET "/clientes?token=$tokenJoao" $null).status -eq 403)
Checar 'GET /api/pedidos -> 403'               ((Chamar GET "/pedidos?token=$tokenJoao" $null).status -eq 403)
Checar 'GET /api/admin/produtos -> 403'        ((Chamar GET "/admin/produtos?token=$tokenJoao" $null).status -eq 403)
Checar 'GET /api/admin/entradas-saidas -> 403' ((Chamar GET "/admin/entradas-saidas?token=$tokenJoao" $null).status -eq 403)
Checar 'GET /api/admin/seguros -> 403'         ((Chamar GET "/admin/seguros?token=$tokenJoao" $null).status -eq 403)
Checar 'GET /api/admin/depoimentos -> 403'     ((Chamar GET "/admin/depoimentos?token=$tokenJoao" $null).status -eq 403)
Checar 'GET /api/admin/usuarios -> 403'        ((Chamar GET "/admin/usuarios?token=$tokenJoao" $null).status -eq 403)
Checar 'GET /api/admin/resumo -> 403'          ((Chamar GET "/admin/resumo?token=$tokenJoao" $null).status -eq 403)

# ---------------------------------------------------------------------------
Titulo '4. Liberando apenas Clientes e Pets'
$salvo = Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{
    token = $tokenGeral; acessoTotal = $false
    modulos = @(
        @{ chave = 'clientes'; visualizar = $true; criar = $true;  editar = $true;  excluir = $false }
        @{ chave = 'pets';     visualizar = $true; criar = $false; editar = $false; excluir = $false }
    )
}
Checar 'permissoes salvas' ($salvo.status -eq 200) "-> $($salvo.corpo.error)"

Checar 'GET /api/clientes -> 200 (liberado)'   ((Chamar GET "/clientes?token=$tokenJoao" $null).status -eq 200)
Checar 'GET /api/pets -> 200 (liberado)'       ((Chamar GET "/pets?token=$tokenJoao" $null).status -eq 200)
Checar 'GET /api/admin/produtos ainda 403'     ((Chamar GET "/admin/produtos?token=$tokenJoao" $null).status -eq 403)
Checar 'GET /api/admin/seguros ainda 403'      ((Chamar GET "/admin/seguros?token=$tokenJoao" $null).status -eq 403)
Checar 'financeiro_login ainda 403'            ((Chamar POST '/financeiro_login' @{ token = $tokenJoao; senha = '123456' }).status -eq 403)

Titulo '5. Granularidade: pode criar cliente, nao pode excluir'
$criarCliente = Chamar POST '/admin/sync/clientes' @{
    token = $tokenJoao
    criados = @(@{ id = "CLI-TESTE-$(Get-Random -Maximum 99999)"; nome = 'Cliente de teste'; telefone = '11999999999' })
    atualizados = @(); removidos = @()
}
Checar 'POST sync/clientes com criados -> 200' ($criarCliente.status -eq 200) "-> $($criarCliente.corpo.error)"
$idNovoCliente = $criarCliente.corpo.data.novosIds[0]

$excluirCliente = Chamar POST '/admin/sync/clientes' @{
    token = $tokenJoao; criados = @(); atualizados = @(); removidos = @($idNovoCliente)
}
Checar 'POST sync/clientes com removidos -> 403' ($excluirCliente.status -eq 403)

$produtoJoao = Chamar POST '/admin/sync/produtos' @{
    token = $tokenJoao
    criados = @(@{ id = 'PRD-TESTE'; nome = 'Produto proibido' }); atualizados = @(); removidos = @()
}
Checar 'POST sync/produtos -> 403 (modulo nao liberado)' ($produtoJoao.status -eq 403)

# limpeza do cliente de teste, pelo Administrador Geral
Chamar POST '/admin/sync/clientes' @{ token = $tokenGeral; criados = @(); atualizados = @(); removidos = @($idNovoCliente) } | Out-Null

# ---------------------------------------------------------------------------
Titulo '6. Acesso total ligado e desligado'
Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{
    token = $tokenGeral; acessoTotal = $true
    modulos = @(
        @{ chave = 'clientes'; visualizar = $true; criar = $true;  editar = $true;  excluir = $false }
        @{ chave = 'pets';     visualizar = $true; criar = $false; editar = $false; excluir = $false }
    )
} | Out-Null
Checar 'com acesso total: /api/admin/produtos -> 200' ((Chamar GET "/admin/produtos?token=$tokenJoao" $null).status -eq 200)
Checar 'com acesso total: /api/admin/seguros -> 200'  ((Chamar GET "/admin/seguros?token=$tokenJoao" $null).status -eq 200)

Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{
    token = $tokenGeral; acessoTotal = $false
    modulos = @(
        @{ chave = 'clientes'; visualizar = $true; criar = $true;  editar = $true;  excluir = $false }
        @{ chave = 'pets';     visualizar = $true; criar = $false; editar = $false; excluir = $false }
    )
} | Out-Null
Checar 'sem acesso total: /api/admin/produtos volta a 403' ((Chamar GET "/admin/produtos?token=$tokenJoao" $null).status -eq 403)
Checar 'sem acesso total: /api/clientes continua 200'      ((Chamar GET "/clientes?token=$tokenJoao" $null).status -eq 200)

# ---------------------------------------------------------------------------
Titulo '7. Administrador comum nao aumenta o proprio poder'
Checar 'Joao nao le a lista de administradores' ((Chamar GET "/admin/usuarios?token=$tokenJoao" $null).status -eq 403)
Checar 'Joao nao altera as proprias permissoes' ((Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{ token = $tokenJoao; acessoTotal = $true; modulos = @() }).status -eq 403)
Checar 'Joao nao cria administrador'            ((Chamar POST '/admin/usuarios' @{ token = $tokenJoao; nome = 'Invasor'; email = "invasor$(Get-Random)@x.com"; senha = 'senha123'; confirmarSenha = 'senha123' }).status -eq 403)

# mesmo liberando o modulo Usuarios, conceder permissao continua sendo do Geral
Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{
    token = $tokenGeral; acessoTotal = $false
    modulos = @(@{ chave = 'usuarios'; visualizar = $true; criar = $true; editar = $true; excluir = $true })
} | Out-Null
Checar 'o modulo Usuarios nao e delegavel -> Joao segue em 403' ((Chamar GET "/admin/usuarios?token=$tokenJoao" $null).status -eq 403)
Checar 'Joao segue sem conceder permissao a ninguem'            ((Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{ token = $tokenJoao; acessoTotal = $true; modulos = @() }).status -eq 403)

# Nem o acesso total abre essa porta: acesso total libera as ferramentas do
# petshop, nunca a autoridade sobre os outros administradores.
Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{ token = $tokenGeral; acessoTotal = $true; modulos = @() } | Out-Null
Checar 'com ACESSO TOTAL, Joao abre Produtos'                     ((Chamar GET "/admin/produtos?token=$tokenJoao" $null).status -eq 200)
Checar 'com ACESSO TOTAL, Joao AINDA leva 403 em /admin/usuarios' ((Chamar GET "/admin/usuarios?token=$tokenJoao" $null).status -eq 403)
Checar 'com ACESSO TOTAL, Joao AINDA nao cria administrador'      ((Chamar POST '/admin/usuarios' @{ token = $tokenJoao; nome = 'Invasor 2'; email = "invasor2$(Get-Random)@x.com"; senha = 'senha123'; confirmarSenha = 'senha123' }).status -eq 403)
Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{ token = $tokenGeral; acessoTotal = $false; modulos = @() } | Out-Null

$idGeral = ((Chamar GET "/admin/usuarios?token=$tokenGeral" $null).corpo.data.usuarios | Where-Object { $_.email -eq 'admin@gmail.com' }).id
Checar 'Joao nao desativa o Administrador Geral'  ((Chamar POST "/admin/usuarios/$idGeral/status" @{ token = $tokenJoao; ativo = $false }).status -eq 403)
Checar 'Joao nao exclui o Administrador Geral'    ((Chamar DELETE "/admin/usuarios/$idGeral`?token=$tokenJoao" $null).status -eq 403)
Checar 'Joao nao redefine a senha do Admin Geral' ((Chamar POST "/admin/usuarios/$idGeral/senha" @{ token = $tokenJoao; senha = 'hackeado1'; confirmarSenha = 'hackeado1' }).status -eq 403)

# ---------------------------------------------------------------------------
Titulo '8. Protecao do unico Administrador Geral'
Checar 'Geral nao desativa a si mesmo'       ((Chamar POST "/admin/usuarios/$idGeral/status" @{ token = $tokenGeral; ativo = $false }).status -eq 403)
Checar 'Geral nao exclui o unico Geral'      ((Chamar DELETE "/admin/usuarios/$idGeral`?token=$tokenGeral" $null).status -eq 403)
Checar 'Geral nao muda as proprias permissoes' ((Chamar PUT "/admin/usuarios/$idGeral/permissoes" @{ token = $tokenGeral; acessoTotal = $true; modulos = @() }).status -eq 403)

# ---------------------------------------------------------------------------
Titulo '9. Administrador desativado perde o painel'
Chamar POST "/admin/usuarios/$idJoao/status" @{ token = $tokenGeral; ativo = $false } | Out-Null
Checar 'token antigo de Joao deixa de operar (401)' ((Chamar GET "/admin/usuarios?token=$tokenJoao" $null).status -eq 401)
Checar 'Joao nao consegue logar de novo'            ((Chamar POST '/login' @{ email = $emailJoao; senha = 'senha123' }).status -eq 401)

Chamar POST "/admin/usuarios/$idJoao/status" @{ token = $tokenGeral; ativo = $true } | Out-Null
Checar 'reativado, Joao loga de novo' ((Chamar POST '/login' @{ email = $emailJoao; senha = 'senha123' }).status -eq 200)

# ---------------------------------------------------------------------------
Titulo '10. Auditoria'
$auditoria = Chamar GET "/admin/usuarios/auditoria?token=$tokenGeral" $null
Checar 'registros gravados' ($auditoria.corpo.data.registros.Count -gt 0)
Checar 'a criacao de administrador foi registrada' (@($auditoria.corpo.data.registros | Where-Object { $_.acao -like 'Criou*' }).Count -gt 0)

# ---------------------------------------------------------------------------
Titulo '11. Perfil Funcionario (item 1 do roadmap)'
$emailFunc = "funcionario$(Get-Random)@lanepets.test"
$semUnidade = Chamar POST '/admin/usuarios' @{ token = $tokenGeral; nome = 'Func Teste'; email = $emailFunc; senha = 'senha123'; confirmarSenha = 'senha123'; perfil = 'Funcionario' }
Checar 'funcionario sem unidade e recusado (400)' ($semUnidade.status -eq 400)

$criadoFunc = Chamar POST '/admin/usuarios' @{ token = $tokenGeral; nome = 'Func Teste'; email = $emailFunc; senha = 'senha123'; confirmarSenha = 'senha123'; perfil = 'Funcionario'; unidade = 'franco'; acessoTotal = $true }
Checar 'funcionario com unidade e criado' ($criadoFunc.status -eq 200) "-> $($criadoFunc.corpo.error)"
$idFunc = $criadoFunc.corpo.data.id
$daLista = (Chamar GET "/admin/usuarios?token=$tokenGeral" $null).corpo.data.usuarios | Where-Object { $_.id -eq $idFunc }
Checar 'nasce SEM acesso total, mesmo pedindo' (-not $daLista.acessoTotal)
Checar 'lista mostra perfil e unidade'         ($daLista.funcionario -and $daLista.unidade -eq 'franco')

Checar 'nao recebe acesso total (403)' ((Chamar PUT "/admin/usuarios/$idFunc/permissoes" @{ token = $tokenGeral; acessoTotal = $true; modulos = @() }).status -eq 403)

$pedido = @(
    @{ chave = 'agendamentos'; visualizar = $true; criar = $true; editar = $true; excluir = $true },
    @{ chave = 'produtos';     visualizar = $true; criar = $true; editar = $true; excluir = $true },
    @{ chave = 'pagamentos';   visualizar = $true; criar = $false; editar = $false; excluir = $false },
    @{ chave = 'relatorios';   visualizar = $true; criar = $false; editar = $false; excluir = $false },
    @{ chave = 'dashboard';    visualizar = $true; criar = $false; editar = $false; excluir = $false }
)
Chamar PUT "/admin/usuarios/$idFunc/permissoes" @{ token = $tokenGeral; acessoTotal = $false; modulos = $pedido } | Out-Null
$gravadas = (Chamar GET "/admin/usuarios/$idFunc/permissoes?token=$tokenGeral" $null).corpo.data.modulos
$m = @{}; $gravadas | ForEach-Object { $m[$_.chave] = $_ }
Checar 'agendamentos liberado por inteiro'          ($m['agendamentos'].visualizar -and $m['agendamentos'].excluir)
Checar 'produtos fica so em visualizar (teto)'      ($m['produtos'].visualizar -and -not $m['produtos'].criar -and -not $m['produtos'].editar)
Checar 'pagamentos descartado (fora do perfil)'     (-not $m['pagamentos'].visualizar)
Checar 'relatorios descartado (fora do perfil)'     (-not $m['relatorios'].visualizar)

$tokenFunc = Entrar $emailFunc 'senha123'
Checar '/api/admin/me marca funcionario + unidade' ((Chamar GET "/admin/me?token=$tokenFunc" $null).corpo.data.usuario.unidade -eq 'Franco')
Checar 'funcionario leva 403 em /admin/relatorio'       ((Chamar GET "/admin/relatorio?token=$tokenFunc" $null).status -eq 403)
Checar 'funcionario leva 403 em /admin/entradas-saidas' ((Chamar GET "/admin/entradas-saidas?token=$tokenFunc" $null).status -eq 403)
Checar 'funcionario leva 403 em /admin/usuarios'        ((Chamar GET "/admin/usuarios?token=$tokenFunc" $null).status -eq 403)

$agsFunc = (Chamar GET "/agendamentos?token=$tokenFunc" $null).corpo.data.agendamentos
Checar 'so recebe agendamentos da unidade dele' (@($agsFunc | Where-Object { $_.unidade -ne 'Franco' }).Count -eq 0)
$estadoFunc = (Chamar GET "/admin/estado?token=$tokenFunc" $null).corpo.data
Checar '/admin/estado sem agendamento de outra unidade' (@($estadoFunc.agendamentos | Where-Object { $_.unidade -and $_.unidade -notmatch 'franco' }).Count -eq 0)
Checar '/api/unidades devolve so a dele' ((@((Chamar GET "/unidades?token=$tokenFunc" $null).corpo.data.unidades) -join ',') -eq 'Franco')

$outraUnidade = Chamar POST '/admin/sync/agendamentos' @{ token = $tokenFunc; criados = @(@{ pet = 'Teste'; dono = 'Teste'; telefone = '11999999999'; dataHora = '2099-01-01T10:00'; unidade = 'caieiras'; status = 'Pendente' }); atualizados = @(); removidos = @() }
Checar 'nao cria agendamento em outra unidade (403)' ($outraUnidade.status -eq 403)

Chamar DELETE "/admin/usuarios/$idFunc`?token=$tokenGeral" $null | Out-Null

# ---------------------------------------------------------------------------
Titulo 'Limpeza'
$remocao = Chamar DELETE "/admin/usuarios/$idJoao`?token=$tokenGeral" $null
Checar 'usuario de teste removido' ($remocao.status -eq 200) "-> $($remocao.corpo.error)"

# ---------------------------------------------------------------------------
Write-Host "`n--------------------------------------------"
Write-Host ("  Passaram: {0}   Falharam: {1}" -f $script:ok, $script:falhou) -ForegroundColor ($(if ($script:falhou -eq 0) { 'Green' } else { 'Red' }))
Write-Host "--------------------------------------------`n"
if ($script:falhou -gt 0) { exit 1 }
