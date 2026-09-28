# LanePets — PRD (Documento de Requisitos do Produto)

> Projeto de faculdade e pessoal de **Fabrício Miguel da Silva**. Atualizado em 29/09/2026.
> Arquitetura: `architecture.md` · Regras de desenvolvimento: `rules.md` · Visual: `design.md` ·
> Tarefas: `tasks.md` · Memória do projeto: `memory.md`.

## 1. Visão

O LanePets é um sistema de gestão para petshop com **três públicos numa aplicação só**:

1. **Site público:** o tutor conhece os serviços, os produtos, o seguro pet, as unidades e as avaliações.
2. **Área do cliente:** o tutor cadastra os pets, agenda, compra na lojinha, acompanha pagamentos, contrata o seguro e avalia.
3. **Painel administrativo:** a equipe opera agenda, clientes, estoque, pedidos, financeiro e usuários, com permissões por módulo e por unidade.

**Unidades atuais:** Caieiras e Franco da Rocha, que vêm do seed, e Jundiaí, cadastrada pelo painel. As unidades vêm
sempre do banco e nunca são escritas à mão.

## 2. Problema que resolve

- Agendamento por telefone e caderno, sem controle de horário por unidade.
- Estoque sem histórico: não se sabe por que um saldo mudou.
- Financeiro espalhado: não há um lugar só para pagamentos, reembolsos e mensalidades do seguro.
- O tutor não tem onde ver o histórico do pet, os pedidos e os pagamentos.

## 3. Personas

| Persona | Quer | Onde |
|---|---|---|
| Tutor (cliente) | agendar rápido, comprar e retirar na unidade, ver o histórico do pet | site + área do cliente |
| Administrador Geral | ver tudo e controlar usuários, log e integridade | painel |
| Administrador comum | operar os módulos que recebeu | painel |
| Funcionário | atender a agenda **da unidade dele** | painel (teto de permissões) |
| Visitante (vitrine) | conhecer o painel sem gravar nada | painel em modo leitura |

## 4. Funcionalidades — Site público (`cliente.html`)

- **Hero:**
  - Vantagens reais: agendar pelo site, leva e traz opcional, comprar e retirar na unidade.
  - Cartões com **nota média** e **"seguro a partir de R$ X/mês"**, que só aparecem quando há dado.
- **Faixa de números reais:** pets atendidos, avaliação, serviços, produtos e unidades (`GET /api/public/numeros`). Sem dado, o cartão some.
- **Serviços agrupados por nome:** tabela de preço por porte, em abas por categoria (Banho, Banho e tosa, Tratamentos, Cuidados extras, Pacotes).
- **Lojinha:** busca, categorias, ordenação, selo de estoque e 8 produtos por vez com "ver mais".
- **Seguro pet:** planos vindos do banco e pedido de contato.
- **Avaliações moderadas.** Para avaliar é preciso estar logado.
- **Unidades e contato numa seção:** telefone clicável e "Como chegar" só quando há endereço de verdade.

## 5. Funcionalidades — Área do cliente (`minha-conta.html`)

- **Conta:**
  - Cadastro com e-mail e senha, ou **Login com Google**: vincula a conta que já existe pedindo a senha uma vez, ou cria uma conta nova.
  - Recuperação de senha por **código de 6 dígitos por e-mail**.
  - Trocar e-mail ou senha exige a senha atual.
  - Liga e desliga os avisos por e-mail.
- **Telefone obrigatório** para agendar, pedir na loja e contratar o seguro.
- **Meus Pets:**
  - Ficha completa: sexo, nascimento, peso, cor, porte, foto, observações e necessidades.
  - Wizard em 4 etapas, até 20 pets por conta.
- **Agendar** (wizard em 7 etapas: pet → serviço → unidade → data e hora → transporte → pagamento → revisão):
  - **Até 6 serviços por agendamento.**
  - A grade de horários respeita a capacidade da unidade.
  - Busca e/ou entrega custam R$ 15 cada.
  - Confirmação automática quando a unidade tem essa opção ligada.
