# LanePets — Tarefas

> Ordem de trabalho definida pelo Fabrício em 28/09/2026. Atualizado em 29/09/2026.
> Ritmo: **uma etapa por vez, com validação dele antes de continuar.**

## Resumo

| # | Tarefa | Status |
|---|---|---|
| — | Desenvolvimento principal (20 itens do roadmap + extras) | ✅ Concluído (173 testes) |
| 1 | Login com Google | ✅ Concluída (228 testes) |
| 2 | Testar o Login com Google | ✅ Concluída (234 testes) |
| ➕ | Rodada de melhorias das telas | ✅ Concluída (242 testes, commit e push feitos) |
| 3 | **Deploy no Railway** | 🔴 **Próxima** |
| 4 | Teste completo em produção | Pendente |
| 5 | Atualizar README | Pendente |
| 6 | Atualizar LinkedIn | Pendente |
| 7 | Melhorias futuras | Depois do sistema no ar |
| 8 | Gateway de pagamento | Futuro — **não fazer agora** |

## ✅ Concluído

- [x] **Desenvolvimento principal:**
  - Autenticação e permissões (perfil Funcionário), dados do cliente, gestão dos pets (ficha, foto), agendamento completo.
  - Unidades, produtos e estoque em livro, pagamentos, dashboard, imagens em data URI.
  - Arquitetura em Services, testes e CI, validações, erros `ERR-xxxx`, log de eventos.
  - Busca e filtros, responsividade, design system, banco (backup, índices, integridade).
  - Cobrança mensal do seguro, avaliação só com login, paginação no servidor.
  - E-mails ao tutor + lembrete da véspera, recuperação de senha (cliente e painel), confirmação automática por unidade.
  - Sino de pendências, pronto para hospedar, modo visitante, relatório Excel/PDF, cartão fidelidade.
- [x] **Tarefa 1 — Login com Google:** configuração → validação do ID token → primeiro acesso e vinculação → endpoint → tela → preparação para o Railway.
- [x] **Tarefa 2 — Testes do Google:** 6 testes de regressão + teste à mão com a conta real + regra do telefone obrigatório.
- [x] **Rodada de telas:**
  - Backend: vários serviços por agendamento (até 6) e sacola da loja (`pedidos/lote`).
  - Site: home redesenhada, unidades e contato juntos.
  - Área do cliente: Avaliações, Agendar, Loja, Seguro, Pagamentos, Meus Pedidos, Meus Agendamentos e Histórico.

## 🔴 Tarefa 3 — Deploy no Railway

- [ ] Conta e projeto no Railway ligados ao repositório `Miguel33-Dev/lanpets` (branch `master`), build pelo Dockerfile.
- [ ] Volume montado em `/app/data`: banco, backups, e-mails `.txt` e logs.
- [ ] Variáveis de ambiente:
  - `LanePets__AdminSenhaInicial` e `LanePets__FinancePassword`: 8+ caracteres, com letra e número, e não pode ser `123456`.
  - `LanePets__Visitante=true`: só se for vitrine com dados de exemplo.
  - `LanePets__Google__ClientId`.
  - SMTP (`LanePets__Email__*`): opcional.
  - `LanePets__UrlPublica`: opcional.
- [ ] Google Cloud:
  - Adicionar a origem `https://<app>.up.railway.app` no cliente "LanePets Web".
  - **Publicar o app** em Público-alvo.
  - Conferir `/api/health` → `google: true`.
- [ ] Domínio.
- [ ] Registrar o link no README e no `memory.md`.

## Tarefa 4 — Teste completo em produção

- [ ] **Cliente:** Login Google → telefone + pet → agendamento com vários serviços → unidade → horário → pagamento → sacola com vários produtos.
- [ ] **Equipe:** confirma → atende → conclui o agendamento; confirma e entrega os pedidos.
- [ ] **Financeiro:** pagamentos → Dashboard → Relatório (Excel e PDF).

## Tarefa 5 — Atualizar README

- [ ] **Conteúdo:** descrição, funcionalidades (vários serviços e sacola), stack e arquitetura, segurança, Login Google.
- [ ] **Números:** total de testes (242).
- [ ] **Uso:** Docker e Railway, como executar, estrutura do projeto.
- [ ] **Extras:** link da aplicação e prints das telas novas.

## Tarefa 6 — Atualizar LinkedIn

- [ ] **Conteúdo:** evolução PHP → Laravel → .NET 10, arquitetura, testes, segurança, área do cliente, painel, Google, deploy.
- [ ] **Material:** atualizar os números no `lanepets-linkedin-v2.png`, no `lanepets-linkedin-textos.md` e no `lanepets-video.mp4`, e usar as telas redesenhadas.

## Tarefa 7 — Melhorias futuras

- [ ] **E-mail:** outra conta de envio (hoje o e-mail para o próprio remetente do Gmail cai em Enviados).
- [ ] **Banco:** avaliar o crescimento e tirar Cadastro e Entradas e Saídas do LaneStore.
- [ ] **Notificações:** internas e no site.
- [ ] **Conta só-Google:** opção "Criar senha" direto na Minha Conta.
- [ ] **Agendamento:** filtrar os serviços pelo porte do pet e redesenhar a etapa Revisão (7).
- [ ] **Cadastros no painel:** unificar "Acessório"/"Acessórios" e os serviços com nomes quase iguais.
- [ ] **Loja:** fotos nos produtos.
- [ ] **Google Cloud:** excluir a chave secreta do cliente OAuth (não é usada).

## Tarefa 8 — Gateway de pagamento (não fazer agora)

Mercado Pago ou Stripe em modo de teste, uma etapa por vez: preparação → sandbox → integração → webhook →
idempotência → estados → testes → produção.
