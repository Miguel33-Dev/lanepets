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
      <div class="loja-media">
        <span class="loja-selo loja-selo--${estado.chave}">${estado.texto}</span>
        <span class="loja-simbolo">${icone(produto.nome + ' ' + (produto.categoria || ''), 'racao')}</span>
      </div>
      <div class="loja-corpo">
        <span class="tag">${categoria}</span>
        <h3>${esc(produto.nome)}</h3>
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
    if (contagem) {
      contagem.textContent = lojaProdutos.length
        ? `${lista.length} ${lista.length === 1 ? 'produto' : 'produtos'}`
        : '';
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

    alvo.innerHTML = lista.map(cardProduto).join('');
  }

  function limparFiltrosLoja() {
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
    if (ordem) ordem.addEventListener('change', () => { lojaOrdem = ordem.value; desenharLoja(); });

    const caixa = $('#lojaCategorias');
    if (caixa) {
      caixa.addEventListener('click', evento => {
        const chip = evento.target.closest('.loja-chip');
        if (!chip) return;
        lojaCategoria = chip.dataset.categoria || '';
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

  function cardDepoimento(depoimento) {
    return `<article class="card depoimento">
      <span class="estrelas" aria-label="${depoimento.avaliacao} de 5 estrelas">${'★'.repeat(depoimento.avaliacao)}${'☆'.repeat(5 - depoimento.avaliacao)}</span>
      <blockquote>“${esc(depoimento.comentario)}”</blockquote>
      <cite>${esc(depoimento.nomeCliente)} · tutor(a) de ${esc(depoimento.nomePet)}</cite>
    </article>`;
  }

  /* ---------- Carregamento ----------------------------------------------- */
  $('#lista-servicos').innerHTML = esqueleto(6);
  $('#lista-produtos').innerHTML = esqueleto(4);
  $('#lista-depoimentos').innerHTML = esqueleto(3);
  $('#lista-seguros').innerHTML = '<p style="opacity:.7">Carregando planos…</p>';

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

  async function carregar() {
    await carregarSecao('#lista-servicos', '/api/public/servicos',
      cardServico, 'Serviços LanePets', 'Em breve, novos serviços. Fale com a nossa equipe para conhecer as opções.', 6);

    /* A lojinha nao passa mais pelo carregarSecao generico: ela guarda a lista
       inteira para a busca e os filtros trabalharem, e mostra o catalogo todo
       em vez dos seis primeiros itens. */
    await carregarLoja();

    try {
      const planos = await api('/api/public/seguros');
      $('#lista-seguros').innerHTML = planos.length
        ? planos.map(cardPlano).join('')
        : '<p style="opacity:.75">Nenhum plano disponível no momento. Fale com a nossa equipe.</p>';
      document.querySelectorAll('.contratar').forEach(botao => botao.addEventListener('click', () => {
        $('#plano-id').value = botao.dataset.id;
        $('#seguro-titulo').textContent = 'Plano ' + botao.dataset.nome;
        $('#modal-seguro').showModal();
      }));
    } catch (erro) {
      console.error(erro);
      $('#lista-seguros').innerHTML = '<p style="opacity:.75">Não foi possível carregar os planos agora.</p>';
    }

    await carregarSecao('#lista-depoimentos', '/api/public/depoimentos',
      cardDepoimento, 'Seja a primeira família a avaliar', 'Clientes LanePets podem enviar um depoimento para a nossa moderação.', 6);
  }

  /* ---------- Envio dos formulários --------------------------------------- */
  function enviando(botao, ativo, texto) {
    botao.disabled = ativo;
    if (ativo) { botao.dataset.textoOriginal = botao.textContent; botao.textContent = texto; }
    else if (botao.dataset.textoOriginal) { botao.textContent = botao.dataset.textoOriginal; }
  }

  $('#abrir-depoimento').addEventListener('click', () => $('#modal-depoimento').showModal());

  $('#enviar-depoimento').addEventListener('click', async evento => {
    evento.preventDefault();
    const botao = evento.currentTarget;
    const feedback = $('#dep-feedback');
    feedback.textContent = '';
    enviando(botao, true, 'Enviando…');
    try {
      const dados = await api('/api/public/depoimentos', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          nome: $('#dep-nome').value,
          telefone: $('#dep-telefone').value,
          pet: $('#dep-pet').value,
          avaliacao: Number($('#dep-avaliacao').value),
          comentario: $('#dep-comentario').value
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
