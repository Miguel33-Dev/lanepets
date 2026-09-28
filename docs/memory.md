# LanePets — Memória do projeto

> O que já foi decidido, onde paramos e o que já deu errado, para não repetir. Atualizado em 29/09/2026.
> Ler antes de começar qualquer trabalho. Tarefas: `tasks.md` · Regras: `rules.md`.

## 1. Onde paramos

- **Estado:** desenvolvimento principal, Login com Google e rodada de melhorias das telas concluídos. **242 testes verdes**, commit e push feitos, CI verde.
- **Próxima:** **Tarefa 3 — Deploy no Railway** (ainda não feito).
- **Repositório:** `https://github.com/Miguel33-Dev/lanpets.git`, branch `master`.
- **Pasta local:** `C:\Users\fabri\OneDrive\Documentos\Novo Projeto\LanePetsCSharp-area-publica-e-seguros\LanePetsCSharp`.

## 2. Histórico do projeto

- **Linha do tempo:**
  - Começou em PHP.
  - Passou por Laravel/SQLite MVC.
  - Desde 16/09/2026 é **ASP.NET Core .NET 10**, a única versão válida.
- **24–26/09:** 20 itens do roadmap (permissões, pets, agenda, estoque, pagamentos, testes, log, design system...).
- **26–29/09:**
  - Cobrança mensal do seguro, paginação no servidor.
  - E-mails, recuperação de senha, confirmação automática.
  - Sino de pendências, hospedagem, visitante, Excel/PDF, fidelidade → 173 testes.
- **28/09:** Login com Google (228) e testes de regressão (234).
- **28–29/09:** rodada de telas + vários serviços + sacola (242).

## 3. Decisões do Fabrício (valem até ele mudar)

- **Login com Google:**
  - Um e-mail que já tem conta com senha → pede a senha **uma vez** para vincular.
  - Cliente novo → conta criada direto, sem senha e sem pet, completada depois.
  - **Sem pacote NuGet novo.**
- **Telefone obrigatório** para agendar, pedir e contratar seguro. O pet pode vir antes do telefone e a avaliação não exige.
- **Seguro:**
  - A mensalidade é gerada sozinha.
  - Tolerância de **10 dias**, depois vira **Inadimplente** (sem cancelar).
  - Um `Pagamento` por mês.
- **Estoque único** (não por unidade). **Sem foreign keys. Sem Repositories.**
- **Tela Clientes** inteira fora do estado antigo.
- **Loja com sacola:** vários produtos numa compra, gravados como um pedido por produto.
- **Agendamento:** até 6 serviços.
- **Unidades e contato** numa seção só no site.
- **Cartão fidelidade:** 10 selos = 1 banho grátis (configurável).
- **Gateway de pagamento:** só no fim, e **não agora**.

## 4. Configuração e ambiente

- **Executar:**
  - `./INICIAR-LANEPETS.ps1` → `http://localhost:5180`.
  - `./TESTAR-LANEPETS.ps1` → 242 testes.
  - Se o PowerShell bloquear o script: `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`.
- **Painel local:** `admin@gmail.com` / `123456`. Só em desenvolvimento: em produção o app recusa essa senha.
- **Banco real:** `%LOCALAPPDATA%\LanePets\lanepets.db`, fora do OneDrive. O `lanepets.db` dentro da pasta é uma cópia antiga de 22/09.
- **`appsettings.Development.json`** (fora do git e da imagem): SMTP do Gmail (senha de app colocada por ele), `Visitante: true` e o Client ID do Google. Os testes ignoram os três.
- **Google Cloud:**
  - Projeto "LanePets", Google Auth Platform com público Externo, **em teste** (ele é usuário de teste).
  - Cliente OAuth "LanePets Web" com as origens `http://localhost:5180` e `http://localhost`.
  - A chave secreta não é usada e deve ser excluída.
- **Erro "ERR-5001 · Ref. XXXX":** procurar a referência em `%LOCALAPPDATA%\LanePets\logs\erros-AAAA-MM-DD.log`.

## 5. Incidentes (não repetir)

| Sintoma | Causa | Lição |
|---|---|---|
| "Recurso não encontrado na API." / API nova com 404 ou 405 | App antigo aberto (DLL velha) | Mudou `.cs` → Ctrl+C e iniciar de novo |
| `database disk image is malformed` | SQLite dentro do OneDrive | Banco fora de pasta sincronizada |
| Tela nova quebrada (`... is not a function`) | JS antigo em cache | Subir o `?v=` em todas as páginas |
| Data errada depois das 21h | `toISOString()` (UTC) | Data local |
| Hora do pedido 3 h adiantada | `DateTime` UTC sem fuso | `SpecifyKind(Utc).ToString("O")` |
| Cliente e painel marcavam o mesmo horário | Unidade pelo nome × pelo id | Comparar por `Normalizador.IdUnidade` |
| Editar preço desfazia baixas de estoque | O sync regravava o saldo | Saldo só pelo livro |
| Receita aparecia para quem não podia ver | Regra do dinheiro só em parte dos endpoints | `null` sem `pagamentos:visualizar` em todo endpoint |
| Foto do pet com 0 px | `flex-start` + `absolute` | `align-items: stretch` |
| Página mais larga que o celular | Filho de grade sem `min-width:0`; fileira com rolagem | `min-width:0`; `width:0; min-width:100%` |
| Cartão escondido aparecendo vazio | `display` da classe venceu o `[hidden]` | `[hidden]{display:none}` |
| Aviso de telefone sem regra no backend | A tela prometia uma regra inexistente | Regra no backend; texto = regra real |
| Selo "Mais escolhido" / "sem carência escondida" | Texto sem dado que sustente | Fora: só o que o sistema sabe |
| `.git/index.lock` sobrando | IA rodou git na pasta | IA não roda git; commit é do Fabrício |
| Build com 64 erros | `*Tests.cs` em `Claude outputs/` | `DefaultItemExcludes`; teste só em `tests/` |
| Push recusado | README editado no GitHub | `git diff --stat master...origin/master`; nunca `--force` |
| Script `.ps1` bloqueado | Política do PowerShell | `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass` |
| `ObjectDisposedException` | Disco C: cheio | Conferir o espaço antes de caçar bug |

## 6. Como a IA confere o trabalho

- **Compilação:** a IA compila o app num container próprio (csc + DLLs do `bin`), com banco temporário e `LanePets__Demo=true`, e confere a API e as telas com Playwright a 1440 e 390 px.
- **O que não roda lá:**
  - Os testes xUnit oficiais rodam só na máquina dele.
  - O Google real só funciona na máquina dele.
- **Nunca** contra o banco real dele.
- Para parar o app no container: `kill $(pgrep -x dotnet)`, nunca `pkill -f`.
