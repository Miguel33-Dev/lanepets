# LanePets — Design (identidade visual e interface)

> Atualizado em 29/09/2026. Regras de front: `rules.md` §3.

## 1. Identidade

Verde profundo + âmbar, com clima acolhedor de petshop, e **honestidade visual**: nada de selo, nota ou número que o
sistema não saiba.

## 2. Paleta

| Papel | Token (painel) | Apelido (site/área do cliente) | Hex |
|---|---|---|---|
| Marca escura | `--brand-900` | `--verde-escuro` | `#033c22` |
| Marca | `--brand-700` | `--verde` | `#07562e` |
| Interativo | `--brand-500` | `--verde-claro` | `#10804a` / `#0a6b3a` |
| Lima (detalhe) | — | `--lima` | `#dcefb9` |
| Acento / CTA | `--accent-500` | `--laranja` | `#ef8f22` |
| Acento hover | `--accent-600` | `--laranja-forte` | `#d97d12` |
| Acento suave | — | `--laranja-suave` | `#fdf0dd` |
| Fundo | `--bg` | `--creme` | `#f7faf6` / `#fbfcf4` |
| Superfície | `--surface` | `--branco` / `--superficie` | `#ffffff` / `#f3f7ef` |
| Borda | `--border` | `--borda` | `#e2eae1` / `#e2eadd` |
| Texto | `--ink-900` | `--tinta` | `#12261c` |
| Sucesso · alerta · erro | — | — | `#1f7a45` · `#b8801a` · `#c0392b` (erro na área do cliente `#b2342a`) |

**Cor por status:** Solicitado/Pendente = laranja · Confirmado/Concluído/Pago = verde · Em andamento = azul `#2f6fa8` ·
Cancelado/Recusado = vermelho.

## 3. Tipografia

- **Fraunces** (serifada): marca, títulos e valores em destaque (site e área do cliente).
- **DM Sans**: texto do site e da área do cliente.
- **Manrope**: títulos e números do painel.
- **Inter**: texto do painel.

## 4. Arquivos de estilo

| Arquivo | Onde |
|---|---|
| `design-system.css` | **única** fonte do painel: tokens, sidebar, topbar, cards, KPIs, tabelas, filtros, botões, badges, modais, toasts, skeletons, `.tabela-cards`, sino, selo de visitante |
| `publico.css` + `js/publico.js` | site público (`cliente.html`) — home com escopo `body.home` |
| `conta.css` (`.cc-`) + `auth.css` (`.lp-`) | área do cliente e telas de login/cadastro |
| `ui-kit.js` | `toast()`, `confirmarAcao()`, `comLoading()`, drawer mobile, rótulos da `.tabela-cards` |
| `css/admin-acoes.css` + `js/admin-acoes.js` | ações em ícone + menu `⋮` |

**Versões atuais (`?v=`):**
- Site e área do cliente: `publico.css` 13 · `js/publico.js` 13 · `conta.css` 27 · `minha-conta.js` 38 · `auth.css` 6.
- Painel: `design-system.css` 9 · `admin-permissoes.js` 7.

## 5. Medidas

- Raios: `--r-sm` 10 · `--r-md` 14 · `--r-lg` 20 · `--r-full` 999.
- Sombras: `--sombra-sm`, `--sombra-md` e `--sombra-lg`, esverdeadas. Foco: `--anel` (anel verde de 4 px).
- Espaçamento do painel: `--sp-1..--sp-9`.
- Largura do conteúdo do site: 1200 px (hero até 1240 px).

## 6. Componentes e padrões

- **Botões:**
  - `.botao.primario`: laranja, texto escuro; no hover escurece e o texto fica branco.
  - `.botao.claro`: branco com borda.
  - No painel: `.btn*` (inclui `.btn-danger-solid` e `.btn.carregando`).
- **Selos de status:** `.cc-selo--ok|aguarda|info|erro|neutro` (bolinha + texto).
- **Quadros de resumo:** `.pdx-resumo__item`. O primeiro vem em destaque, em verde escuro com texto claro.
- **Pílulas de filtro com contagem:** a ativa fica verde.
- **Busca:** campo arredondado com a lupa à esquerda (`.pgx-busca`).
- **Bloco de data** (`.agx-data`): dia da semana, dia grande, mês e hora num chip. A versão grande vem em verde com a hora em laranja.
- **Etiquetas de serviço** (`.agx-servicos`): uma por serviço, em laranja suave.
- **Linha do tempo:**
  - Pedidos: Pedido feito → Confirmado → Retirado.
  - Agendamentos: Solicitado → Confirmado → Em andamento → Concluído, com a etapa atual em laranja.
  - Histórico: linha vertical com um ponto em cada atendimento.
- **Estados vazios:** ícone num quadrado suave, título, frase útil e um botão de ação ("Ir para a loja", "Agendar agora").
- **Esqueletos de carregamento** no lugar de tela branca.
- **Cartões que dependem de dado** nascem `hidden` e só aparecem com número real (nota média, "seguro a partir de", números da home).
- **Ícones:** SVG inline, traço 1.7–1.9, `currentColor`. **Nunca emoji.**

## 7. Telas (prefixos de CSS)

| Tela | Prefixo | Destaques |
|---|---|---|
| Home do site | `.home` | hero com cartões flutuantes, barra de números, serviços em abas com tabela por porte, lojinha com "ver mais", chamada final verde |
| Loja / sacola | `.sl-` | sacola fixa ao lado (desktop) ou barra flutuante (celular), − n + nos cards |
| Agendar — Pagamento | `.pg-` | formas de pagamento em cartões |
| Seguro Pet | `.sg-` | contratos em cartões, histórico recolhido |
| Pagamentos | `.pgx-` | resumo com barra, extrato por mês |
| Meus Pedidos | `.pdx-` | sacola num cartão, linha do tempo |
| Meus Agendamentos | `.agx-` | destaque do próximo atendimento, lista por mês |
| Histórico de serviços | `.hsx-` | pets em cartões, linha do tempo vertical |

## 8. Responsividade

- **Painel:** corte único de **860 px** (menu `⋮`, cards de Agendamentos). Tabela larga no celular vira `.tabela-cards`.
- **Área do cliente:** menu lateral vira drawer. Resumos vão para 2 colunas. Cards viram coluna, com valor e ações numa linha de baixo.
- **Site:** o menu vira hambúrguer a 1080 px. Os serviços vão para 1 coluna e as abas rolam de lado. A lojinha fica em 2 colunas no celular.
- **Conferência obrigatória:** 330, 390, 860 e 1440 px, sem rolagem lateral e sem erro de JS.
