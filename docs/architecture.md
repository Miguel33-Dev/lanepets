<p align="center">
  <img src="img/banner-architecture.svg" alt="LanePets — Arquitetura" width="100%">
</p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white">
  <img alt="EF Core" src="https://img.shields.io/badge/EF_Core-10.0.11-512BD4">
  <img alt="SQLite" src="https://img.shields.io/badge/SQLite-banco_único-003B57?logo=sqlite&logoColor=white">
  <img alt="SignalR" src="https://img.shields.io/badge/SignalR-tempo_real-07562e">
  <img alt="xUnit" src="https://img.shields.io/badge/xUnit-242_testes-1f7a45">
  <img alt="Docker" src="https://img.shields.io/badge/Docker-multi--stage-2496ED?logo=docker&logoColor=white">
</p>

<p align="center">
  <a href="PRD.md">PRD</a> ·
  <b>Arquitetura</b> ·
  <a href="rules.md">Regras</a> ·
  <a href="design.md">Design</a> ·
  <a href="tasks.md">Tarefas</a> ·
  <a href="memory.md">Memória</a>
</p>

> [!NOTE]
> Atualizado em **29/09/2026**. Uma única aplicação **ASP.NET Core (.NET 10)** serve o frontend (HTML, CSS e JS de
> `wwwroot/`), a API REST (`/api/...`) e um hub SignalR (`/hubs/lanepets`) para o tempo real do painel.

---

## 🧭 1. Visão geral

<p align="center">
  <img src="img/arquitetura.svg" alt="Arquitetura do LanePets" width="100%">
</p>

## 🧰 2. Stack

| Camada | Tecnologia |
|---|---|
| ⚙️ Backend | .NET 10 (`net10.0`, `RootNamespace: LanePets`), ASP.NET Core |
| 🗄️ Banco | SQLite + EF Core 10.0.11 |
| 🔑 Senhas | BCrypt.Net-Next 4.2.0 |
| ⚡ Tempo real | SignalR |
| 🎨 Frontend | HTML + CSS + JavaScript puro, **sem framework** |
| 🔐 Login social | Google Identity Services + validação RS256/JWKS própria, **sem pacote NuGet** |
| 📊 Planilha | `PlanilhaXlsx` própria (ZIP + Office Open XML), **sem pacote** |
| 🧪 Testes | xUnit 2.9.3 + `Microsoft.AspNetCore.Mvc.Testing` 10 |
| 🔁 CI | GitHub Actions "LanePets CI" |
| 🚀 Deploy | Docker → Railway |

## 📁 3. Estrutura de pastas

```text
LanePetsCSharp/
├── Controllers/   AdminStore · AdminSync · AgendaPainel · ApiControllerBase · Auth · ClientPortal · Data ·
│                  Eventos · Integridade · Notificacoes · Pagamentos · Public · Unidades · UsuariosAdmin
├── Services/      regra de negócio (45 arquivos)
├── Data/          LanePetsDbContext + seed/*.csv (produtos e 43 serviços)
├── Models/        Entities.cs
├── DTOs/          contratos conferidos por tela (records)
├── Middleware/    AdminAreaGuard · ErroGlobal
├── Hubs/          hub SignalR
├── wwwroot/       20 páginas · design-system.css · publico.css · conta.css · auth.css · js/ · css/
├── tests/         LanePets.Tests (Infra · Autenticacao · Operacao · Painel)
├── docs/          esta documentação
└── Dockerfile · INICIAR-LANEPETS.ps1 · TESTAR-LANEPETS.ps1 · testar-*.ps1 · README.md
```

## 🔀 4. Pipeline HTTP

```mermaid
flowchart LR
    A[Forwarded<br>Headers] --> B[HSTS / HTTPS<br>fora do Dev] --> C[ErroGlobal] --> D[StatusCodePages<br>404/405 no envelope] --> E[CORS] --> F[Session] --> G[AdminAreaGuard] --> H[Arquivos<br>estáticos] --> I[Routing] --> J[Controllers] --> K[Hub]
```

Em hospedagem, a porta vem da variável `PORT`. O `Program.cs` termina com `public partial class Program { }`, usado
pelos testes.

## 🧱 5. Camadas

