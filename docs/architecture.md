# LanePets — Arquitetura

> Atualizado em 29/09/2026. Requisitos: `PRD.md` · Regras: `rules.md`.

## 1. Visão geral

Uma única aplicação **ASP.NET Core (.NET 10)** serve o frontend (HTML, CSS e JS estáticos de `wwwroot/`) e a API
REST (`/api/...`), além de um hub SignalR (`/hubs/lanepets`) para o tempo real do painel.

```
Navegador ──► ASP.NET Core ──► Controllers ──► Services (regra de negócio) ──► EF Core ──► SQLite (lanepets.db)
   │              │                                   │
   │              ├─ estáticos (wwwroot)              ├─ EmailService (SMTP ou .txt)
   │              └─ SignalR /hubs/lanepets           └─ Workers em segundo plano
   └─ Google Identity Services (só no login com Google)
```

## 2. Stack

| Camada | Tecnologia |
|---|---|
| Backend | .NET 10 (`net10.0`, `RootNamespace: LanePets`), ASP.NET Core |
| Banco | SQLite + EF Core 10.0.11 (`Microsoft.EntityFrameworkCore.Sqlite`) |
| Senhas | BCrypt.Net-Next 4.2.0 |
| Tempo real | SignalR |
| Frontend | HTML + CSS + JavaScript puro, sem framework |
| Login social | Google Identity Services + validação RS256/JWKS própria, **sem pacote NuGet** |
| Planilha | `PlanilhaXlsx` própria (ZIP + Office Open XML), **sem pacote** |
| Testes | xUnit 2.9.3 + `Microsoft.AspNetCore.Mvc.Testing` 10 |
| CI | GitHub Actions "LanePets CI" (`.github/workflows/dotnet-desktop.yml`) |
| Deploy | Docker (Dockerfile na raiz) → Railway (Tarefa 3) |

## 3. Estrutura de pastas

```
Controllers/  AdminStore · AdminSync · AgendaPainel · ApiControllerBase (envelope/erro) · Auth · ClientPortal · Data ·
              Eventos · Integridade · Notificacoes · Pagamentos · Public · Unidades · UsuariosAdmin
Services/     regra de negócio (ver §5)
Data/         LanePetsDbContext + seed/*.csv (produtos e 43 serviços)
Models/       Entities.cs
DTOs/         contratos conferidos por tela/script (records)
Middleware/   AdminAreaGuard (bloqueia HTML do painel sem sessão) · ErroGlobal
Hubs/         hub SignalR
wwwroot/      20 páginas HTML, design-system.css, publico.css, conta.css, auth.css, ui-kit.js, js/, css/, lib/
tests/        LanePets.Tests (Infra · Autenticacao · Operacao · Painel)
raiz/         Dockerfile · INICIAR-LANEPETS.ps1 · TESTAR-LANEPETS.ps1 · testar-*.ps1 · README.md
```

## 4. Pipeline HTTP

`UseForwardedHeaders` (proxy, antes de tudo) → HSTS/HTTPS fora do Development → `ErroGlobalMiddleware` →
`UseStatusCodePages` (404/405 de `/api` no envelope) → CORS (`LanePets:CorsOrigins`) → Session →
`AdminAreaGuardMiddleware` → arquivos estáticos (padrão `cliente.html`) → routing → controllers → hub. O `Program.cs` termina com `public partial class Program { }`, usado pelos testes. Em hospedagem, a
porta vem da variável `PORT`.

## 5. Camadas

- **Controller:** só lê a requisição, checa a permissão, chama o Service e monta a resposta. Não tem regra de negócio.
- **Service:** regra de negócio usando o `DbContext` direto (**sem Repositories**). Há três tipos:
  - Cálculo puro = classe `static` (`IndicadoresFinanceiros`, `MensalidadesSeguro`, `StatusAgendamento`, `ListaPaginada`, `Validacao`).
  - Estado da requisição = Service scoped (`SyncPainelService`, `ClientesPainelService`, `AgendaPainelService`).
  - Singletons: `EmailService`, `EventosService` (com DbContext próprio), `GoogleTokenService`.
- **Workers:** `CobrancaMensalSeguroWorker` (de hora em hora), `LembreteAgendamentoWorker` (de hora em hora) e `RetencaoLogWorker` (diário).

**Principais Services:**
- **Contas e acesso:** Session · Permissao · ContaCliente · ContaGoogle · GoogleLogin · GoogleToken · RecuperacaoSenha · Visitante · Hospedagem.
- **Operação:** AgendamentosCliente · Pedidos · Estoque · Pagamentos · SegurosCliente · Avaliacoes · Fidelidade · AvisosCliente · UnidadesRegras · ResponsaveisAgendamento.
- **Painel:** SyncPainel · ClientesPainel · AgendaPainel · FormatoPainel · NotificacoesPainel.
- **Plataforma:** Banco · Integridade · Importacao · Eventos · RetencaoEventos · Email · PlanilhaXlsx · Seed · DatabaseBootstrap · Normalizador · Csv · Realtime.

## 6. Banco de dados

