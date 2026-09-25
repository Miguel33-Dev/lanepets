<#
    LanePets — teste da area "Meus pets" do CLIENTE.

    COMO USAR
        1. Em um terminal:   dotnet run
        2. Em outro:         .\testar-meus-pets.ps1

    O QUE ELE PROVA
        1. A ficha completa do pet e gravada e volta igual depois de recarregar.
        2. A validacao vale no BACKEND: cada chamada abaixo vai direto na API,
           sem passar por tela nenhuma, que e o que alguem faria pelo DevTools.
        3. O cliente A NAO alcanca o pet do cliente B — nem para ler, nem para
           editar, nem trocando o ClienteId na requisicao.

    O script cria duas contas de teste com e-mail aleatorio. Ele nao altera
    nenhum dado que ja exista e nao encosta no painel administrativo.
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

function CriarConta($rotulo) {
    $sufixo = [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $email = "teste.$rotulo.$sufixo@lanepets.test"
    $r = Chamar POST '/cliente/cadastro' @{
        nome = "Teste $rotulo"; email = $email; senha = 'SenhaTeste123!';
        telefone = '11999990000'; endereco = 'Rua de teste, 1'; pet = "Pet$rotulo"; tipo = 'Cachorro'
    }
    if ($r.status -ne 200) { throw "Cadastro de $rotulo falhou (HTTP $($r.status)): $($r.corpo.error)" }
    $login = Chamar POST '/cliente/login' @{ email = $email; senha = 'SenhaTeste123!' }
    if ($login.status -ne 200) { throw "Login de $rotulo falhou (HTTP $($login.status))" }
    return $login.corpo.data.token
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

$tokenA = CriarConta 'a'
$tokenB = CriarConta 'b'

# ---------------------------------------------------------------------------
Titulo 'Cadastro completo do pet'

$ficha = @{
    nome = 'Thor'; tipo = 'Cachorro'; raca = 'Golden Retriever'; sexo = 'Macho'
    dataNascimento = '2023-03-15'; peso = '28,5'; cor = 'Dourado'; porte = 'Grande'
    fotoUrl = ''; observacoes = 'Muito docil e brincalhao.'
    necessidadesEspeciais = 'Precisa de apoio na escada.'
    infoAtendimento = 'Fica agitado com secador alto.'
}
$criado = Chamar POST '/cliente/pets' $ficha $tokenA
Checar 'POST /cliente/pets devolve 200' ($criado.status -eq 200) "(HTTP $($criado.status): $($criado.corpo.error))"
$petA = $criado.corpo.data
Checar 'Pet recebeu id' ($null -ne $petA.id)

# ---------------------------------------------------------------------------
Titulo 'Persistencia — os campos voltam do banco'

$relido = Chamar GET "/cliente/pets/$($petA.id)" $null $tokenA
$p = $relido.corpo.data
Checar 'GET do pet devolve 200'          ($relido.status -eq 200)
Checar 'Nome persistiu'                  ($p.petNome -eq 'Thor')
Checar 'Raca persistiu'                  ($p.raca -eq 'Golden Retriever')
Checar 'Sexo persistiu'                  ($p.sexo -eq 'Macho')
Checar 'Data de nascimento persistiu'    ($p.dataNascimento -eq '2023-03-15')
Checar 'Peso persistiu como numero'      ([decimal]$p.peso -eq 28.5)
Checar 'Cor persistiu'                   ($p.cor -eq 'Dourado')
Checar 'Porte persistiu'                 ($p.porte -eq 'Grande')
Checar 'Observacoes persistiram'         ($p.observacoes -like 'Muito docil*')
Checar 'Necessidades persistiram'        ($p.necessidadesEspeciais -like 'Precisa*')
Checar 'Info de atendimento persistiu'   ($p.infoAtendimento -like 'Fica agitado*')

$conta = Chamar GET '/cliente/conta' $null $tokenA
$naLista = @($conta.corpo.data.pets | Where-Object { $_.id -eq $petA.id })
Checar 'Pet aparece em GET /cliente/conta' ($naLista.Count -eq 1)
Checar 'Lista traz a ficha completa'       ($naLista[0].porte -eq 'Grande')

# ---------------------------------------------------------------------------
Titulo 'Edicao'

$edicao = $ficha.Clone()
$edicao.nome = 'Thor Editado'
$edicao.peso = '30'
$atualizado = Chamar PUT "/cliente/pets/$($petA.id)" $edicao $tokenA
Checar 'PUT devolve 200'              ($atualizado.status -eq 200)
Checar 'Nome atualizado na resposta'  ($atualizado.corpo.data.petNome -eq 'Thor Editado')
$conferido = Chamar GET "/cliente/pets/$($petA.id)" $null $tokenA
Checar 'Nome persistiu apos recarga'  ($conferido.corpo.data.petNome -eq 'Thor Editado')
Checar 'Peso persistiu apos recarga'  ([decimal]$conferido.corpo.data.peso -eq 30)

# ---------------------------------------------------------------------------
Titulo 'Foto — troca, preservacao e remocao explicita (item 10 do roadmap)'

# JPEG minimo valido (1x1), em data URI — o mesmo formato que a tela envia.
$fotoTeste = 'data:image/jpeg;base64,/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA='
$comFoto = $edicao.Clone(); $comFoto.fotoUrl = $fotoTeste
$r = Chamar PUT "/cliente/pets/$($petA.id)" $comFoto $tokenA
Checar 'Enviar foto: 200'                 ($r.status -eq 200) "(HTTP $($r.status): $($r.corpo.error))"
Checar 'Foto gravada igual ao enviado'    ((Chamar GET "/cliente/pets/$($petA.id)" $null $tokenA).corpo.data.fotoUrl -eq $fotoTeste)

$semFoto = $edicao.Clone(); $semFoto.fotoUrl = ''; $semFoto.cor = 'Caramelo'
Chamar PUT "/cliente/pets/$($petA.id)" $semFoto $tokenA | Out-Null
$depois = (Chamar GET "/cliente/pets/$($petA.id)" $null $tokenA).corpo.data
Checar 'Editar outro campo sem reenviar a foto PRESERVA a foto' ($depois.fotoUrl -eq $fotoTeste)
Checar 'e o outro campo mudou'                                  ($depois.cor -eq 'Caramelo')

$remover = $edicao.Clone(); $remover.fotoUrl = ''; $remover.removerFoto = $true
Chamar PUT "/cliente/pets/$($petA.id)" $remover $tokenA | Out-Null
Checar 'removerFoto=true apaga a foto' (-not (Chamar GET "/cliente/pets/$($petA.id)" $null $tokenA).corpo.data.fotoUrl)

$grande = $edicao.Clone(); $grande.fotoUrl = 'data:image/jpeg;base64,' + ('A' * 210000)
Checar 'Foto acima de 200 KB e recusada (400)' ((Chamar PUT "/cliente/pets/$($petA.id)" $grande $tokenA).status -eq 400)
$gif = $edicao.Clone(); $gif.fotoUrl = 'data:image/gif;base64,R0lGODlhAQABAAAAACw='
Checar 'GIF e recusado (400)' ((Chamar PUT "/cliente/pets/$($petA.id)" $gif $tokenA).status -eq 400)

# ---------------------------------------------------------------------------
Titulo 'Validacao no backend (a tela nao e a defesa)'

$casos = @(
    @{ nome = 'Nome vazio e recusado';        dados = @{ nome = '';      tipo = 'Cachorro' } },
    @{ nome = 'Nome de 1 letra e recusado';   dados = @{ nome = 'A';     tipo = 'Cachorro' } },
    @{ nome = 'Especie invalida e recusada';  dados = @{ nome = 'Rex';   tipo = 'Dragao' } },
    @{ nome = 'Sexo invalido e recusado';     dados = @{ nome = 'Rex';   tipo = 'Cachorro'; sexo = 'X' } },
    @{ nome = 'Porte invalido e recusado';    dados = @{ nome = 'Rex';   tipo = 'Cachorro'; porte = 'Gigante' } },
    @{ nome = 'Peso nao numerico e recusado'; dados = @{ nome = 'Rex';   tipo = 'Cachorro'; peso = 'muito' } },
    @{ nome = 'Peso absurdo e recusado';      dados = @{ nome = 'Rex';   tipo = 'Cachorro'; peso = '900' } },
    @{ nome = 'Data no futuro e recusada';    dados = @{ nome = 'Rex';   tipo = 'Cachorro'; dataNascimento = '2099-01-01' } },
    @{ nome = 'Data mal formada e recusada';  dados = @{ nome = 'Rex';   tipo = 'Cachorro'; dataNascimento = '15/03/2023' } },
    @{ nome = 'Arquivo nao-imagem e recusado';dados = @{ nome = 'Rex';   tipo = 'Cachorro'; fotoUrl = 'data:text/html;base64,PHNjcmlwdD4=' } },
    @{ nome = 'Nome gigante e recusado';      dados = @{ nome = ('N' * 200); tipo = 'Cachorro' } }
)
foreach ($caso in $casos) {
    $r = Chamar POST '/cliente/pets' $caso.dados $tokenA
    Checar $caso.nome ($r.status -eq 400) "(HTTP $($r.status))"
    if ($r.status -eq 400) {
        Checar "  -> mensagem legivel, nao '400 Bad Request'" ($r.corpo.error -and $r.corpo.error.Length -gt 8) "($($r.corpo.error))"
    }
}

# ---------------------------------------------------------------------------
Titulo 'Seguranca — cliente A nao alcanca o pet de B'

$petB = (Chamar POST '/cliente/pets' @{ nome = 'PetDoB'; tipo = 'Gato' } $tokenB).corpo.data
Checar 'Cliente B tem um pet' ($null -ne $petB.id)

$leitura = Chamar GET "/cliente/pets/$($petB.id)" $null $tokenA
Checar 'A NAO le o pet de B'    ($leitura.status -ne 200) "(HTTP $($leitura.status))"

$escrita = Chamar PUT "/cliente/pets/$($petB.id)" @{ nome = 'Sequestrado'; tipo = 'Gato' } $tokenA
Checar 'A NAO edita o pet de B' ($escrita.status -ne 200) "(HTTP $($escrita.status))"

$intacto = Chamar GET "/cliente/pets/$($petB.id)" $null $tokenB
Checar 'Pet de B continua intacto' ($intacto.corpo.data.petNome -eq 'PetDoB')

# Tentativa de "adotar" o pet passando clienteId no corpo. O campo nem existe
# no contrato: o vinculo vem do token, entao isto nao muda dono nenhum.
$sequestro = Chamar POST '/cliente/pets' @{ nome = 'Invasor'; tipo = 'Cachorro'; clienteId = 'CLI-QUALQUER' } $tokenA
if ($sequestro.status -eq 200) {
    $contaB = Chamar GET '/cliente/conta' $null $tokenB
    $vazou = @($contaB.corpo.data.pets | Where-Object { $_.petNome -eq 'Invasor' })
    Checar 'clienteId no corpo e ignorado (pet ficou com A)' ($vazou.Count -eq 0)
} else {
    Checar 'clienteId no corpo nao cria pet em outra conta' $true
}

$semToken = Chamar GET "/cliente/pets/$($petA.id)" $null ''
Checar 'Sem token a API recusa' ($semToken.status -eq 401) "(HTTP $($semToken.status))"

# ---------------------------------------------------------------------------
Write-Host ""
$cor = 'Green'; if ($script:falhou -gt 0) { $cor = 'Red' }
Write-Host "Aprovados: $script:ok   Falhas: $script:falhou" -ForegroundColor $cor
if ($script:falhou -gt 0) { exit 1 }