| Camada | Papel | Exemplos |
|---|---|---|
| **Controller** | só lê a requisição, checa a permissão, chama o Service e monta a resposta | `ClientPortalController` |
| **Service `static`** | cálculo puro | `IndicadoresFinanceiros`, `MensalidadesSeguro`, `StatusAgendamento`, `ListaPaginada`, `Validacao` |
| **Service scoped** | estado da requisição | `SyncPainelService`, `ClientesPainelService`, `AgendaPainelService` |
| **Singleton** | recurso compartilhado | `EmailService`, `EventosService` (DbContext próprio), `GoogleTokenService` |
| **Worker** | segundo plano | `CobrancaMensalSeguroWorker` e `LembreteAgendamentoWorker` (de hora em hora), `RetencaoLogWorker` (diário) |

> [!IMPORTANT]
> **Sem Repositories:** os Services usam o `DbContext` direto. Não existe regra de negócio no controller.

<details>
<summary><b>Todos os Services por área</b></summary>

- **Contas e acesso:** Session · Permissao · ContaCliente · ContaGoogle · GoogleLogin · GoogleToken · RecuperacaoSenha · Visitante · Hospedagem
- **Operação:** AgendamentosCliente · Pedidos · Estoque · Pagamentos · SegurosCliente · Avaliacoes · Fidelidade · AvisosCliente · UnidadesRegras · ResponsaveisAgendamento
- **Painel:** SyncPainel · ClientesPainel · AgendaPainel · FormatoPainel · NotificacoesPainel
- **Plataforma:** Banco · Integridade · Importacao · Eventos · RetencaoEventos · Email · PlanilhaXlsx · Seed · DatabaseBootstrap · Normalizador · Csv · Realtime

</details>

## 🗄️ 6. Banco de dados

**Um banco só (`lanepets.db`), fora da pasta do código**, porque o OneDrive corrompia o SQLite.
`ConnectionStrings:LanePetsConnection` tem prioridade.

| Ambiente | Pasta do banco |
|---|---|
| 🪟 Windows | `%LOCALAPPDATA%\LanePets\` |
| 🐧 Linux | `~/.local/share/LanePets/` |
| 🐳 Docker | `/app/data/` |
| 🧪 Testes | `%TEMP%\LanePets.Tests\<id>\` |

- **Sem EF Migrations:** `EnsureCreated()` + `GarantirColunaAsync` / `CREATE TABLE|INDEX IF NOT EXISTS`. Coluna nova é sempre opcional. **Sem foreign key**, com índices e varredura de integridade.

```mermaid
flowchart LR
    A[WAL +<br>quick_check] --> B[Backup<br>VACUUM INTO<br>10 últimos] --> C[Seed] --> D[Unidades] --> E[26 índices] --> F[Reconciliação<br>de pagamentos] --> G[Retenção<br>do Log]
```

<details>
<summary><b>Entidades (23)</b></summary>

Pet · Cliente · Agendamento · Servico · Produto · EntradaSaida · Pacote · Configuracao · Auditoria* ·
ReconciliacaoMigracao · Depoimento · PlanoSeguro · SolicitacaoSeguro · UsuarioCliente (`GoogleSub`) ·
UsuarioAdministrador · UsuarioAdminPermissao · Unidade · Pedido · EventoLog · MovimentacaoEstoque · Pagamento ·
RecuperacaoSenha · ResgateFidelidade

</details>

## 🔌 7. API

**Envelope único:**

```jsonc
// sucesso
{ "ok": true, "data": { ... } }
// erro
{ "ok": false, "error": "mensagem", "codigo": "ERR-4000", "proibido": false, "timestamp": "..." }  // + "referencia" no 500
```

| Código | Significado |
|---|---|
| `ERR-4000` | regra de negócio |
| `ERR-4001` | corpo em formato errado |
| `ERR-4010` | sem sessão |
| `ERR-4030` | sem permissão |
| `ERR-4040` / `ERR-4050` | rota ou método inexistente |
| `ERR-4990` | cancelada por quem chamou |
| `ERR-5001` | inesperado (com referência no log) |

| Grupo | Rotas principais |
|---|---|
| 👤 **Cliente** `/api/cliente`<br><sub>header `X-LanePets-Client`</sub> | `cadastro` · `login` · `logout` · `senha/esqueci\|redefinir` · `google/config` · `google` · `conta` (+ `/avisos`, `/email`, `/senha`) · `pets` · `catalogo` · `horarios` · `agendamentos` (`servicoIds`, até 6) · `pedidos` · **`pedidos/lote`** · `seguros` · `avaliacoes` |
| 🌐 **Público** `/api/public` | `servicos` · `produtos` · `depoimentos` · `seguros` · `seguros/solicitacoes` · `unidades` · `numeros` |
| 🛠️ **Painel** `/api/admin`<br><sub>`?token=`</sub> | `estado?colecoes=` · `sync/{colecao}` · `resumo` · `relatorio` (+ `excel`) · `clientes` · `pets` · `agenda` · `produtos` · `estoque/movimentacoes` · `entradas-saidas` · `pedidos` · `importar` · `unidades` · `pagamentos` · `integridade` · `eventos` · `usuarios` · `notificacoes` · `senha/esqueci\|redefinir` |
| 🔑 **Auth** `/api` | `health` (`demo`, `visitante`, `google`) · `login` · `login/visitante` · `admin/me` · `logout` |

## 🔄 8. Fluxos principais

### Login com Google

```mermaid
sequenceDiagram
    autonumber
    actor T as Tutor
    participant N as Navegador
    participant G as Google
    participant A as LanePets API
    T->>N: clica em "Entrar com Google"
    N->>G: Google Identity Services
    G-->>N: ID token (JWT)
    N->>A: POST /api/cliente/google { credential }
    A->>A: confere RS256 (JWKS em cache), iss, aud, exp, e-mail verificado
    alt conta achada pelo sub
        A-->>N: sessão
    else e-mail já tem conta com senha
        A-->>N: { vincular: true } → pede a senha uma vez
    else cliente novo
        A-->>N: conta criada + "complete seu cadastro"
    end