- **Meus Agendamentos:** destaque do próximo atendimento, trilha do status, lista por mês, filtros e busca. O cliente cancela só enquanto está Solicitado ou Confirmado.
- **Histórico de serviços:** linha do tempo dos atendimentos concluídos, por pet, com o serviço mais feito e o total.
- **Produtos com sacola:** vários produtos numa compra só, tudo ou nada, até 20 itens. Retira na unidade e paga na retirada.
- **Meus Pedidos:** a compra da sacola aparece num cartão, com linha do tempo Pedido feito → Confirmado → Retirado. O cliente cancela enquanto está Pendente.
- **Pagamentos:** extrato só de leitura, com reembolso em análise.
- **Seguro Pet:** contratação em etapas, mensalidade de cada mês e situação da cobrança (Em dia / Aguardando / Inadimplente).
- **Cartão fidelidade:** cada atendimento concluído vale 1 selo, e 10 selos valem 1 prêmio (configurável).
- **Avaliações:** só com login, do próprio pet, e passam por moderação.

## 6. Funcionalidades — Painel administrativo

- **Dashboard:** 8 indicadores com filtro de unidade. Os valores em dinheiro só aparecem com permissão de pagamentos.
- **Agendamentos:**
  - Lista paginada no servidor e calendário de dia, semana e mês.
  - Status: Solicitado · Confirmado · Em andamento · Concluído · Cancelado.
  - Responsável: funcionário da mesma unidade.
  - Wizard de criação.
- **Clientes e pets:** paginados no servidor, com o detalhe do cliente e o cartão fidelidade.
- **Produtos e estoque:** estoque como **livro** de movimentações, alerta de mínimo, foto e visibilidade na loja.
- **Pedidos:** Pendente → Confirmado → Entregue, ou Cancelado (devolve o estoque). Paginados.
- **Unidades:** capacidade por horário, serviços oferecidos e confirmação automática. As unidades só são desativadas, nunca excluídas.
- **Pagamentos:** transições controladas, reembolso pendente e uma mensalidade por mês no seguro.
- **Relatório financeiro:** exportação em **Excel (.xlsx)** e **PDF**.
- **Entradas e saídas:** lançamentos manuais e importação.
- **Gestão da área pública:** planos de seguro, moderação das avaliações e solicitações de seguro.
- **Administração** (só o Geral): **usuários e permissões**, **Log de eventos** com 12 meses de retenção e **integridade do banco** (22 verificações).
- **Sino de pendências:** avisa sobre o que espera a equipe agir.
- **Recuperação de senha do painel** por código enviado por e-mail.
- **Modo visitante:** só leitura, para vitrine.

## 7. Requisitos não funcionais

- **Segurança:**
  - Autorização sempre no backend (403, nunca 200 com dados), e a identidade vem da sessão.
  - BCrypt nas senhas.
  - Os códigos de uso único ficam só como hash.
- **Confiabilidade:**
  - Backup automático na subida (10 últimos).
  - `quick_check` do SQLite antes de subir.
  - E-mail nunca derruba uma operação.
- **Honestidade dos dados:** nenhum número ou selo inventado na tela. Sem dado, estado vazio.
- **Responsividade:** toda tela é conferida a 330, 390, 860 e 1440 px, sem rolagem lateral.
- **Qualidade:** **242 testes automatizados** (xUnit, aplicação real e banco temporário) e CI no GitHub Actions a cada push.
- **Produção:** o app não sobe com a senha de exemplo, os segredos ficam só em variáveis de ambiente e os dados ficam em `/app/data` (volume).

## 8. Fora do escopo agora

- **Gateway de pagamento** (Mercado Pago ou Stripe): futuro, não fazer agora.
- Notificações dentro do site: hoje só existe o e-mail.
- Estoque por unidade: o estoque é único, por decisão.

## 9. Critérios de sucesso

- O ciclo completo em produção funciona: cliente (Google → pet → agendamento com vários serviços → sacola) → equipe (confirma → atende → conclui → entrega o pedido) → financeiro (pagamento → Dashboard → Relatório).
- Todos os testes verdes e o CI verde.
- O sistema está no ar no Railway, com link público, README e LinkedIn atualizados.