- **Um banco só (`lanepets.db`), fora da pasta do código** (o OneDrive corrompia o SQLite):
  - Windows: `%LOCALAPPDATA%\LanePets\`
  - Linux: `~/.local/share/LanePets/`
  - Docker: `/app/data/`
  - Testes: `%TEMP%\LanePets.Tests\<id>\`

  `ConnectionStrings:LanePetsConnection` tem prioridade.
- **Sem EF Migrations:** `EnsureCreated()` + `GarantirColunaAsync` / `CREATE TABLE|INDEX IF NOT EXISTS`. Coluna nova é sempre opcional. **Sem foreign key**, com índices e varredura de integridade.
- **Subida:** WAL + `quick_check` → backup `VACUUM INTO` (10 últimos) → seed → unidades → 26 índices → reconciliação de pagamentos → retenção do Log.
- **Entidades:** Pet · Cliente · Agendamento · Servico · Produto · EntradaSaida · Pacote · Configuracao · Auditoria* ·
  ReconciliacaoMigracao · Depoimento · PlanoSeguro · SolicitacaoSeguro · UsuarioCliente (`GoogleSub`) ·
  UsuarioAdministrador · UsuarioAdminPermissao · Unidade · Pedido · EventoLog · MovimentacaoEstoque · Pagamento ·
  RecuperacaoSenha · ResgateFidelidade.

## 7. API

- **Envelope único:**
  - Sucesso: `{ ok, data }`.
  - Erro: `{ ok:false, error, codigo, proibido, timestamp }` (+ `referencia` no 500).
  - Códigos de erro: `ERR-4000` negócio · `4001` formato · `4010` sessão · `4030` permissão · `4040/4050` rota · `4990` cancelado · `5001` inesperado.
- **Cliente** `/api/cliente` (header `X-LanePets-Client`):
  - Acesso: `cadastro` · `login` · `logout` · `senha/esqueci|redefinir` · `google/config` · `google`.
  - Conta: `conta` (+ `/avisos`, `/email`, `/senha`) · `pets` · `catalogo` · `horarios`.
  - Operação:
    - `agendamentos` aceita `servicoIds`, até 6.
    - `pedidos` · **`pedidos/lote`** (sacola) · `seguros` · `avaliacoes`.
- **Público** `/api/public`: `servicos` · `produtos` · `depoimentos` · `seguros` · `seguros/solicitacoes` · `unidades` · `numeros`.
- **Painel** `/api/admin` (`?token=`):
  - Estado e sync: `estado?colecoes=` · `sync/{colecao}` · `importar`.
  - Indicadores e relatório: `resumo` · `relatorio` (+ `excel`).
  - Operação: `clientes` · `pets` · `agenda` · `produtos` · `estoque/movimentacoes` · `entradas-saidas` · `pedidos` · `unidades` · `pagamentos`.
  - Administração: `integridade` · `eventos` · `usuarios` · `notificacoes` · `senha/esqueci|redefinir`.
- **Auth** `/api`: `health` (com `demo`, `visitante` e `google`) · `login` · `login/visitante` · `admin/me` · `logout`.

## 8. Fluxos principais

- **Painel antigo × novo:** as telas novas leem por endpoints próprios, paginados no servidor (resposta `{ total, offset, limite, temMais, itens }`, com resumo da base inteira). A gravação continua por `POST /api/admin/sync/{colecao}` com o registro inteiro (`FormatoPainel` é a única tradução banco → painel). As telas antigas usam `LaneStore` e declaram `window.LANE_COLECOES`.
- **Login com Google:** o navegador recebe o ID token e o backend confere RS256 pela chave do `kid` (JWKS em cache), `iss`, `aud`, `exp` e o e-mail verificado. A conta é achada pelo `sub`.
- **Sacola:** `pedidos/lote` grava tudo ou nada num `SaveChanges`. No banco vira **um pedido e um pagamento por produto**.
- **Pagamento:** um por agendamento ou pedido, e um por mês no seguro (chave `(Origem, OrigemId, Competencia)`). A situação é calculada na leitura.
- **Log de eventos:** toda ação relevante vira `EventoLog`, gravado depois do `SaveChanges`. Nunca derruba a operação e nunca guarda segredo.

## 9. Testes

`WebApplicationFactory<Program>` sobe a **aplicação real** contra um banco temporário vazio. O cenário é montado pela
API e o banco só é lido.

Infra de teste:
- `LanePetsApp`: zera o SMTP, força `Visitante=false` e zera o Google.
- `ColecaoApi`.
- `Api`: `AdminCom`, `FuncionarioCom`.
- `Cenarios`.
- `GoogleFalso`: aplicação derivada com `ConfigureTestServices`.

São **242 testes** (29/09), e nenhum teste fala com a internet.

## 10. Hospedagem

Dockerfile em multi-stage (sdk:10.0 → aspnet:10.0), `ASPNETCORE_ENVIRONMENT=Production`, porta 8080 (ou `PORT`),
`TZ=America/Sao_Paulo` e dados em `/app/data` (volume). O `.dockerignore` tira testes, bancos, scripts, `Claude outputs/`
e `appsettings.Development.json`.

Segredos só por variável de ambiente:
- `LanePets__AdminSenhaInicial`
- `LanePets__FinancePassword`
- `LanePets__Google__ClientId`
- `LanePets__Visitante`
- `LanePets__Email__*`

Sem `LanePets:Demo=true`, o app não sobe com a senha de exemplo.
