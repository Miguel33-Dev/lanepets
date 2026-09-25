/* =============================================================================
   LanePets — Clientes (painel administrativo).

   Esta camada e SO de apresentacao. A origem dos dados continua exatamente a
   mesma de antes:

       lanepets.db  ->  GET /api/admin/estado  ->  ponte admin-store.js  ->  tela
       tela         ->  POST /api/admin/sync/<lista>  ->  lanepets.db

   Nenhum numero desta tela e inventado: todos saem das listas "clientes",
   "pets" e "agendamentos" que a ponte entrega, e dos campos que a API ja
   devolve por cliente (qtdPets, qtdAgendamentos, qtdPedidos, criadoEm,
   status, origem, temConta).
   ============================================================================= */
(function () {
  'use strict';

  var POR_PAGINA = 12;

  var estado = {
    busca: '',
    status: '',        /* '', 'ativo', 'inativo' */
    origem: '',        /* '', 'portal_cliente', 'cadastro_painel' */
    pagina: 1,
    carregando: true
  };

  var pedidosPorCliente = null;   /* preenchido sob demanda, via API de pedidos */

  /* ----------------------------------------------------------------- */
  /* Utilidades                                                         */
  /* ----------------------------------------------------------------- */
  function lista(chave) {
    try { return JSON.parse(localStorage.getItem(chave)) || []; }
    catch (e) { return []; }
  }

  function esc(valor) {
    var d = document.createElement('div');
    d.textContent = valor === null || valor === undefined ? '' : String(valor);
    return d.innerHTML;
  }

  function el(id) { return document.getElementById(id); }

  function dataBr(iso) {
    if (!iso) return '—';
    var d = new Date(iso);
    return isNaN(d) ? '—' : d.toLocaleDateString('pt-BR');
  }

  function dataHoraBr(iso) {
    if (!iso) return '—';
    var d = new Date(iso);
    return isNaN(d) ? '—' : d.toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', year: '2-digit', hour: '2-digit', minute: '2-digit' });
  }

  function brl(v) {
    return Number(v || 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  }

  function iniciais(nome) {
    var partes = String(nome || '').trim().split(/\s+/).filter(Boolean);
    if (!partes.length) return '?';
    if (partes.length === 1) return partes[0].substring(0, 2).toUpperCase();
    return (partes[0][0] + partes[partes.length - 1][0]).toUpperCase();
  }

  /* Tom do avatar: derivado do proprio nome, entao o mesmo cliente tem sempre
     a mesma cor. Nao e dado, e so aparencia estavel. */
  function tomAvatar(texto) {
    var soma = 0, s = String(texto || '');
    for (var i = 0; i < s.length; i++) soma = (soma + s.charCodeAt(i)) % 997;
    return soma % 6;
  }

  function normal(v) {
    return String(v === null || v === undefined ? '' : v)
      .normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
  }

  function statusDe(c) {
    return String(c.status || 'ativo').toLowerCase() === 'inativo' ? 'inativo' : 'ativo';
  }

  function origemRotulo(c) {
    return c.origem === 'portal_cliente' ? 'Cadastro pelo site' : 'Cadastro pelo painel';
  }

  /* Versao curta, para caber na linha da lista. */
  function origemCurta(c) {
    return c.origem === 'portal_cliente' ? 'Site' : 'Painel';
  }

  /* ----------------------------------------------------------------- */
  /* Icones (mesmo tracado do restante do painel)                       */
  /* ----------------------------------------------------------------- */
  var ICO = {
    pata: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="7" cy="8" r="2"/><circle cx="12" cy="5.5" r="2"/><circle cx="17" cy="8" r="2"/><path d="M12 13c-3.3 0-6 2.1-6 4.7 0 1.6 1.5 2.3 3 1.8.9-.3 1.9-.5 3-.5s2.1.2 3 .5c1.5.5 3-.2 3-1.8 0-2.6-2.7-4.7-6-4.7Z"/></svg>',
    agenda: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4.5" width="18" height="16.5" rx="2.5"/><path d="M16 2.5v4M8 2.5v4M3 10h18"/></svg>',
    carrinho: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="9" cy="20" r="1.4"/><circle cx="18" cy="20" r="1.4"/><path d="M2.5 3h2.2l2.3 11.2a1.6 1.6 0 0 0 1.6 1.3h8.7a1.6 1.6 0 0 0 1.6-1.25L21 7H6"/></svg>',
    email: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="2.5" y="4.5" width="19" height="15" rx="2.5"/><path d="m3 7 9 6 9-6"/></svg>',
    fone: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M21 16.9v2.6a1.8 1.8 0 0 1-2 1.8 17.6 17.6 0 0 1-7.7-2.7 17.3 17.3 0 0 1-5.3-5.3A17.6 17.6 0 0 1 3.3 5.5 1.8 1.8 0 0 1 5.1 3.5h2.6a1.8 1.8 0 0 1 1.8 1.6c.1.9.3 1.7.6 2.5a1.8 1.8 0 0 1-.4 1.9l-1.1 1.1a14 14 0 0 0 5.3 5.3l1.1-1.1a1.8 1.8 0 0 1 1.9-.4c.8.3 1.6.5 2.5.6a1.8 1.8 0 0 1 1.6 1.9Z"/></svg>',
    olho: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M2 12s3.6-6.5 10-6.5S22 12 22 12s-3.6 6.5-10 6.5S2 12 2 12Z"/><circle cx="12" cy="12" r="2.8"/></svg>',
    lapis: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
    mais: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><circle cx="12" cy="5" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="12" cy="19" r="1"/></svg>',
    poder: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v9"/><path d="M6.6 6.6a8 8 0 1 0 10.8 0"/></svg>'
  };

  /* ----------------------------------------------------------------- */
  /* Filtros                                                            */
  /* ----------------------------------------------------------------- */
  function todos() { return lista('clientes'); }

  function filtrados() {
    var termo = normal(estado.busca);
    return todos().filter(function (c) {
      if (estado.status && statusDe(c) !== estado.status) return false;
      if (estado.origem) {
        var o = c.origem === 'portal_cliente' ? 'portal_cliente' : 'cadastro_painel';
        if (o !== estado.origem) return false;
      }
      if (!termo) return true;
      return [c.nome, c.email, c.telefone, c.endereco].some(function (v) {
        return normal(v).indexOf(termo) !== -1;
      });
    });
  }

  /* ----------------------------------------------------------------- */
  /* Cards de resumo — sempre sobre o total real, nao sobre o filtro     */
  /* ----------------------------------------------------------------- */
  function pintarResumo() {
    var clientes = todos();
    var ativos = clientes.filter(function (c) { return statusDe(c) === 'ativo'; }).length;
    var agora = new Date();
    var novos = clientes.filter(function (c) {
      if (!c.criadoEm) return false;
      var d = new Date(c.criadoEm);
      return !isNaN(d) && d.getMonth() === agora.getMonth() && d.getFullYear() === agora.getFullYear();
    }).length;

    el('statTotal').textContent = clientes.length;
    el('statAtivos').textContent = ativos;
    el('statInativos').textContent = clientes.length - ativos;
    el('statNovos').textContent = novos;
    el('numClientes').textContent = clientes.length;
    el('numClientesRotulo').textContent = clientes.length === 1 ? 'cliente cadastrado' : 'clientes cadastrados';
  }

  /* ----------------------------------------------------------------- */
  /* Lista                                                              */
  /* ----------------------------------------------------------------- */
  function esqueleto(qtd) {
    var uma =
      '<div class="cli-skeleton-row" aria-hidden="true">' +
        '<div class="skeleton sk-av"></div>' +
        '<div class="sk-linhas">' +
          '<div class="skeleton sk-l1"></div>' +
          '<div class="skeleton sk-l2"></div>' +
          '<div class="skeleton sk-l3"></div>' +
        '</div>' +
        '<div class="skeleton sk-acao"></div>' +
      '</div>';
    var html = '';
    for (var i = 0; i < (qtd || 5); i++) html += uma;
    return html;
  }

  function pill(icone, valor, rotulo) {
    var n = Number(valor || 0);
    return '<span class="cli-pill" data-zero="' + (n ? 0 : 1) + '" title="' + esc(rotulo) + '">' +
      icone + '<b>' + n + '</b> ' + esc(rotulo) + '</span>';
  }

  function linhaCliente(c) {
    var st = statusDe(c);
    var nome = c.nome || 'Sem nome';
    var av = c.foto
      ? '<span class="cli-avatar"><img src="' + esc(c.foto) + '" alt=""></span>'
      : '<span class="cli-avatar" data-tom="' + tomAvatar(nome) + '" aria-hidden="true">' + esc(iniciais(nome)) + '</span>';

    return '<article class="cli-row" data-id="' + esc(c.id) + '" data-status="' + st + '">' +
      av +
      '<div class="cli-ident">' +
        '<div class="cli-nome" title="' + esc(nome) + '">' + esc(nome) + '</div>' +
        '<div class="cli-sub">' +
          '<span>' + esc(origemCurta(c)) + '</span>' +
          '<span class="sep">&middot;</span>' +
          '<span>desde ' + esc(dataBr(c.criadoEm)) + '</span>' +
        '</div>' +
      '</div>' +
      '<div class="cli-contato">' +
        '<span title="' + esc(c.email || '') + '">' + ICO.email + esc(c.email || '—') + '</span>' +
        '<span>' + ICO.fone + esc(c.telefone || '—') + '</span>' +
      '</div>' +
      '<div class="cli-metricas">' +
        pill(ICO.pata, c.qtdPets, 'pets') +
        pill(ICO.agenda, c.qtdAgendamentos, 'agend.') +
        pill(ICO.carrinho, c.qtdPedidos, 'pedidos') +
      '</div>' +
      '<span class="cli-status" data-status="' + st + '"><i></i>' + (st === 'ativo' ? 'Ativo' : 'Inativo') + '</span>' +
      '<div class="cli-acoes">' +
        '<button type="button" class="icon-btn" data-acao="ver" data-dica="Ver detalhes" aria-label="Ver detalhes de ' + esc(nome) + '">' + ICO.olho + '</button>' +
        '<button type="button" class="icon-btn" data-acao="editar" data-permissao="clientes:editar" data-dica="Editar" aria-label="Editar ' + esc(nome) + '">' + ICO.lapis + '</button>' +
        '<div class="cli-mais">' +
          '<button type="button" class="icon-btn" data-acao="mais" data-dica="Mais ações" aria-haspopup="true" aria-expanded="false" aria-label="Mais ações">' + ICO.mais + '</button>' +
          '<div class="cli-menu" role="menu">' +
            '<button type="button" role="menuitem" data-acao="ver">' + ICO.olho + 'Ver detalhes</button>' +
            '<button type="button" role="menuitem" data-acao="pets">' + ICO.pata + 'Ver pets</button>' +
            '<button type="button" role="menuitem" data-acao="agendamentos">' + ICO.agenda + 'Ver agendamentos</button>' +
            '<button type="button" role="menuitem" data-acao="pedidos">' + ICO.carrinho + 'Ver pedidos</button>' +
            '<hr>' +
            '<button type="button" role="menuitem" data-acao="editar" data-permissao="clientes:editar">' + ICO.lapis + 'Editar cliente</button>' +
            '<button type="button" role="menuitem" data-acao="alternar" data-permissao="clientes:editar" class="' + (st === 'ativo' ? 'perigo' : '') + '">' + ICO.poder + (st === 'ativo' ? 'Desativar cliente' : 'Reativar cliente') + '</button>' +
          '</div>' +
        '</div>' +
      '</div>' +
    '</article>';
  }

  function vazio(comBusca) {
    if (comBusca) {
      return '<div class="cli-vazio">' +
        '<div class="cli-vazio-ico"><svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/></svg></div>' +
        '<h3>Nenhum cliente encontrado para sua busca.</h3>' +
        '<p>Tente outro nome, e-mail ou telefone, ou remova os filtros aplicados.</p>' +
        '<button type="button" class="btn btn-outline" data-acao="limpar-tudo">Limpar pesquisa</button>' +
      '</div>';
    }
    return '<div class="cli-vazio">' +
      '<div class="cli-vazio-ico"><svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M16 20v-1.8a3.6 3.6 0 0 0-3.6-3.6H6.6A3.6 3.6 0 0 0 3 18.2V20"/><circle cx="9.5" cy="7.5" r="3.4"/><path d="M21 20v-1.8a3.6 3.6 0 0 0-2.7-3.48"/><path d="M15.5 4.2a3.4 3.4 0 0 1 0 6.6"/></svg></div>' +
      '<h3>Nenhum cliente encontrado</h3>' +
      '<p>Quando novos clientes criarem suas contas no site, eles aparecerão aqui automaticamente.</p>' +
    '</div>';
  }

  function pintarLista() {
    var alvo = el('listaClientes');
    if (!alvo) return;

    if (estado.carregando) {
      alvo.innerHTML = esqueleto(5);
      el('paginacao').innerHTML = '';
      el('resultadoInfo').textContent = 'Carregando clientes…';
      return;
    }

    var visiveis = filtrados();
    var totalGeral = todos().length;

    if (!visiveis.length) {
      alvo.innerHTML = vazio(totalGeral > 0);
      el('paginacao').innerHTML = '';
      el('resultadoInfo').textContent = totalGeral ? '0 clientes no filtro atual' : '';
      return;
    }

    var totalPaginas = Math.max(1, Math.ceil(visiveis.length / POR_PAGINA));
    if (estado.pagina > totalPaginas) estado.pagina = totalPaginas;
    var inicio = (estado.pagina - 1) * POR_PAGINA;
    var pagina = visiveis.slice(inicio, inicio + POR_PAGINA);

    alvo.innerHTML = pagina.map(linhaCliente).join('');
    el('resultadoInfo').textContent =
      'Mostrando ' + (inicio + 1) + '–' + (inicio + pagina.length) + ' de ' + visiveis.length +
      (visiveis.length === 1 ? ' cliente' : ' clientes');

    pintarPaginacao(totalPaginas);

    /* Reaplica as permissoes nos botoes recem-criados: quem nao pode editar
       nao ve o botao. A regra de verdade continua no backend. */
    if (window.LanePermissoes && typeof window.LanePermissoes.ajustarElementos === 'function') {
      window.LanePermissoes.ajustarElementos(alvo);
    }
  }

  function pintarPaginacao(totalPaginas) {
    var nav = el('paginacao');
    if (totalPaginas <= 1) { nav.innerHTML = ''; return; }

    var atual = estado.pagina;
    var paginas = [];
    for (var p = 1; p <= totalPaginas; p++) {
      if (p === 1 || p === totalPaginas || Math.abs(p - atual) <= 1) paginas.push(p);
      else if (paginas[paginas.length - 1] !== '…') paginas.push('…');
    }

    nav.innerHTML =
      '<button type="button" data-pagina="' + (atual - 1) + '"' + (atual === 1 ? ' disabled' : '') + '>&larr; Anterior</button>' +
      paginas.map(function (p) {
        if (p === '…') return '<span class="reticencias">…</span>';
        return '<button type="button" data-pagina="' + p + '"' + (p === atual ? ' aria-current="page"' : '') + '>' + p + '</button>';
      }).join('') +
      '<button type="button" data-pagina="' + (atual + 1) + '"' + (atual === totalPaginas ? ' disabled' : '') + '>Próxima &rarr;</button>';
  }

  /* ----------------------------------------------------------------- */
  /* Detalhes do cliente                                                */
  /* ----------------------------------------------------------------- */
  function petsDoCliente(id) {
    return lista('pets').filter(function (p) { return p.clienteId === id; });
  }

  function agendamentosDoCliente(id) {
    return lista('agendamentos')
      .filter(function (a) { return a.clienteId === id; })
      .sort(function (a, b) { return String(b.dataHora || '').localeCompare(String(a.dataHora || '')); });
  }

  /* Os pedidos nao vem na ponte; usamos o mesmo endpoint que a tela de
     Pedidos ja usa. Sem permissao de pedidos, a secao simplesmente some. */
  function carregarPedidos() {
    if (pedidosPorCliente) return Promise.resolve(pedidosPorCliente);
    if (!window.LaneAdmin || typeof window.LaneAdmin.pedidos !== 'function') return Promise.resolve(null);
    return window.LaneAdmin.pedidos()
      .then(function (todosPedidos) {
        pedidosPorCliente = {};
        (todosPedidos || []).forEach(function (p) {
          var k = p.clienteId || p.ClienteId || '';
          (pedidosPorCliente[k] = pedidosPorCliente[k] || []).push(p);
        });
        return pedidosPorCliente;
      })
      .catch(function () { return null; });
  }

  function miniLista(itens, vazioTexto) {
    if (!itens.length) return '<p class="cli-mini-vazio">' + esc(vazioTexto) + '</p>';
    return '<div class="cli-mini">' + itens.join('') + '</div>';
  }

  window.abrirDetalheCliente = function (id, secao) {
    var c = todos().filter(function (x) { return x.id === id; })[0];
    if (!c) return;

    var st = statusDe(c);
    var nome = c.nome || 'Sem nome';
    var pets = petsDoCliente(id);
    var ags = agendamentosDoCliente(id);

    el('detHead').innerHTML =
      (c.foto
        ? '<span class="cli-avatar"><img src="' + esc(c.foto) + '" alt=""></span>'
        : '<span class="cli-avatar" data-tom="' + tomAvatar(nome) + '" aria-hidden="true">' + esc(iniciais(nome)) + '</span>') +
      '<div style="min-width:0">' +
        '<h2>' + esc(nome) + '</h2>' +
        '<span class="cli-status" data-status="' + st + '"><i></i>' + (st === 'ativo' ? 'Cliente ativo' : 'Cliente inativo') + '</span>' +
      '</div>';

    el('detCorpo').innerHTML =
      '<section class="cli-det-sec">' +
        '<h3>Informações pessoais</h3>' +
        '<dl class="cli-def">' +
          '<div><dt>Nome</dt><dd>' + esc(nome) + '</dd></div>' +
          '<div><dt>E-mail</dt><dd>' + esc(c.email || '—') + '</dd></div>' +
          '<div><dt>Telefone</dt><dd>' + esc(c.telefone || '—') + '</dd></div>' +
          '<div><dt>Cadastro</dt><dd>' + esc(dataBr(c.criadoEm)) + '</dd></div>' +
          '<div><dt>Endereço</dt><dd>' + esc(c.endereco || '—') + '</dd></div>' +
          '<div><dt>Origem</dt><dd>' + esc(origemRotulo(c)) + '</dd></div>' +
          (c.observacoes ? '<div><dt>Observações</dt><dd>' + esc(c.observacoes) + '</dd></div>' : '') +
        '</dl>' +
      '</section>' +

      '<section class="cli-det-sec">' +
        '<h3>Resumo</h3>' +
        '<div class="cli-resumo">' +
          '<div><div class="n">' + pets.length + '</div><div class="r">Pets</div></div>' +
          '<div><div class="n">' + (c.qtdAgendamentos || ags.length || 0) + '</div><div class="r">Agendamentos</div></div>' +
          '<div><div class="n">' + (c.qtdPedidos || 0) + '</div><div class="r">Pedidos</div></div>' +
          '<div><div class="n">' + (c.temConta ? 'Sim' : 'Não') + '</div><div class="r">Conta no site</div></div>' +
        '</div>' +
      '</section>' +

      '<section class="cli-det-sec" id="secPets">' +
        '<h3>Pets</h3>' +
        miniLista(pets.map(function (p) {
          return '<div class="cli-mini-item"><span><strong>' + esc(p.pet || '—') + '</strong>' +
            (p.raca ? ' · ' + esc(p.raca) : '') + '</span>' +
            '<span class="quando">' + esc(p.tipo || '—') + '</span></div>';
        }), 'Nenhum pet vinculado a este cliente.') +
      '</section>' +

      '<section class="cli-det-sec" id="secAgendamentos">' +
        '<h3>Agendamentos</h3>' +
        miniLista(ags.slice(0, 8).map(function (a) {
          return '<div class="cli-mini-item"><span><strong>' + esc(a.pet || '—') + '</strong> · ' + esc(a.status || 'Pendente') + '</span>' +
            '<span class="quando">' + esc(dataHoraBr(a.dataHora)) + '</span></div>';
        }), 'Nenhum agendamento registrado.') +
        (ags.length > 8 ? '<p class="cli-mini-vazio">Mostrando os 8 mais recentes de ' + ags.length + '.</p>' : '') +
      '</section>' +

      '<section class="cli-det-sec" id="secPedidos">' +
        '<h3>Pedidos</h3>' +
        '<p class="cli-mini-vazio">Carregando pedidos…</p>' +
      '</section>';

    var btnEd = el('btnDetEditar');
    if (btnEd) btnEd.dataset.id = id;

    el('modalDetalhe').classList.add('ativo');

    carregarPedidos().then(function (mapa) {
      var alvo = el('secPedidos');
      if (!alvo) return;
      if (!mapa) { alvo.parentNode.removeChild(alvo); return; }
      var meus = (mapa[id] || []).slice(0, 8);
      alvo.innerHTML = '<h3>Pedidos</h3>' + miniLista(meus.map(function (p) {
        return '<div class="cli-mini-item"><span><strong>' + esc(p.produtoNome || p.ProdutoNome || '—') + '</strong> · ' +
          esc(p.status || p.Status || '—') + '</span>' +
          '<span class="quando">' + brl(p.total || p.Total) + ' · ' + esc(dataBr(p.criadoEm)) + '</span></div>';
      }), 'Nenhum pedido registrado.');
    });

    if (secao) {
      setTimeout(function () {
        var destino = el(secao);
        if (destino) destino.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }, 140);
    }
  };

  window.fecharDetalheCliente = function () {
    el('modalDetalhe').classList.remove('ativo');
  };

  /* ----------------------------------------------------------------- */
  /* Ativar / desativar — mesmo campo "status" e mesmo caminho de        */
  /* gravacao do modal de edicao que ja existia.                         */
  /* ----------------------------------------------------------------- */
  function alternarStatus(id) {
    var clientes = lista('clientes');
    var alvo = clientes.filter(function (c) { return c.id === id; })[0];
    if (!alvo) return;
    var vai = statusDe(alvo) === 'ativo' ? 'inativo' : 'ativo';

    window.confirmarAcao({
      titulo: vai === 'inativo' ? 'Desativar cliente?' : 'Reativar cliente?',
      texto: vai === 'inativo'
        ? 'O cadastro continua no banco, apenas marcado como inativo.'
        : 'O cliente volta a aparecer como ativo no painel.',
      corBotao: vai === 'inativo' ? 'btn-danger' : 'btn-primary',
      textoBotao: vai === 'inativo' ? 'Desativar' : 'Reativar'
    }).then(function (ok) {
      if (!ok) return;
      alvo.status = vai;
      localStorage.setItem('clientes', JSON.stringify(clientes));
      desenhar();
      window.toast(vai === 'inativo' ? 'Cliente marcado como inativo.' : 'Cliente reativado.', 'success');
    });
  }

  /* ----------------------------------------------------------------- */
  /* Eventos                                                            */
  /* ----------------------------------------------------------------- */
  function fecharMenus(exceto) {
    Array.prototype.forEach.call(document.querySelectorAll('.cli-menu.aberto'), function (m) {
      if (m === exceto) return;
      m.classList.remove('aberto');
      var linha = m.closest('.cli-row');
      if (linha) linha.classList.remove('menu-aberto');
      var botao = m.parentElement && m.parentElement.querySelector('[data-acao="mais"]');
      if (botao) botao.setAttribute('aria-expanded', 'false');
    });
  }

  function limparFiltros() {
    var campo = el('filtro');
    campo.value = '';
    estado.busca = ''; estado.status = ''; estado.origem = ''; estado.pagina = 1;
    el('caixaBusca').classList.remove('tem-texto');
    el('filtroOrigem').value = '';
    Array.prototype.forEach.call(el('segStatus').querySelectorAll('button'), function (b) {
      b.setAttribute('aria-pressed', String(b.dataset.status === ''));
    });
    pintarLista();
  }

  function ligarEventos() {
    var campo = el('filtro');
    var caixa = el('caixaBusca');

    function aplicarBusca() {
      estado.busca = campo.value;
      estado.pagina = 1;
      caixa.classList.toggle('tem-texto', !!campo.value.trim());
      pintarLista();
    }
    campo.addEventListener('input', aplicarBusca);
    campo.addEventListener('keydown', function (e) {
      if (e.key === 'Enter') { e.preventDefault(); aplicarBusca(); }
    });

    el('btnLimparBusca').addEventListener('click', function () {
      campo.value = '';
      campo.focus();
      aplicarBusca();
    });

    el('segStatus').addEventListener('click', function (e) {
      var botao = e.target.closest('button[data-status]');
      if (!botao) return;
      estado.status = botao.dataset.status;
      estado.pagina = 1;
      Array.prototype.forEach.call(this.querySelectorAll('button'), function (b) {
        b.setAttribute('aria-pressed', String(b === botao));
      });
      pintarLista();
    });

    el('filtroOrigem').addEventListener('change', function () {
      estado.origem = this.value;
      estado.pagina = 1;
      pintarLista();
    });

    el('paginacao').addEventListener('click', function (e) {
      var botao = e.target.closest('button[data-pagina]');
      if (!botao || botao.disabled) return;
      estado.pagina = Number(botao.dataset.pagina) || 1;
      pintarLista();
      el('cardLista').scrollIntoView({ behavior: 'smooth', block: 'start' });
    });

    el('listaClientes').addEventListener('click', function (e) {
      if (e.target.closest('[data-acao="limpar-tudo"]')) { limparFiltros(); return; }

      var botao = e.target.closest('button[data-acao]');
      if (!botao) return;
      var linha = botao.closest('.cli-row');
      if (!linha) return;
      var id = linha.dataset.id;
      var acao = botao.dataset.acao;

      if (acao === 'mais') {
        var menu = linha.querySelector('.cli-menu');
        var abrindo = !menu.classList.contains('aberto');
        fecharMenus(menu);
        menu.classList.toggle('aberto', abrindo);
        linha.classList.toggle('menu-aberto', abrindo);
        botao.setAttribute('aria-expanded', String(abrindo));
        return;
      }

      fecharMenus();

      if (acao === 'ver') window.abrirDetalheCliente(id);
      else if (acao === 'editar') window.editarClienteConta(id);
      else if (acao === 'pets') window.abrirDetalheCliente(id, 'secPets');
      else if (acao === 'agendamentos') window.abrirDetalheCliente(id, 'secAgendamentos');
      else if (acao === 'pedidos') window.abrirDetalheCliente(id, 'secPedidos');
      else if (acao === 'alternar') alternarStatus(id);
    });

    document.addEventListener('click', function (e) {
      if (!e.target.closest('.cli-mais')) fecharMenus();
    });
    document.addEventListener('keydown', function (e) {
      if (e.key !== 'Escape') return;
      fecharMenus();
      if (el('modalDetalhe').classList.contains('ativo')) window.fecharDetalheCliente();
    });

    el('modalDetalhe').addEventListener('mousedown', function (e) {
      if (e.target === this) window.fecharDetalheCliente();
    });

    var btnDetEditar = el('btnDetEditar');
    if (btnDetEditar) {
      btnDetEditar.addEventListener('click', function () {
        var id = this.dataset.id;
        window.fecharDetalheCliente();
        if (id) window.editarClienteConta(id);
      });
    }
  }

  /* ----------------------------------------------------------------- */
  /* Ciclo de vida                                                      */
  /* ----------------------------------------------------------------- */
  function desenhar() {
    estado.carregando = false;
    pintarResumo();
    pintarLista();
    if (typeof window.carregarTabelaPets === 'function') window.carregarTabelaPets();
  }
  window.desenharClientes = desenhar;

  document.addEventListener('DOMContentLoaded', function () {
    ligarEventos();
    pintarLista();   /* esqueleto enquanto a ponte confirma os dados */
    setTimeout(desenhar, 80);
    window.addEventListener('lanepets:dados', desenhar);
  });
})();
