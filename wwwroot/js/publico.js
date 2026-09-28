/* =============================================================================
   LanePets — Site público
   Mesmas rotas da API e mesmo comportamento de antes. Mudou a apresentação:
   ícones reais por tipo de serviço, porte em etiqueta, esqueletos de
   carregamento e estados vazios com texto útil.
   ============================================================================= */
(function () {
  const $ = seletor => document.querySelector(seletor);

  async function api(url, opcoes) {
    const resposta = await fetch(url, opcoes);
    const corpo = await resposta.json();
    if (!resposta.ok || !corpo.ok) throw new Error(corpo.error || 'Não foi possível concluir a solicitação.');
    return corpo.data;
  }

  const money = valor => Number(valor).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  const esc = valor => { const d = document.createElement('div'); d.textContent = valor ?? ''; return d.innerHTML; };

  /* ---------- Ícones ----------------------------------------------------- */
  const svg = caminho =>
    '<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + caminho + '</svg>';

  const ICONES = {
    banho:    '<path d="M4 13h16v2a5 5 0 0 1-5 5H9a5 5 0 0 1-5-5Z"/><path d="M7 13V5.5A2.5 2.5 0 0 1 9.5 3c1.2 0 2.2.8 2.4 2"/><path d="M11 6h3"/><path d="M6 21.5 5 23M12 21.5 11 23M18 21.5 17 23"/>',
    tosa:     '<circle cx="6" cy="6" r="2.6"/><circle cx="6" cy="18" r="2.6"/><path d="M8.2 7.6 20 18M8.2 16.4 20 6"/>',
    hidrata:  '<path d="M12 3s6 6.2 6 10a6 6 0 0 1-12 0c0-3.8 6-10 6-10Z"/><path d="M9.5 14a2.6 2.6 0 0 0 2.5 2.5"/>',
    unha:     '<path d="M12 3c3.3 0 5.5 2.4 5.5 5.6 0 3.1-1.4 5.1-1.4 7.4 0 2-1.6 3-4.1 3s-4.1-1-4.1-3c0-2.3-1.4-4.3-1.4-7.4C6.5 5.4 8.7 3 12 3Z"/>',
    dente:    '<path d="M6 4c2 0 2.4 1.2 6 1.2S16 4 18 4c2.4 0 2.7 3 2 6-.6 2.4-1.3 3.4-1.8 6.2-.4 2.3-1 3.8-2.2 3.8-1.6 0-1.5-4.6-4-4.6s-2.4 4.6-4 4.6c-1.2 0-1.8-1.5-2.2-3.8C5.3 13.4 4.6 12.4 4 10c-.7-3-.4-6 2-6Z"/>',
    pacote:   '<path d="m3 8 9-5 9 5-9 5-9-5Z"/><path d="M3 8v8l9 5 9-5V8"/><path d="M12 13v8"/>',
    racao:    '<path d="M5 9h14l-1.2 10.2A2 2 0 0 1 15.8 21H8.2a2 2 0 0 1-2-1.8Z"/><path d="M8.5 9V6.5a3.5 3.5 0 0 1 7 0V9"/><path d="M10 13.5h4M10 16.5h4"/>',
    brinquedo:'<circle cx="12" cy="12" r="8.4"/><path d="M6.1 6.4c2.9 1.5 4.4 4.3 4.4 8.6"/><path d="M17.9 6.4c-2.9 1.5-4.4 4.3-4.4 8.6"/>',
    higiene:  '<path d="M9 3h6v4H9z"/><path d="M7.5 7h9l1 12.2A1.8 1.8 0 0 1 15.7 21H8.3a1.8 1.8 0 0 1-1.8-1.8Z"/><path d="M9.5 12h5"/>',
    pet:      '<circle cx="6.5" cy="9" r="2"/><circle cx="11" cy="6.3" r="2"/><circle cx="15.8" cy="9" r="2"/><path d="M11 13.4c-3.1 0-5.6 2-5.6 4.4 0 1.5 1.4 2.2 2.8 1.7.85-.3 1.8-.45 2.8-.45s1.95.15 2.8.45c1.4.5 2.8-.2 2.8-1.7 0-2.4-2.5-4.4-5.6-4.4Z"/>'
  };

  const REGRAS = [
    [/tosa|corte|maquin/i, 'tosa'],
    [/hidrat|spa|desembar/i, 'hidrata'],
    [/unha|garra/i, 'unha'],
    [/dent|hálito|halito|bucal/i, 'dente'],
    [/banho/i, 'banho'],
    [/pacote|plano|mensal/i, 'pacote'],
    [/ração|racao|alimento|petisc|snack|biscoit/i, 'racao'],
    [/brinq|bola|corda|mordedor/i, 'brinquedo'],
    [/shampoo|xampu|perfume|colônia|colonia|higien|tapete|sabon/i, 'higiene']
  ];

  function icone(nome, padrao) {
    const texto = String(nome || '');
    for (const [regra, chave] of REGRAS) if (regra.test(texto)) return svg(ICONES[chave]);
    return svg(ICONES[padrao || 'pet']);
  }

  /* ---------- Blocos reutilizáveis --------------------------------------- */
  const esqueleto = quantidade => Array.from({ length: quantidade }, () =>
    '<article class="card esqueleto" aria-hidden="true"><span class="b1"></span><span class="b2"></span><span class="b3"></span><span class="b4"></span></article>'
  ).join('');

  const vazio = (titulo, texto) =>
    `<article class="card"><span class="servico-icone">${svg(ICONES.pet)}</span><h3>${titulo}</h3><p>${texto}</p></article>`;

  const DESCRICOES = {
    banho: 'Banho com produtos adequados ao pelo, secagem e escovação.',
    tosa: 'Tosa feita com calma, respeitando o temperamento do pet.',
    hidrata: 'Hidratação profunda para deixar o pelo macio e sem nós.',
    unha: 'Corte de unhas seguro, sem machucar as almofadinhas.',
    dente: 'Cuidado bucal para manter o hálito e os dentes saudáveis.',
    pacote: 'Rotina de cuidados combinada em um pacote mensal.',
    pet: 'Cuidado personalizado para o seu pet.'
  };

  function descricao(nome) {
    const texto = String(nome || '');
    for (const [regra, chave] of REGRAS) if (regra.test(texto)) return DESCRICOES[chave] || DESCRICOES.pet;
    return DESCRICOES.pet;
  }

  function cardServico(servico) {
    const porte = servico.porte
      ? `<span class="tag tag-porte">Porte ${esc(servico.porte)}</span>`
      : '';
    return `<article class="card">
      <span class="servico-icone">${icone(servico.nome)}</span>
      <div class="card-tags">${porte}</div>
      <h3>${esc(servico.nome)}</h3>
      <p>${descricao(servico.nome)}</p>
      <span class="preco"><small>A partir de</small> ${money(servico.preco)}</span>
    </article>`;
  }

  /* ---------- Serviços agrupados (28/09) ---------------------------------
     O cadastro tem um registro por porte ("Banho" P, M e G). Na home isso
     virava tres cards iguais. Agora: um card por servico com o preco de cada
     porte, e abas por categoria. Os valores sao os da API, sem arredondar. */
  const CATEGORIAS_SERVICO = [
    { chave: 'banho',  nome: 'Banho',           regra: n => /banho/i.test(n) && !/tosa/i.test(n) && !/pacote/i.test(n) },
    { chave: 'tosa',   nome: 'Banho e tosa',    regra: n => /tosa/i.test(n) && !/pacote/i.test(n) },
    { chave: 'trat',   nome: 'Tratamentos',     regra: n => /hidrat|oz[oô]nio|subpelo|desembol|shampoo|parasit/i.test(n) },
    { chave: 'extra',  nome: 'Cuidados extras', regra: n => /unha|dente|bucal/i.test(n) },
    { chave: 'pacote', nome: 'Pacotes',         regra: n => /pacote/i.test(n) },
    { chave: 'outros', nome: 'Outros',          regra: () => true }
  ];
  const ORDEM_PORTE = ['pequeno', 'médio', 'medio', 'grande', 'gato', 'adicional', ''];
  const semAcento = t => String(t || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().replace(/\s+/g, ' ').trim();
  const maiuscula = t => { const x = String(t || '').trim(); return x.charAt(0).toUpperCase() + x.slice(1); };

  let gruposServico = [];
  let abaServico = '';

  function agruparServicos(lista) {
    const mapa = new Map();
    for (const s of lista) {
      const chave = semAcento(s.nome);
      if (!chave) continue;
      if (!mapa.has(chave)) mapa.set(chave, { nome: maiuscula(s.nome), precos: [] });
      const g = mapa.get(chave);
      const porte = String(s.porte || '').trim();
      const preco = Number(s.preco) || 0;
      if (!g.precos.some(x => semAcento(x.porte) === semAcento(porte) && x.preco === preco)) g.precos.push({ porte, preco });
    }
    const grupos = [...mapa.values()];
    for (const g of grupos) {
      g.precos.sort((a, b) => ORDEM_PORTE.indexOf(semAcento(a.porte)) - ORDEM_PORTE.indexOf(semAcento(b.porte)) || a.preco - b.preco);
      /* "Adicional" + sem porte com o mesmo valor = a mesma coisa; fica uma linha so. */
      g.precos = g.precos.filter((x, i, arr) => !(x.porte === '' && arr.some(y => y !== x && /adicional/i.test(y.porte) && y.preco === x.preco)));
      g.categoria = CATEGORIAS_SERVICO.find(c => c.regra(g.nome)).chave;
    }
    return grupos.sort((a, b) => a.nome.localeCompare(b.nome, 'pt-BR'));
  }

  const rotuloPorte = porte => {
    const p = semAcento(porte);
    if (!p) return 'Valor';
    if (p === 'gato') return 'Gatos';
    if (p === 'adicional') return 'Adicional';
    return 'Porte ' + String(porte).trim().toLowerCase();
  };
  const valorServico = preco => (preco >= 1 ? money(preco) : 'Sob consulta');

  function cardServicoGrupo(g) {
    const linhas = g.precos.map(x => `<li><span>${esc(rotuloPorte(x.porte))}</span><b>${valorServico(x.preco)}</b></li>`).join('');
    return `<article class="card srv-card">
      <div class="srv-topo">
        <span class="servico-icone">${icone(g.nome)}</span>
        <div><h3>${esc(g.nome)}</h3><p>${descricao(g.nome)}</p></div>
      </div>
      <ul class="srv-precos">${linhas}</ul>
      <a class="srv-agendar" href="minha-conta.html#agendar">Agendar este serviço <b>→</b></a>
    </article>`;
  }

  function desenharServicos() {
    const abas = $('#servicos-abas');
    const alvo = $('#lista-servicos');
    const usadas = CATEGORIAS_SERVICO.filter(c => gruposServico.some(g => g.categoria === c.chave));
    if (!usadas.some(c => c.chave === abaServico)) abaServico = usadas.length ? usadas[0].chave : '';
    if (abas) {
      abas.hidden = usadas.length < 2;
      abas.innerHTML = usadas.map(c => {
        const n = gruposServico.filter(g => g.categoria === c.chave).length;
        return `<button type="button" role="tab" class="srv-aba" data-aba="${c.chave}" aria-selected="${c.chave === abaServico}">${esc(c.nome)}<span>${n}</span></button>`;
      }).join('');
    }
    const lista = gruposServico.filter(g => g.categoria === abaServico);
    alvo.innerHTML = lista.length
      ? lista.map(cardServicoGrupo).join('')
      : vazio('Serviços LanePets', 'Em breve, novos serviços. Fale com a nossa equipe para conhecer as opções.');
  }

  async function carregarServicos() {
    try {
      gruposServico = agruparServicos(await api('/api/public/servicos'));
      desenharServicos();
      numeroHome('servicos', gruposServico.length);
    } catch (erro) {
      console.error(erro);
      $('#lista-servicos').innerHTML = vazio('Conteúdo indisponível agora', 'Não conseguimos carregar os serviços. Atualize a página ou fale com a nossa equipe.');
    }
  }

  document.addEventListener('click', evento => {
    const aba = evento.target.closest('.srv-aba');
    if (!aba) return;
    abaServico = aba.dataset.aba;
    desenharServicos();
  });

  /* Mostra um card da faixa de numeros so quando ha um numero de verdade. */
  function numeroHome(chave, valor) {
    const card = document.querySelector(`[data-numero="${chave}"]`);
    if (!card || !(Number(valor) > 0)) return;
    card.querySelector('strong').textContent = Number(valor).toLocaleString('pt-BR');
    card.hidden = false;
  }

  /* =======================================================================
     LOJINHA

     Os produtos continuam vindo de /api/public/produtos — a MESMA rota de
     antes, o mesmo registro que o painel administrativo edita. O que mudou e
     o que a tela faz com eles: agora a lista fica em memoria e a busca, o
     filtro de categoria e a ordenacao trabalham sobre ela, sem ida extra ao
     servidor a cada tecla.

     Nao ha carrinho no LanePets. O botao leva ao fluxo de pedido que ja
     existe, na Area do Cliente — nenhuma logica de compra nova foi criada, e
     quem valida estoque e preco continua sendo o backend.
     ======================================================================= */

  let lojaProdutos = [];                 // tudo que a API devolveu
  let lojaCategoria = '';                // '' = todas
  let lojaBusca = '';
  let lojaOrdem = 'nome';

  /**
   * Estado do estoque, a partir do que a API realmente manda:
   * controlaEstoque, estoque e o proprio "disponivel" calculado no servidor.
   * Nada e estimado aqui.
   */
  function estadoProduto(produto) {
    if (produto.disponivel === false) return { chave: 'fora', texto: 'Indisponível' };
    if (produto.controlaEstoque && Number(produto.estoque) <= 3) {
      return { chave: 'pouco', texto: 'Poucas unidades' };
    }
    return { chave: 'ok', texto: 'Em estoque' };
  }

  const ICONE_CARRINHO = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="9" cy="20" r="1.4"/><circle cx="18" cy="20" r="1.4"/><path d="M2.5 3h2.2l2.3 11.2a1.6 1.6 0 0 0 1.6 1.3h8.7a1.6 1.6 0 0 0 1.6-1.25L21 7H6"/></svg>';

  const tomCategoria = c => [...String(c || '')].reduce((n, ch) => n + ch.charCodeAt(0), 0) % 5;
  const LOJA_PASSO = 8;
  let lojaLimite = LOJA_PASSO;

  function cardProduto(produto) {
    /* A disponibilidade vem do mesmo estoque do painel: o produto e o mesmo
       registro do banco, nao uma copia do site. */
    const estado = estadoProduto(produto);
    const fora = estado.chave === 'fora';
    const categoria = esc(produto.categoria || 'LanePets');

    /* O cadastro de produto nao tem campo de imagem. Em vez de um retangulo
       vazio, a area de midia traz o simbolo da categoria, na mesma proporcao
       em todos os cards — sem foto deformada e sem buraco na grade. */
    return `<article class="card loja-card${fora ? ' fora' : ''}">
      <div class="loja-media loja-media--t${tomCategoria(produto.categoria)}">
        <span class="loja-selo loja-selo--${estado.chave}">${estado.texto}</span>
        ${produto.fotoUrl && /^data:image\//.test(produto.fotoUrl)
          ? `<img src="${esc(produto.fotoUrl)}" alt="${esc(produto.nome)}" loading="lazy">`
          : `<span class="loja-simbolo">${icone(produto.nome + ' ' + (produto.categoria || ''), 'racao')}</span>`}
      </div>
      <div class="loja-corpo">
        <span class="tag">${categoria}</span>
        <h3>${esc(produto.nome)}</h3>
        ${produto.descricao ? `<p class="loja-desc">${esc(produto.descricao)}</p>` : ''}
        <div class="loja-preco"><strong>${money(produto.valorVenda)}</strong></div>
        ${fora
          ? '<button class="botao claro" type="button" disabled>Indisponível</button>'
          : `<a class="botao primario" href="minha-conta.html#pedido">${ICONE_CARRINHO}<b>Pedir</b></a>`}
      </div>
    </article>`;
  }

  /** Redesenha a grade a partir dos filtros atuais. */
  function desenharLoja() {
    const alvo = $('#lista-produtos');
    const termo = lojaBusca.trim().toLowerCase();

    let lista = lojaProdutos.filter(produto => {
      if (lojaCategoria && produto.categoria !== lojaCategoria) return false;
      if (termo) {
        const texto = (produto.nome + ' ' + (produto.categoria || '')).toLowerCase();
        if (!texto.includes(termo)) return false;
      }
      return true;
    });

    lista = lista.slice().sort((a, b) => {
      if (lojaOrdem === 'menor') return Number(a.valorVenda) - Number(b.valorVenda);
      if (lojaOrdem === 'maior') return Number(b.valorVenda) - Number(a.valorVenda);
      return String(a.nome).localeCompare(String(b.nome), 'pt-BR');
    });

    const contagem = $('#lojaContagem');
    const mostrando = Math.min(lista.length, lojaLimite);
    if (contagem) {
      contagem.textContent = !lojaProdutos.length ? ''
        : lista.length > mostrando
          ? `Mostrando ${mostrando} de ${lista.length} produtos`
          : `${lista.length} ${lista.length === 1 ? 'produto' : 'produtos'}`;
    }
    const mais = $('#loja-mais');
    if (mais) {
      const resta = lista.length - mostrando;
      mais.hidden = resta <= 0;
      const b = $('#loja-mais-botao');
      if (b && resta > 0) b.textContent = `Ver mais ${Math.min(resta, LOJA_PASSO)} ${Math.min(resta, LOJA_PASSO) === 1 ? 'produto' : 'produtos'}`;
    }

    if (!lista.length) {
      alvo.innerHTML = `<div class="loja-estado">
        <h3>Nenhum produto encontrado</h3>
        <p>Não encontramos produtos para a busca ou o filtro selecionado.</p>
        <button class="botao claro" type="button" id="lojaLimparTudo">Limpar filtros</button>
      </div>`;
      const limpar = $('#lojaLimparTudo');
      if (limpar) limpar.addEventListener('click', limparFiltrosLoja);
      return;
    }

    alvo.innerHTML = lista.slice(0, lojaLimite).map(cardProduto).join('');
  }

  function limparFiltrosLoja() {
    lojaLimite = LOJA_PASSO;
    lojaBusca = '';
    lojaCategoria = '';
    const campo = $('#lojaBusca');
    if (campo) { campo.value = ''; campo.parentElement.classList.remove('tem-texto'); }
    montarCategorias();
    desenharLoja();
  }

  /** Pilulas de categoria construidas a partir das categorias que EXISTEM. */
  function montarCategorias() {
    const caixa = $('#lojaCategorias');
    if (!caixa) return;

    const categorias = [...new Set(lojaProdutos.map(p => p.categoria).filter(Boolean))]
      .sort((a, b) => a.localeCompare(b, 'pt-BR'));

    caixa.innerHTML = [['', 'Todos'], ...categorias.map(c => [c, c])]
      .map(([valor, rotulo]) => `<button type="button" class="loja-chip" data-categoria="${esc(valor)}"
             aria-pressed="${valor === lojaCategoria}">${esc(rotulo)}</button>`).join('');
  }

  function ligarLoja() {
    const campo = $('#lojaBusca');
    if (campo) {
      campo.addEventListener('input', () => {
        lojaBusca = campo.value;
        lojaLimite = LOJA_PASSO;
        campo.parentElement.classList.toggle('tem-texto', campo.value.length > 0);
        desenharLoja();
      });
    }

    const limparBusca = $('#lojaLimparBusca');
    if (limparBusca) {
      limparBusca.addEventListener('click', () => {
        lojaBusca = '';
        campo.value = '';
        campo.parentElement.classList.remove('tem-texto');
        campo.focus();
        desenharLoja();
      });
    }

    const ordem = $('#lojaOrdem');
    if (ordem) ordem.addEventListener('change', () => { lojaOrdem = ordem.value; lojaLimite = LOJA_PASSO; desenharLoja(); });

    const mais = $('#loja-mais-botao');
    if (mais) mais.addEventListener('click', () => { lojaLimite += LOJA_PASSO; desenharLoja(); });

    const caixa = $('#lojaCategorias');
    if (caixa) {
      caixa.addEventListener('click', evento => {
        const chip = evento.target.closest('.loja-chip');
        if (!chip) return;
        lojaCategoria = chip.dataset.categoria || '';
        lojaLimite = LOJA_PASSO;
        caixa.querySelectorAll('.loja-chip').forEach(b =>
          b.setAttribute('aria-pressed', String(b === chip)));
        desenharLoja();
      });
    }
  }

  /** Carrega a lojinha. Erro nao vira texto tecnico na cara do cliente. */
  async function carregarLoja() {
    try {
      lojaProdutos = await api('/api/public/produtos');
      numeroHome('produtos', lojaProdutos.length);
      montarCategorias();
      desenharLoja();
    } catch (erro) {
      console.error(erro);
      $('#lojaContagem').textContent = '';
      $('#lista-produtos').innerHTML = `<div class="loja-estado">
        <h3>Não foi possível carregar os produtos</h3>
        <p>Pode ter sido uma instabilidade na conexão. Tente de novo em instantes.</p>
        <button class="botao claro" type="button" id="lojaTentarDeNovo">Tentar novamente</button>
      </div>`;
      const botao = $('#lojaTentarDeNovo');
      if (botao) botao.addEventListener('click', () => {
        $('#lista-produtos').innerHTML = esqueleto(4);
        carregarLoja();
      });
    }
  }

  function cardPlano(plano, indice) {
    const destaque = indice === 1;
    const coberturas = String(plano.coberturas || '')
      .split(';').map(item => item.trim()).filter(Boolean)
      .map(item => `<li>${esc(item)}</li>`).join('');
    return `<article class="plano ${destaque ? 'destaque' : ''}">
      <h3>${esc(plano.nome)}</h3>
      <p>${esc(plano.descricao)}</p>
      <ul>${coberturas}</ul>
      <strong>${money(plano.valorMensal)}<small>/mês</small></strong>
      <button class="botao ${destaque ? 'primario' : 'claro'} contratar" type="button" data-id="${esc(plano.id)}" data-nome="${esc(plano.nome)}">Quero este plano</button>
    </article>`;
  }

  const iniciais = nome => String(nome || '?').trim().split(/\s+/).filter(Boolean).slice(0, 2).map(p => p[0].toUpperCase()).join('') || '?';
  function cardDepoimento(depoimento) {
    const nota = Math.max(0, Math.min(5, Number(depoimento.avaliacao) || 0));
    return `<article class="card depoimento">
      <div class="dep-topo">
        <span class="estrelas" aria-label="${nota} de 5 estrelas">${'★'.repeat(nota)}<span class="apagada">${'★'.repeat(5 - nota)}</span></span>
        <svg class="dep-aspas" viewBox="0 0 32 24" aria-hidden="true"><path d="M0 24V13.7C0 5.9 4.4 1.3 12.3 0l1.2 3.6C9 4.8 7 7.4 6.8 11.2H12V24Zm18 0V13.7C18 5.9 22.4 1.3 30.3 0l1.2 3.6c-4.5 1.2-6.5 3.8-6.7 7.6H30V24Z"/></svg>
      </div>
      <blockquote>${esc(depoimento.comentario)}</blockquote>
      <div class="dep-autor">
        <span class="dep-avatar" aria-hidden="true">${esc(iniciais(depoimento.nomeCliente))}</span>
        <div><strong>${esc(depoimento.nomeCliente)}</strong><span>tutor(a) de ${esc(depoimento.nomePet)}</span></div>
      </div>
    </article>`;
  }

  /* ---------- Unidades / Contato -----------------------------------------
     Uma única chamada a /api/public/unidades alimenta as duas seções: o card
     completo em #unidades e a versão resumida (endereço + telefone) em
     #contato. Nenhum dado de contato é inventado — o que a unidade não tem
     cadastrado simplesmente não aparece.
     ------------------------------------------------------------------------- */
  const ICONE_PIN = svg('<path d="M12 21.5s7.5-6.6 7.5-12.4A7.5 7.5 0 0 0 4.5 9.1c0 5.8 7.5 12.4 7.5 12.4Z"/><circle cx="12" cy="9" r="2.6"/>');
  const ICONE_TEL = svg('<path d="M4.5 4h3.6l1.6 4.4-2.1 1.8a13 13 0 0 0 6.2 6.2l1.8-2.1 4.4 1.6v3.6c0 1-.9 1.8-1.9 1.7A17 17 0 0 1 3 5.9 1.8 1.8 0 0 1 4.5 4Z"/>');
  const ICONE_RELOGIO = svg('<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/>');

  /* 29/09: telefone clicavel (tel:) e mostrado com mascara; "Como chegar" so quando ha endereco de verdade
     (o texto "Consulte a equipe..." que algumas unidades tem cadastrado nao vira link de mapa). */
  function telefoneBonito(digitos) {
    const d = digitos.replace(/^55(?=\d{10,11}$)/, '');
    if (d.length === 11) return `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`;
    if (d.length === 10) return `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`;
    return null;
  }
  function linhaTelefone(texto) {
    const digitos = texto.replace(/\D/g, '');
    const bonito = telefoneBonito(digitos);
    if (!bonito) return `<div class="unidade-linha"><span>${ICONE_TEL}</span><span>${esc(texto)}</span></div>`;
    const tel = digitos.startsWith('55') && digitos.length > 11 ? digitos : '55' + digitos;
    return `<div class="unidade-linha"><span>${ICONE_TEL}</span><a class="unidade-tel" href="tel:+${tel}">${esc(bonito)}</a></div>`;
  }
  function enderecoReal(endereco) {
    const e = String(endereco || '').trim();
    return e.length >= 8 && !/consulte|a confirmar/i.test(e) ? e : '';
  }

  function cardUnidade(unidade) {
    const telefone = String(unidade.telefone || '').trim();
    const endereco = enderecoReal(unidade.endereco);
    const mapa = endereco
      ? `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${endereco}, ${unidade.nome}`)}`
      : '';
    return `<article class="card unidade-card">
      <span class="servico-icone">${ICONE_PIN}</span>
      <h3>${esc(unidade.nome)}</h3>
      <div class="unidade-linha"><span>${ICONE_PIN}</span><span>${esc(unidade.endereco || 'Endereço a confirmar com a equipe LanePets.')}</span></div>
      ${telefone ? linhaTelefone(telefone) : ''}
      ${unidade.horarioFuncionamento ? `<div class="unidade-linha discreta"><span>${ICONE_RELOGIO}</span><span>${esc(unidade.horarioFuncionamento)}</span></div>` : ''}
      ${mapa ? `<a class="botao claro unidade-mapa" href="${mapa}" target="_blank" rel="noopener">Como chegar <b>→</b></a>` : ''}
    </article>`;
  }

  async function carregarUnidades() {
    const alvoUnidades = $('#lista-unidades');
    const sub = $('#unidades-sub');
    try {
      const unidades = await api('/api/public/unidades');
      if (alvoUnidades) {
        alvoUnidades.innerHTML = unidades.length
          ? unidades.map(cardUnidade).join('')
          : vazio('Novas unidades em breve', 'Estamos organizando as próximas unidades LanePets.');
      }
      // O numero sai da lista de verdade (antes estava "Duas unidades" escrito a mao).
      if (sub && unidades.length) {
        const n = unidades.length;
        const extenso = ['', 'Uma', 'Duas', 'Três', 'Quatro', 'Cinco', 'Seis', 'Sete', 'Oito', 'Nove', 'Dez'][n] || String(n);
        sub.textContent = n === 1
          ? 'Uma unidade preparada para receber você e o seu pet.'
          : `${extenso} unidades preparadas para receber você e o seu pet.`;
      }
    } catch (erro) {
      console.error(erro);
      if (alvoUnidades) alvoUnidades.innerHTML = vazio('Não foi possível carregar as unidades', 'Atualize a página em instantes.');
    }
  }

  /* ---------- Carregamento ----------------------------------------------- */
  $('#lista-servicos').innerHTML = esqueleto(3);
  $('#lista-produtos').innerHTML = esqueleto(4);
  $('#lista-depoimentos').innerHTML = esqueleto(3);
  $('#lista-seguros').innerHTML = '<p style="opacity:.7">Carregando planos…</p>';
  if ($('#lista-unidades')) $('#lista-unidades').innerHTML = esqueleto(3);

  async function carregarSecao(seletor, url, montar, tituloVazio, textoVazio, limite) {
    try {
      const todos = await api(url);
      const dados = limite ? todos.slice(0, limite) : todos;
      $(seletor).innerHTML = dados.length ? dados.map(montar).join('') : vazio(tituloVazio, textoVazio);
      return dados;
    } catch (erro) {
      console.error(erro);
      $(seletor).innerHTML = vazio('Conteúdo indisponível agora', 'Não conseguimos carregar esta lista. Atualize a página ou fale com a nossa equipe.');
      return [];
    }
  }

  /* ---------- Faixa de números -------------------------------------------
     Só números reais: pets com atendimento concluído e média das avaliações
     aprovadas. Zero/sem avaliação = card continua escondido. */
  async function carregarNumeros() {
    try {
      const n = await api('/api/public/numeros');
      const mostrar = (chave, texto) => {
        const card = document.querySelector(`[data-numero="${chave}"]`);
        if (!card || !texto) return;
        card.querySelector('strong').textContent = texto;
        card.hidden = false;
      };
      const pets = Number(n.petsAtendidos || 0);
      if (pets > 0) mostrar('pets', pets.toLocaleString('pt-BR'));
      if (n.avaliacaoMedia != null && Number(n.avaliacoes) > 0) {
        mostrar('avaliacao', Number(n.avaliacaoMedia).toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + '/5');
        const rotulo = document.querySelector('[data-numero="avaliacao"] span');
        const qtdTexto = `${n.avaliacoes} ${Number(n.avaliacoes) === 1 ? 'avaliação' : 'avaliações'}`;
        if (rotulo) rotulo.textContent = `avaliação média · ${qtdTexto}`;
        const chip = document.querySelector('#hero-nota');
        if (chip) {
          chip.querySelector('strong').textContent = Number(n.avaliacaoMedia).toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + ' de 5';
          chip.querySelector('small').textContent = `média de ${qtdTexto}`;
          chip.hidden = false;
        }
      }
    } catch (erro) {
      console.error('[LanePets] Não foi possível carregar os números da home.', erro);
    }
  }

  async function carregar() {
    await carregarServicos();

    /* A lojinha nao passa mais pelo carregarSecao generico: ela guarda a lista
       inteira para a busca e os filtros trabalharem, e mostra o catalogo todo
       em vez dos seis primeiros itens. */
    await carregarLoja();

    try {
      const planos = await api('/api/public/seguros');
      $('#lista-seguros').innerHTML = planos.length
        ? planos.map(cardPlano).join('')
        : '<p style="opacity:.75">Nenhum plano disponível no momento. Fale com a nossa equipe.</p>';
      /* Cartao "Seguro pet" do hero: menor mensalidade real dos planos ativos. */
      const valores = planos.map(p => Number(p.valorMensal)).filter(v => v > 0);
      const chipSeguro = $('#hero-seguro');
      if (chipSeguro && valores.length) {
        chipSeguro.querySelector('small').textContent = `planos a partir de ${money(Math.min(...valores))}/mês`;
        chipSeguro.hidden = false;
      }
      document.querySelectorAll('.contratar').forEach(botao => botao.addEventListener('click', () => {
        $('#plano-id').value = botao.dataset.id;
        $('#seguro-titulo').textContent = 'Plano ' + botao.dataset.nome;
        $('#modal-seguro').showModal();
      }));
    } catch (erro) {
      console.error(erro);
      $('#lista-seguros').innerHTML = '<p style="opacity:.75">Não foi possível carregar os planos agora.</p>';
    }

    const deps = await carregarSecao('#lista-depoimentos', '/api/public/depoimentos',
      cardDepoimento, 'Seja a primeira família a avaliar', 'Clientes LanePets podem enviar um depoimento para a nossa moderação.', 6);
    const caixaDeps = $('#lista-depoimentos');
    if (caixaDeps) caixaDeps.classList.toggle('dep-vazio', !deps.length);

    await carregarUnidades();
  }

  /* ---------- Envio dos formulários --------------------------------------- */
  function enviando(botao, ativo, texto) {
    botao.disabled = ativo;
    if (ativo) { botao.dataset.textoOriginal = botao.textContent; botao.textContent = texto; }
    else if (botao.dataset.textoOriginal) { botao.textContent = botao.dataset.textoOriginal; }
  }

  /* Avaliacao pelo site (26/09): so com a conta do cliente. Nome + telefone nao
     identificam ninguem, entao o formulario anonimo saiu. Logado (mesmo token da
     area do cliente), o modal mostra o formulario com os pets da conta e envia para
     POST /api/cliente/avaliacoes; sem login, mostra "Entrar para avaliar". Quem decide
     e o servidor: token vencido volta 401 e o modal cai no bloco de login. */
  const CHAVE_CLIENTE = 'lanePetsClienteToken';
  const tokenCliente = () => { try { return localStorage.getItem(CHAVE_CLIENTE) || ''; } catch (_) { return ''; } };
  const apiCliente = (rota, opcoes = {}) => api('/api/cliente/' + rota, {
    ...opcoes,
    headers: { ...(opcoes.headers || {}), 'Content-Type': 'application/json', 'X-LanePets-Client': tokenCliente() }
  });

  function blocoDepoimento(qual) {
    ['carregando', 'login', 'form'].forEach(b => { $('#dep-' + b).hidden = b !== qual; });
  }

  async function abrirDepoimento() {
    $('#dep-feedback').textContent = '';
    $('#modal-depoimento').showModal();
    if (!tokenCliente()) { blocoDepoimento('login'); return; }
    blocoDepoimento('carregando');
    try {
      const conta = await apiCliente('conta');
      $('#dep-quem').textContent = conta.nome || 'cliente LanePets';
      const pets = conta.pets || [];
      $('#dep-pet').innerHTML = pets.map(p => `<option value="${esc(p.id)}">${esc(p.petNome)}</option>`).join('');
      $('#dep-pet-rotulo').hidden = pets.length === 0;
      blocoDepoimento('form');
    } catch (erro) {
      /* Sessao vencida (o servidor reinicia as sessoes em memoria) ou conta removida. */
      console.error('[LanePets] conta do cliente indisponivel para avaliar:', erro);
      blocoDepoimento('login');
    }
  }

  $('#abrir-depoimento').addEventListener('click', abrirDepoimento);

  $('#enviar-depoimento').addEventListener('click', async evento => {
    evento.preventDefault();
    const botao = evento.currentTarget;
    const feedback = $('#dep-feedback');
    feedback.textContent = '';
    enviando(botao, true, 'Enviando…');
    try {
      const dados = await apiCliente('avaliacoes', {
        method: 'POST',
        body: JSON.stringify({
          petId: $('#dep-pet').value || null,
          avaliacao: Number($('#dep-avaliacao').value),
          comentario: $('#dep-comentario').value
        })
      });
      feedback.textContent = dados.message;
      $('#dep-comentario').value = '';
    } catch (erro) {
      feedback.textContent = erro.message;
    } finally {
      enviando(botao, false);
    }
  });

  $('#enviar-seguro').addEventListener('click', async evento => {
    evento.preventDefault();
    const botao = evento.currentTarget;
    const feedback = $('#seguro-feedback');
    feedback.textContent = '';
    enviando(botao, true, 'Enviando…');
    try {
      const dados = await api('/api/public/seguros/solicitacoes', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          planoId: $('#plano-id').value,
          nome: $('#seguro-nome').value,
          telefone: $('#seguro-telefone').value,
          pet: $('#seguro-pet').value,
          observacao: $('#seguro-observacao').value
        })
      });
      feedback.textContent = dados.message;
      botao.form.reset();
    } catch (erro) {
      feedback.textContent = erro.message;
    } finally {
      enviando(botao, false);
    }
  });

  ligarLoja();
  carregar();
  carregarNumeros();
})();

/* =============================================================================
   Navegação do topo — menu responsivo e estado de sessão do cliente
   ============================================================================= */
(function () {
  const botao = document.querySelector('#abrir-menu');
  const nav = document.querySelector('#menu-principal');
  if (!nav) return;

  if (botao) {
    const alternar = aberto => {
      nav.classList.toggle('aberto', aberto);
      botao.setAttribute('aria-expanded', String(aberto));
      botao.textContent = aberto ? '✕' : '☰';
      botao.setAttribute('aria-label', aberto ? 'Fechar menu' : 'Abrir menu');
    };
    botao.addEventListener('click', () => alternar(!nav.classList.contains('aberto')));
    nav.addEventListener('click', evento => { if (evento.target.tagName === 'A') alternar(false); });
    document.addEventListener('keydown', evento => { if (evento.key === 'Escape') alternar(false); });
    window.addEventListener('resize', () => { if (window.innerWidth > 1080) alternar(false); });
  }

  const conta = document.querySelector('#nav-conta');
  const acao = document.querySelector('#nav-acao');
  const logado = () => Boolean(localStorage.getItem('lanePetsClienteToken'));

  function aplicarEstado() {
    if (!conta || !acao) return;
    if (logado()) {
      conta.textContent = 'Minha conta';
      conta.href = 'minha-conta.html';
      acao.textContent = 'Sair';
      acao.href = '#sair';
      acao.dataset.sair = '1';
    } else {
      conta.textContent = 'Entrar';
      conta.href = 'minha-conta.html';
      acao.textContent = 'Criar conta';
      acao.href = 'minha-conta.html#cadastro';
      delete acao.dataset.sair;
    }
  }

  if (acao) {
    acao.addEventListener('click', async evento => {
      if (!acao.dataset.sair) return;
      evento.preventDefault();
      const token = localStorage.getItem('lanePetsClienteToken') || '';
      try {
        await fetch('/api/cliente/logout', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-LanePets-Client': token } });
      } catch (_) { /* a sessão local é encerrada de qualquer forma */ }
      localStorage.removeItem('lanePetsClienteToken');
      aplicarEstado();
    });
  }

  aplicarEstado();
  window.addEventListener('storage', aplicarEstado);
  window.addEventListener('pageshow', aplicarEstado);
})();

/* =============================================================================
   Modais <dialog>
   O X é type="button" (nunca submete o formulário) e fecha de verdade.
   Clique fora do cartão também fecha; o Esc já é nativo do <dialog>.
   ============================================================================= */
(function () {
  document.addEventListener('click', evento => {
    const alvo = evento.target.closest('[data-fechar]');
    if (!alvo) return;
    evento.preventDefault();
    const caixa = alvo.closest('dialog');
    if (caixa && typeof caixa.close === 'function') caixa.close();
  });

  document.querySelectorAll('dialog').forEach(caixa => {
    caixa.addEventListener('click', evento => { if (evento.target === caixa) caixa.close(); });
    caixa.addEventListener('close', () => {
      const formulario = caixa.querySelector('form');
      if (formulario) formulario.reset();
      const aviso = caixa.querySelector('.feedback');
      if (aviso) aviso.textContent = '';
    });
  });
})();

/* =============================================================================
   Aparecimento suave das seções ao rolar
   Uma vez visível, a seção fica visível — sem "piscar" ao rolar para cima e
   para baixo. Sem IntersectionObserver no navegador, as seções já nascem
   visíveis (a classe .reveal só esconde depois de confirmado que o recurso existe).
   ============================================================================= */
(function () {
  const secoes = document.querySelectorAll('.reveal');
  if (!secoes.length) return;
  if (!('IntersectionObserver' in window)) {
    secoes.forEach(secao => secao.classList.add('em-vista'));
    return;
  }
  const observador = new IntersectionObserver(entradas => {
    entradas.forEach(entrada => {
      if (entrada.isIntersecting) {
        entrada.target.classList.add('em-vista');
        observador.unobserve(entrada.target);
      }
    });
  }, { threshold: .12, rootMargin: '0px 0px -40px 0px' });
  secoes.forEach(secao => observador.observe(secao));
})();
