# Executar o LanePets

O LanePets e uma unica aplicacao ASP.NET Core (.NET 10): ela serve o frontend e a API.
Nao abra os arquivos HTML por duplo clique — nesse modo nao existe API para atender `/api/*`.

## Antes da primeira execucao

Nada a fazer. O projeto duplicado ja foi neutralizado: `PetshopCSharp.csproj` foi
renomeado para `PetshopCSharp.csproj.bak`, entao a pasta tem um unico `.csproj` e
`dotnet run` volta a compilar. Se quiser, apague o `.bak` — ele nao e mais usado.

> Enquanto os dois `.csproj` coexistiam, `dotnet run` parava com "contem mais de um
> arquivo de projeto", o servidor continuava rodando a build anterior e qualquer
> endpoint novo da API respondia **HTTP 405** (rota existe, metodo nao).

## Depois de mexer em qualquer arquivo .cs

HTML, CSS e JS sao servidos do disco: basta atualizar a pagina (Ctrl+F5).
Codigo C# nao — e preciso **parar o processo (Ctrl+C) e rodar `dotnet run` de novo**,
senao a API continua sendo a versao antiga.

## Inicio rapido

No PowerShell, dentro desta pasta:

```powershell
dotnet run --launch-profile LanePets
```

ou

```powershell
./INICIAR-LANEPETS.ps1
```

A aplicacao sobe em **http://localhost:5180**.

## Mapa de rotas

| Rota | O que e |
|---|---|
| `/` ou `/cliente.html` | Home publica (inicio, servicos, produtos, seguros, avaliacoes) |
| `/minha-conta.html` | Login e cadastro do cliente + area do cliente |
| `/admin-login.html` | Login administrativo |
| `/index.html` | Painel administrativo (protegido) |
| `/api/*` | API JSON |

Clicar em **Lane Pets** no topo do painel administrativo volta para a home publica
(`cliente.html`), na mesma aba, sem logout.

## Credenciais administrativas iniciais

- E-mail: `admin@gmail.com`
- Senha: `123456`

A senha e gravada com hash BCrypt. Contas antigas gravadas com PBKDF2 continuam
funcionando — a verificacao aceita os dois formatos.

## Banco de dados

### Onde fica o arquivo

O banco NAO fica mais junto do codigo. A pasta do projeto esta dentro do
OneDrive, e o OneDrive sincroniza `.db`, `-wal` e `-shm` como tres arquivos
independentes — ele pode substituir um deles enquanto o SQLite escreve, e isso
corrompe o banco de verdade.

Por padrao o arquivo vive fora de qualquer sincronizacao:

| Sistema | Caminho |
|---|---|
| Windows | `%LOCALAPPDATA%\LanePets\lanepets.db` |
| Linux / macOS | `~/.local/share/LanePets/lanepets.db` |
| Docker | `/app/data/lanepets.db` |

Quem decide isso e `Services/DatabaseBootstrap.cs`. Se
`ConnectionStrings:LanePetsConnection` estiver preenchida em `appsettings.json`,
ela tem prioridade; vazia (o padrao em desenvolvimento), vale a tabela acima.
Na primeira execucao, um `lanepets.db` que ainda esteja na pasta do projeto e
copiado para o destino pelo mecanismo de backup do proprio SQLite.

### Como o banco e aberto e fechado

- `journal_mode=WAL` — muitas leituras simultaneas (painel, area do cliente e
  dashboard) com escritas esparsas; em WAL leitor nao bloqueia escritor.
- `busy_timeout=15000` — em vez de estourar "database is locked", a segunda
  escrita espera.
- `quick_check` na subida — banco corrompido impede a aplicacao de iniciar, com
  a mensagem completa no log. Nada de erro solto no meio de um login.
- `wal_checkpoint(TRUNCATE)` no encerramento — a aplicacao nunca deixa `-wal`
  nem `-shm` para tras.

> Se um dia sobrar um `lanepets.db-wal` ou `lanepets.db-shm` de um processo que
> morreu, pare a aplicacao e apague os dois. O `.db` sozinho basta.

## Seguranca

- As paginas administrativas (`index.html`, `clientes.html`, `agendamentos.html`,
  `Produtos.html`, `entradas_e_saidas.html`, `dashboard_financeiro.html`,
  `relatorio.html`, `clientes_pacote.html`, `gestao-publica.html`) sao bloqueadas no
  **backend** por `Middleware/AdminAreaGuardMiddleware.cs`. Sem sessao de
  administrador valida, o acesso e redirecionado para `/admin-login.html`.
- Sessao administrativa: 120 minutos. Sessao ASP.NET: 30 minutos.
