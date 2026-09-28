# LanePets — Regras do projeto

> Valem para qualquer pessoa ou IA que mexer no código. Atualizado em 29/09/2026. Por que cada regra existe:
> `memory.md` (incidentes) e `architecture.md`.

## 1. Forma de trabalhar

- **Uma etapa por vez**, com validação do Fabrício antes de continuar. Quando ele disser "começar a tarefa N", preparar só a primeira etapa.
- Decisão de regra de negócio que aparecer no meio do trabalho: **perguntar antes** de implementar.
- **Commit e push são sempre do Fabrício.** A IA não roda git na pasta do projeto (deixa `.git/index.lock`).
- A IA **nunca digita senha** nem coloca segredo em arquivo versionado.
- Ao concluir uma etapa, atualizar `tasks.md` e `memory.md`.

## 2. Backend

1. **Sem EF Migrations.** Usar `EnsureCreated()` + `GarantirColunaAsync` / `CREATE ... IF NOT EXISTS`.
2. **Coluna nova sempre opcional**, que nasce vazia. Sem foreign key.
3. **Envelope único** `{ ok, data }` / `{ ok:false, error, codigo, ... }`.
4. **Autorização é do backend:** 403, nunca 200 com dados. O frontend só esconde.
5. **A identidade vem da sessão**, nunca do corpo da requisição. `ClienteId` não existe no contrato de entrada. Toda consulta do cliente filtra `ClienteId == cliente.Id` na mesma query.
6. **Projeção explícita** nas respostas: campo interno não vaza.
7. **Regra de negócio em `Services/`**, com o `DbContext` direto. Sem Repositories. O controller não tem regra.
8. **Validação num lugar só**, antes de tocar na entidade, com mensagem em português (`Validacao`, `PetFicha`, `ImagemDataUri`...).
9. **Erro de negócio:** `throw new Exception("texto")` (tipo base exato) ou `ValidacaoException` → 400 `ERR-4000`. Qualquer outro tipo → 500 `ERR-5001` genérico + referência. Texto técnico só no log.
10. **Nenhum `catch` engole erro** em silêncio.
11. **Cancelar ≠ excluir.** Unidade só é desativada, e nunca a última ativa.
12. **Nada gravado antes da confirmação final** nos wizards.
13. **Status do agendamento** só via `StatusAgendamento` (Solicitado · Confirmado · Em andamento · Concluído · Cancelado). O cliente cancela só Solicitado ou Confirmado.
14. **O saldo do estoque só muda pelo `EstoqueService`** (livro de movimentações). Editar o produto não mexe no saldo.
15. **Pagamento é entidade:** as transições são as de `PagamentosService.PodeIr`. No seguro, um por mês. A situação é calculada, nunca gravada.
16. **Dinheiro só com `pagamentos:visualizar`**: sem a permissão, `null`, nunca R$ 0,00.
17. **Perfil mais restrito = teto no backend** (`Pode()` + `VeUnidade()`). O funcionário vê só a unidade dele. Falha fechada.
18. **Unidade gravada sempre pelo id.** Comparar por `Normalizador.IdUnidade`.
19. **Log de eventos:** toda ação relevante registra um evento depois do `SaveChanges`. Nunca senha, token, cartão ou telefone no detalhe.
20. **E-mail nunca derruba a operação.** Os avisos opcionais respeitam `Cliente.ReceberAvisos`.
21. **Segredo de uso único só como hash**, com comparação em tempo constante, validade curta e tentativas contadas.
22. **Lista que cresce pagina no servidor** (`ListaPaginada`), com resumo da base inteira.
23. **Agendar, pedir e contratar seguro exigem telefone** (`ExigirTelefone`).
24. **Sacola = tudo ou nada**, até 20 itens. **Agendamento = até 6 serviços.**
25. **Produção não aceita senha de exemplo.** Segredo só por variável de ambiente, dados em `/app/data`.
26. **Nenhum pacote NuGet novo sem decisão** (o Google e o Excel foram feitos sem pacote).

## 3. Frontend

1. **Nenhum número, selo ou frase inventada na tela.** Sem dado, o cartão some ou mostra um estado vazio honesto.
2. **Editou script ou CSS compartilhado → subir o `?v=` em toda página que o carrega.**
3. **Dado do sistema nunca no `localStorage`.** Só preferência de tela e o token de sessão do cliente.
4. **Nenhuma página redefine componente do design system.** A variação nova entra no `design-system.css`.
5. **Nome de unidade nunca escrito à mão:** usar `data-unidades` + `js/marca-unidades.js`.
6. **Ícone em SVG inline**, nunca emoji.
7. `[hidden] { display:none }` ao lado de toda classe que define `display`.
8. Filho de grade com conteúdo largo = `min-width:0`. Fileira com rolagem lateral no celular = `width:0; min-width:100%`.
9. `<footer>` dentro de card herda o rodapé do site: usar `<div>`.
10. **Data sempre local** (nada de `toISOString()` para "hoje").
11. **Localizar registro por id**, nunca por referência guardada.
12. **Toda tela é testada a 330, 390, 860 e 1440 px** antes da entrega, sem rolagem lateral e sem erro de JS.
13. A home usa escopo `body.home`. Cada tela nova da área do cliente tem prefixo próprio (`.agx-`, `.hsx-`, `.pdx-`...).

## 4. Testes

- Mudança de regra de negócio → **teste junto**, em `tests/LanePets.Tests`.
- A aplicação real sobe com banco temporário. O cenário é montado **pela API** e o banco só é lido.
- 403 provado com `AdminCom` / `FuncionarioCom`.
- Teste que mexe em estado global devolve tudo no `finally`.
- Serviço externo é trocado por um falso (`GoogleFalso`). Nenhum teste fala com a internet nem manda e-mail real.
- Nunca mexer no `admin@gmail.com` em teste: cada teste cria o próprio admin.
- **Nunca testar contra o banco real do Fabrício.**

## 5. Execução

- **Rodar sempre de dentro da pasta do projeto:** `./INICIAR-LANEPETS.ps1` e `./TESTAR-LANEPETS.ps1`. Se o PowerShell bloquear o script: `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`.
- **Mexeu em `.cs`:** Ctrl+C e iniciar de novo. **Mexeu em HTML, CSS ou JS:** Ctrl+F5.
- **Nenhum `.cs` solto fora de `tests/`**: o `LanePets.csproj` compila tudo o que acha.
- Antes do commit, `git status`: nada de `.fuse_hidden*`, `*.db` ou `Claude outputs/`.
- Nunca `git push --force`.