```

### Sacola da loja

```mermaid
sequenceDiagram
    autonumber
    participant N as Área do cliente
    participant A as POST /pedidos/lote
    participant S as PedidosService
    participant DB as SQLite
    N->>A: itens[] + pagamento + unidade
    A->>S: CriarVariosDoClienteAsync
    S->>S: exige telefone · soma repetidos · até 20 itens · confere estoque
    S->>DB: 1 SaveChanges: N pedidos + N pagamentos + baixa no livro
    DB-->>N: tudo gravado (ou nada)
```

- **Painel antigo × novo:** as telas novas leem por endpoints próprios, paginados no servidor. A gravação continua por `POST /api/admin/sync/{colecao}` com o registro inteiro (`FormatoPainel` é a única tradução banco → painel).
- **Pagamento:** um por agendamento ou pedido, e um por mês no seguro (chave `(Origem, OrigemId, Competencia)`). A situação é calculada na leitura.
- **Log de eventos:** gravado depois do `SaveChanges`; nunca derruba a operação e nunca guarda segredo.

## 🧪 9. Testes

`WebApplicationFactory<Program>` sobe a **aplicação real** contra um banco temporário vazio. O cenário é montado pela
API e o banco só é lido.

| Pasta | O que cobre |
|---|---|
| `Infra/` | `LanePetsApp` (zera SMTP, visitante e Google) · `ColecaoApi` · `Api` (`AdminCom`, `FuncionarioCom`) · `Cenarios` · `GoogleFalso` |
| `Autenticacao/` | login do painel e do cliente, Google (config, token, conta, API, regressão), recuperação de senha, hospedagem, visitante |
| `Operacao/` | agendamentos (inclui vários serviços), pedidos (inclui sacola), pagamentos, seguro e cobrança mensal, avaliações, avisos, fidelidade, confirmação automática |
| `Painel/` | clientes, produtos e estoque, agenda, paginação, dashboard e relatório (Excel), funcionário por unidade, importação, notificações, unidades |

> [!TIP]
> **242 testes** (29/09). Nenhum teste fala com a internet nem manda e-mail real.

## 🐳 10. Hospedagem

```mermaid
flowchart LR
    R[GitHub<br>master] --> B[Railway<br>build pelo Dockerfile]
    B --> I[sdk:10.0<br>restore + publish] --> F[aspnet:10.0<br>porta 8080 / PORT]
    F --> V[(Volume<br>/app/data)]
    ENV[Variáveis de ambiente<br>segredos] --> F
```

- **Imagem:** `ASPNETCORE_ENVIRONMENT=Production` e `TZ=America/Sao_Paulo`.
- **`.dockerignore`:** tira testes, bancos, scripts, `Claude outputs/` e `appsettings.Development.json`.
- **Segredos, só por variável de ambiente:**
  - `LanePets__AdminSenhaInicial`
  - `LanePets__FinancePassword`
  - `LanePets__Google__ClientId`
  - `LanePets__Visitante`
  - `LanePets__Email__*`

> [!WARNING]
> Sem `LanePets:Demo=true`, o app **não sobe** com a senha de exemplo.
