/* =============================================================================
   LanePets — Área do cliente
   Autenticação (login / criar conta) + portal do cliente.
   As rotas, o backend e as chamadas de API continuam exatamente as mesmas;
   aqui mudaram apenas a validação visual, os estados e a troca de painéis.
   ============================================================================= */
(function () {
  const UI = window.LanePetsAuthUI;
  const $ = s => document.querySelector(s);

  let token = localStorage.getItem('lanePetsClienteToken') || '';
  if (token) $('#auth').style.display = 'none';

  const api = (path, options = {}) => LanePetsHttp.request(`cliente/${path}`, {
    ...options,
    headers: { ...(options.headers || {}), 'Content-Type': 'application/json', 'X-LanePets-Client': token }
  });

  const esc = v => { const d = document.createElement('div'); d.textContent = v || ''; return d.innerHTML; };
  const options = (el, rows, label) => el.innerHTML = rows.map(r => `<option value="${r.id}">${esc(label(r))}</option>`).join('');
  const message = (id, e) => $(id).textContent = e.message;

  const formLogin = $('#form-login');
  const formCadastro = $('#form-cadastro');

  /* ---------------------------------------------------------------------
     Interface das telas de autenticação
     --------------------------------------------------------------------- */
  UI.ligarOlhos(document);
  UI.ligarCampos(formLogin);
  UI.ligarCampos(formCadastro);
  UI.mascaraTelefone($('#cad-tel'));
  UI.validarNoBlur($('#login-email'), v => UI.EMAIL_RE.test(v));
  UI.validarNoBlur($('#cad-email'), v => UI.EMAIL_RE.test(v));
  UI.validarNoBlur($('#cad-nome'), v => v.length >= 3);
  UI.validarNoBlur($('#cad-tel'), v => v.replace(/\D/g, '').length >= 10);
  UI.validarNoBlur($('#cad-pet'), v => v.length >= 2);

  function mostrarPainel(nome) {
    document.querySelectorAll('.lp-painel').forEach(p => p.classList.remove('is-ativo'));
    $(`#painel-${nome}`).classList.add('is-ativo');
    $('#cartao-cliente').classList.toggle('lp-card--largo', nome === 'cadastro');
    UI.alerta('#login-aviso', '');
    UI.alerta('#login-info', '');
    UI.alerta('#cad-aviso', '');
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  $('#ir-cadastro').addEventListener('click', e => { e.preventDefault(); mostrarPainel('cadastro'); $('#cad-nome').focus(); });
  $('#ir-login').addEventListener('click', e => { e.preventDefault(); mostrarPainel('login'); $('#login-email').focus(); });
  $('#sucesso-continuar').addEventListener('click', () => { mostrarPainel('login'); $('#login-senha').focus(); });
  $('#esqueci').addEventListener('click', e => {
    e.preventDefault();
    UI.alerta('#login-info', 'Para redefinir sua senha, fale com a equipe LanePets em uma das nossas unidades.');
  });
  if (location.hash === '#cadastro') mostrarPainel('cadastro');

  /* Medidor de força da senha ------------------------------------------- */
  /* Mesma regra do servidor (Services/Validacao.cs, item 13): 8+ caracteres,
     com pelo menos uma letra e um número. O servidor decide; isto é só aviso. */
  const NIVEIS = ['Mín. 8, com letra e número', 'Falta letra ou número', 'Boa', 'Forte'];
  const senhaValida = v => v.length >= 8 && /\p{L}/u.test(v) && /\d/.test(v) && v.trim() === v;
  $('#cad-senha').addEventListener('input', event => {
    const v = event.target.value;
    let nivel = 0;
    if (v.length >= 8) nivel = 1;
    if (senhaValida(v)) nivel = 2;
    if (senhaValida(v) && v.length >= 10 && /[^\p{L}\d]/u.test(v)) nivel = 3;
    $('#forca-senha').dataset.nivel = String(nivel);
    $('#forca-texto').textContent = NIVEIS[nivel];
  });

  /* =====================================================================
     ÁREA DO CLIENTE
     Mesmas rotas de sempre + as rotas novas PUT conta, POST pets e
     GET/POST avaliacoes. Todos os números exibidos vêm da API.
     ===================================================================== */

  let conta = null;      // resposta de GET /api/cliente/conta
  let catalogo = null;   // resposta de GET /api/cliente/catalogo

  const brl = v => Number(v || 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });

  function dataHora(valor) {
    const d = new Date(valor);
    if (isNaN(d)) return String(valor || '—');
    return d.toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }
  function dataCurta(valor) {
    const d = new Date(valor);
    if (isNaN(d)) return '—';
    return d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric' });
  }
  function iniciais(nome) {
    const partes = String(nome || '').trim().split(/\s+/).filter(Boolean);
    if (!partes.length) return '··';
    return (partes[0][0] + (partes.length > 1 ? partes[partes.length - 1][0] : '')).toUpperCase();
  }

  /* Ícones -------------------------------------------------------------- */
  const ico = (caminho, tamanho = 18) =>
    `<svg width="${tamanho}" height="${tamanho}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${caminho}</svg>`;

  const ICO = {
    pata: '<circle cx="6.2" cy="9" r="1.9"/><circle cx="11" cy="6.4" r="1.9"/><circle cx="15.8" cy="9" r="1.9"/><path d="M11 13.4c-3.1 0-5.6 2-5.6 4.4 0 1.5 1.4 2.2 2.8 1.7.85-.3 1.8-.45 2.8-.45s1.95.15 2.8.45c1.4.5 2.8-.2 2.8-1.7 0-2.4-2.5-4.4-5.6-4.4Z"/>',
    agenda: '<rect x="3" y="4.5" width="18" height="16.5" rx="2.5"/><path d="M16 2.5v4M8 2.5v4M3 10h18"/>',
    carrinho: '<path d="M4 5h2l2.2 10.4a1.6 1.6 0 0 0 1.6 1.3h7.7a1.6 1.6 0 0 0 1.6-1.3L21 8H6.4"/><circle cx="10" cy="20" r="1.3"/><circle cx="18" cy="20" r="1.3"/>',
    escudo: '<path d="M12 2.8 4 6v6c0 4.6 3.4 8.2 8 9.2 4.6-1 8-4.6 8-9.2V6Z"/><path d="m9 12 2 2 4-4"/>',
    cartao: '<rect x="2.5" y="5.5" width="19" height="13" rx="2.5"/><path d="M2.5 10h19"/><path d="M6.5 14.5h4"/>',
    relogio: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5.2l3.2 1.9"/>',
    local: '<path d="M12 21s7-5.6 7-11a7 7 0 1 0-14 0c0 5.4 7 11 7 11Z"/><circle cx="12" cy="10" r="2.6"/>',
    balao: '<path d="M21 12.5a7.5 7.5 0 0 1-7.5 7.5c-1.2 0-2.4-.3-3.4-.8L4 21l1.8-5.6A7.5 7.5 0 1 1 21 12.5Z"/>',
    gato: '<path d="M5.6 10.2 6.6 4.2l4.3 3.1M18.4 10.2 17.4 4.2l-4.3 3.1"/><path d="M4.8 13.6a7.2 7.2 0 0 1 14.4 0v1.6a7.2 7.2 0 0 1-14.4 0Z"/><path d="M9.4 13.2h.01M14.6 13.2h.01"/><path d="M12 15.6v1.1M10.5 17.6a2.1 2.1 0 0 0 3 0"/>',
    seta: '<path d="M5 12h13"/><path d="m12.5 6 5.5 6-5.5 6"/>',
    pessoa: '<circle cx="12" cy="8" r="3.6"/><path d="M4.5 20.5a7.5 7.5 0 0 1 15 0"/>',
    etiqueta: '<path d="M3.5 11.2V4.5a1 1 0 0 1 1-1h6.7a1 1 0 0 1 .7.3l8.1 8.1a1 1 0 0 1 0 1.4l-6.7 6.7a1 1 0 0 1-1.4 0L3.8 11.9a1 1 0 0 1-.3-.7Z"/><path d="M7.5 7.5h.01"/>'
  };

  const iconePet = tipo => /gat/i.test(String(tipo || '')) ? ICO.gato : ICO.pata;

  /* Selos de status ------------------------------------------------------ */
  const CLASSE_STATUS = {
    'confirmado': 'ok', 'concluído': 'ok', 'concluido': 'ok', 'pago': 'ok', 'aprovado': 'ok', 'ativo': 'ok', 'entregue': 'ok',
    'pendente': 'aguarda', 'solicitado': 'aguarda', 'reembolsado': 'info', 'em andamento': 'info', 'em contato': 'info', 'em preparo': 'info', 'separado': 'info',
    'cancelado': 'erro', 'recusado': 'erro', 'inativo': 'erro'
  };
  const selo = valor => {
    let texto = String(valor || '—').trim();
    texto = texto ? texto[0].toUpperCase() + texto.slice(1) : '—';
    return `<span class="cc-selo cc-selo--${CLASSE_STATUS[texto.toLowerCase()] || 'neutro'}">${esc(texto)}</span>`;
  };

  /* Blocos reutilizáveis ------------------------------------------------- */
  const vazio = (titulo, texto, acao = '') => `
    <div class="cc-vazio">
      ${ico(ICO.pata, 40)}
      <h3>${titulo}</h3>
      <p>${texto}</p>
      ${acao}
    </div>`;

  const linha = ({ icone, titulo, meta, fim }) => `
    <div class="cc-linha">
      <span class="cc-linha__ico">${ico(icone, 19)}</span>
      <div><div class="cc-linha__titulo">${titulo}</div><div class="cc-linha__meta">${meta}</div></div>
      <div class="cc-linha__fim">${fim}</div>
    </div>`;

  /* Card de metrica. Sem `rota` continua sendo um <article> estatico (e assim
     que a tela de Pagamentos ja o usava). Com `rota`, vira um link para a
     secao correspondente — a navegacao e a mesma do menu lateral. */
  const metrica = ({ icone, rotulo, valor, nota, acento, ok, rota, acao }) => {
    const classes = 'cc-metrica'
      + (acento ? ' cc-metrica--acento' : '')
      + (ok ? ' cc-metrica--ok' : '');
    const corpo = `
      <div class="cc-metrica__topo">
        <span class="cc-metrica__ico">${ico(icone, 19)}</span>
      </div>
      <div class="cc-metrica__rotulo">${rotulo}</div>
      <div class="cc-metrica__valor">${valor}</div>
      <div class="cc-metrica__nota">${nota}</div>
      ${rota ? `<span class="cc-metrica__acao">${acao || 'Ver'} ${ico(ICO.seta, 14)}</span>` : ''}`;
    return rota
      ? `<a class="${classes}" href="#${rota}" data-rota="${rota}">${corpo}</a>`
      : `<article class="${classes}">${corpo}</article>`;
  };

  /* ---------------------------------------------------------------------
     Navegação entre as seções (sem recarregar a página)
     --------------------------------------------------------------------- */
  const ROTAS = {
    'minha-conta':   ['Início', 'Área do cliente / Visão geral'],
    'meus-pets':     ['Meus pets', 'Área do cliente / Pets'],
    'agendar':       ['Agendamentos', 'Área do cliente / Serviços'],
    'historico':     ['Histórico de serviços', 'Área do cliente / Serviços'],
    'produtos':      ['Produtos', 'Área do cliente / Loja'],
    'meus-pedidos':  ['Meus Pedidos', 'Área do cliente / Histórico'],
    'pagamentos':    ['Pagamentos', 'Área do cliente / Financeiro'],
    'seguro':        ['Seguro Pet', 'Área do cliente / Proteção'],
    'avaliacoes':    ['Avaliações', 'Área do cliente / Experiência'],
    'configuracoes': ['Minha Conta', 'Área do cliente / Conta']
  };
  const APELIDOS = { agendamentos: 'agendar', pedidos: 'meus-pedidos', 'meus-pedidos': 'meus-pedidos', loja: 'produtos', pets: 'meus-pets', conta: 'minha-conta', perfil: 'minha-conta', 'historico-servicos': 'historico' };

  function irPara(rota, atualizarHash = true) {
    rota = APELIDOS[rota] || rota;
    if (!ROTAS[rota]) rota = 'minha-conta';
    document.querySelectorAll('.cc-secao').forEach(s => s.classList.toggle('is-ativa', s.id === `sec-${rota}`));
    document.querySelectorAll('.cc-nav a[data-rota]').forEach(a => {
      const ativo = a.dataset.rota === rota;
      a.classList.toggle('is-ativo', ativo);
      if (ativo) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
    });
    const titulo = $('#cc-titulo'), trilha = $('#cc-trilha');
    if (titulo) titulo.textContent = ROTAS[rota][0];
    if (trilha) trilha.textContent = ROTAS[rota][1];
    if (atualizarHash && location.hash !== `#${rota}`) history.replaceState(null, '', `#${rota}`);
    fecharMenu();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  function rotaDoHash() {
    const bruto = (location.hash || '').replace('#', '').trim();
    return APELIDOS[bruto] || (ROTAS[bruto] ? bruto : 'minha-conta');
  }

  /* Menu lateral no celular --------------------------------------------- */
  const abrirMenu = () => {
    const lateral = $('#cc-lateral'), fundo = $('#cc-fundo');
    if (lateral) lateral.classList.add('is-aberta');
    if (fundo) { fundo.hidden = false; fundo.classList.add('is-visivel'); }
  };
  const fecharMenu = () => {
    const lateral = $('#cc-lateral'), fundo = $('#cc-fundo');
    if (lateral) lateral.classList.remove('is-aberta');
    if (fundo) { fundo.classList.remove('is-visivel'); fundo.hidden = true; }
  };

  /* ---------------------------------------------------------------------
     Renderização de cada seção — todos os valores vêm de `conta`
     --------------------------------------------------------------------- */
  function proximoAgendamento() {
    const agora = Date.now();
    return conta.agendamentos
      .filter(a => String(a.status).toLowerCase() !== 'cancelado' && new Date(a.dataHora).getTime() >= agora)
      .sort((a, b) => new Date(a.dataHora) - new Date(b.dataHora))[0] || null;
  }

  function servicosDo(agendamento) {
    try {
      const lista = JSON.parse(agendamento.servicosJson || '[]');
      return lista.map(s => s.nome).filter(Boolean).join(' + ') || 'Serviço LanePets';
    } catch (_) { return 'Serviço LanePets'; }
  }

  function renderResumo() {
    const agora = Date.now();
    const futuros = conta.agendamentos.filter(a => String(a.status).toLowerCase() !== 'cancelado' && new Date(a.dataHora).getTime() >= agora).length;
    const pedidosAbertos = conta.pedidos.filter(p => String(p.status).toLowerCase() === 'pendente').length;
    const gasto = conta.agendamentos.filter(a => String(a.status).toLowerCase() !== 'cancelado').reduce((s, a) => s + Number(a.total || 0), 0)
                + conta.pedidos.filter(p => String(p.status).toLowerCase() !== 'cancelado').reduce((s, p) => s + Number(p.total || 0), 0);

    /* Seguro Pet: so conta contrato que nao foi cancelado. Enquanto a lista
       de contratos ainda nao chegou do servidor, o card mostra "—" em vez de
       um zero que poderia ser lido como "voce nao tem seguro". */
    const segurosAtivos = segurosContratados.filter(s => String(s.status || '').toLowerCase() !== 'cancelada');
    const seguroValor = segurosCarregados ? (segurosAtivos.length ? 'Ativo' : 'Nenhum') : '—';
    const seguroNota = !segurosCarregados
      ? 'Carregando seus contratos'
      : segurosAtivos.length
        ? `${segurosAtivos.length} contrato${segurosAtivos.length > 1 ? 's' : ''} em vigor`
        : 'Nenhum plano contratado';

    $('#cc-resumo').innerHTML = [
      metrica({
        icone: ICO.pata, rotulo: 'Meus pets', valor: conta.pets.length,
        nota: conta.pets.length ? (conta.pets.length === 1 ? '1 pet cadastrado' : `${conta.pets.length} pets cadastrados`) : 'Nenhum pet cadastrado',
        rota: 'meus-pets', acao: 'Ver pets'
      }),
      metrica({
        icone: ICO.agenda, rotulo: 'Agendamentos', valor: conta.agendamentos.length,
        nota: futuros ? `${futuros} ${futuros === 1 ? 'próximo' : 'próximos'}` : 'Nenhum agendamento próximo',
        rota: 'agendar', acao: 'Ver agenda'
      }),
      metrica({
        icone: ICO.carrinho, rotulo: 'Meus pedidos', valor: conta.pedidos.length,
        nota: pedidosAbertos ? `${pedidosAbertos} em aberto` : (conta.pedidos.length ? 'Nenhum em aberto' : 'Nenhum pedido'),
        rota: 'meus-pedidos', acao: 'Ver pedidos'
      }),
      metrica({
        icone: ICO.escudo, rotulo: 'Seguro Pet', valor: seguroValor, nota: seguroNota,
        rota: 'seguro', acao: 'Ver seguro', ok: segurosCarregados && segurosAtivos.length > 0
      })
    ].join('');

    /* Mesmo calculo, versao compacta para o painel "Resumo" dentro de
       Minha Conta — reaproveita os numeros acima, nenhuma chamada nova. */
    html('#cc-resumo-conta', `
      <div class="cc-dado"><dt>Pets cadastrados</dt><dd>${conta.pets.length}</dd></div>
      <div class="cc-dado"><dt>Agendamentos</dt><dd>${conta.agendamentos.length}${futuros ? ` (${futuros} ${futuros === 1 ? 'próximo' : 'próximos'})` : ''}</dd></div>
      <div class="cc-dado"><dt>Pedidos</dt><dd>${conta.pedidos.length}${pedidosAbertos ? ` (${pedidosAbertos} em aberto)` : ''}</dd></div>
      <div class="cc-dado"><dt>Seguro Pet</dt><dd>${esc(seguroValor)}</dd></div>`);

    /* O total investido continua visivel na secao Pagamentos, que ja o exibe
       junto do que esta pago e do que falta confirmar. */
    void gasto;
  }

  /* ---------------------------------------------------------------------
     Blocos da visao geral: pets, pedidos recentes e produtos em destaque.
     Todos leem os mesmos dados que as secoes completas ja usam — nenhuma
     chamada nova ao servidor, nenhum valor inventado.
     --------------------------------------------------------------------- */
  function renderVisaoPets() {
    const alvo = $('#cc-pets-resumo');
    if (!alvo) return;

    const cabecalho = `
      <div class="cc-painel__topo">
        <div><h2>Meus pets</h2><p>Quem você cuida com a gente.</p></div>
        ${conta.pets.length ? '<a class="cc-ver-tudo" href="#meus-pets" data-rota="meus-pets">Ver todos ' + ico(ICO.seta, 14) + '</a>' : ''}
      </div>`;

    if (!conta.pets.length) {
      alvo.innerHTML = `<section class="cc-painel">${cabecalho}
        ${vazio('Nenhum pet cadastrado', 'Cadastre seu primeiro pet para agendar serviços e fazer pedidos.',
          '<button class="botao primario" type="button" data-ir="meus-pets">Adicionar pet</button>')}
      </section>`;
      return;
    }

    const cards = conta.pets.slice(0, 4).map(p => `
      <div class="cc-mini__item">
        <span class="cc-mini__ico">${ico(iconePet(p.tipo), 19)}</span>
        <div class="cc-mini__txt">
          <div class="cc-mini__titulo">${esc(p.petNome)}</div>
          <div class="cc-mini__meta">${esc(p.raca) || 'Raça não informada'}</div>
        </div>
        <div class="cc-mini__fim"><span class="cc-selo cc-selo--neutro">${esc(p.tipo) || 'Pet'}</span></div>
      </div>`).join('');

    alvo.innerHTML = `<section class="cc-painel">${cabecalho}
      <div class="cc-mini">${cards}</div>
      ${conta.pets.length > 4 ? `<p class="cc-nota" style="margin-top:12px">E mais ${conta.pets.length - 4} pet(s) em Meus pets.</p>` : ''}
    </section>`;
  }

  function renderPedidosRecentes() {
    const alvo = $('#cc-pedidos-recentes');
    if (!alvo) return;

    const cabecalho = `
      <div class="cc-painel__topo">
        <div><h2>Pedidos recentes</h2><p>O andamento das suas últimas compras.</p></div>
        ${conta.pedidos.length ? '<a class="cc-ver-tudo" href="#meus-pedidos" data-rota="meus-pedidos">Ver todos ' + ico(ICO.seta, 14) + '</a>' : ''}
      </div>`;

    if (!conta.pedidos.length) {
      alvo.innerHTML = `<section class="cc-painel">${cabecalho}
        ${vazio('Nenhum pedido ainda', 'Escolha um produto do catálogo para fazer o seu primeiro pedido.',
          '<button class="botao primario" type="button" data-ir="produtos">Ver produtos</button>')}
      </section>`;
      return;
    }

    const recentes = conta.pedidos
      .slice()
      .sort((a, b) => String(b.criadoEm).localeCompare(String(a.criadoEm)))
      .slice(0, 3);

    alvo.innerHTML = `<section class="cc-painel">${cabecalho}
      <div class="cc-mini">${recentes.map(p => `
        <div class="cc-mini__item">
          <span class="cc-mini__ico">${ico(ICO.carrinho, 19)}</span>
          <div class="cc-mini__txt">
            <div class="cc-mini__titulo">${esc(p.produtoNome)}</div>
            <div class="cc-mini__meta">${numeroPedido(p)} · ${dataCurta(p.criadoEm)}</div>
          </div>
          <div class="cc-mini__fim">${selo(p.status)}<span class="cc-mini__valor">${brl(p.total)}</span></div>
        </div>`).join('')}</div>
    </section>`;
  }

  /** Vitrine curta: 3 produtos reais do catalogo, com link para a loja. */
  function renderProdutosDestaque() {
    const alvo = $('#cc-produtos-destaque');
    if (!alvo) return;

    const produtos = ((catalogo && catalogo.produtos) || [])
      .filter(p => !p.controlaEstoque || Number(p.estoque) > 0)
      .slice(0, 3);

    const cabecalho = `
      <div class="cc-painel__topo">
        <div><h2>Produtos para seu pet</h2><p>Itens disponíveis na nossa lojinha.</p></div>
        <a class="cc-ver-tudo" href="#produtos" data-rota="produtos">Ver todos os produtos ${ico(ICO.seta, 14)}</a>
      </div>`;

    if (!produtos.length) {
      alvo.innerHTML = `<section class="cc-painel">${cabecalho}
        ${vazio('Nenhum produto disponível', 'Assim que a equipe repuser o estoque, os produtos aparecem aqui.')}
      </section>`;
      return;
    }

    alvo.innerHTML = `<section class="cc-painel">${cabecalho}
      <div class="cc-vitrine-mini">${produtos.map(p => `
        <article class="cc-produto">
          <span class="cc-produto__ico">${ico(ICO.etiqueta, 20)}</span>
          <span class="cc-produto__cat">${esc(p.categoria) || 'LanePets'}</span>
          <h3>${esc(p.nome)}</h3>
          <strong class="cc-produto__preco">${brl(p.valorVenda)}</strong>
          <button class="botao claro" type="button" data-ir="produtos">Pedir</button>
        </article>`).join('')}</div>
    </section>`;
  }

  function renderProximo() {
    const proximo = proximoAgendamento();
    if (!proximo) {
      $('#cc-proximo').innerHTML = `
        <section class="cc-painel">
          ${vazio('Nenhum agendamento por vir', 'Reserve um horário para o seu pet em poucos cliques.',
            '<button class="botao primario" type="button" data-ir="agendar">Agendar um serviço</button>')}
        </section>`;
      return;
    }
    $('#cc-proximo').innerHTML = `
      <div class="cc-destaque">
        <div>
          <p class="cc-destaque__rotulo">Próximo agendamento</p>
          <h3>${esc(proximo.pet)} · ${esc(servicosDo(proximo))}</h3>
          <div class="cc-destaque__linhas">
            <span>${ico(ICO.agenda, 16)} ${dataHora(proximo.dataHora)}</span>
            <span>${ico(ICO.local, 16)} ${esc(proximo.unidade || 'Unidade a confirmar')}</span>
            ${proximo.responsavelNome ? `<span>${ico(ICO.pessoa, 16)} Atendimento com ${esc(proximo.responsavelNome)}</span>` : ''}
            <span>${ico(ICO.cartao, 16)} ${brl(proximo.total)} · ${esc(proximo.formaPagamento || 'A combinar')}</span>
          </div>
        </div>
        <div class="cc-destaque__lado">
          ${selo(proximo.status)}
          <button class="botao claro" type="button" data-ir="agendar">Ver agendamento</button>
        </div>
      </div>`;
  }

  const texto = (seletor, valor) => { const el = $(seletor); if (el) el.textContent = valor; };
  const html = (seletor, valor) => { const el = $(seletor); if (el) el.innerHTML = valor; };

  function renderPerfil() {
    const marca = iniciais(conta.nome);
    const primeiroNome = String(conta.nome || '').split(' ')[0] || 'Cliente';
    texto('#cc-iniciais', marca);
    texto('#cc-nome-topo', primeiroNome);
    texto('#boas-vindas', `Olá, ${primeiroNome}!`);

    const campos = `
      <div class="cc-dado"><dt>Nome completo</dt><dd>${esc(conta.nome) || '—'}</dd></div>
      <div class="cc-dado"><dt>E-mail</dt><dd>${esc(conta.email) || '—'}</dd></div>
      <div class="cc-dado"><dt>Telefone</dt><dd>${esc(conta.telefone) || 'Não informado'}</dd></div>
      <div class="cc-dado"><dt>Endereço</dt><dd>${esc(conta.endereco) || 'Não informado'}</dd></div>
      <div class="cc-dado"><dt>Cliente desde</dt><dd>${conta.criadoEm ? dataCurta(conta.criadoEm) : '—'}</dd></div>
      <div class="cc-dado"><dt>Status da conta</dt><dd>${selo(conta.status || 'ativo')}</dd></div>`;
    html('#cc-dados-2', campos);
    html('#cc-acesso', `
      <div class="cc-dado"><dt>E-mail de acesso</dt><dd>${esc(conta.email) || '—'}</dd></div>
      <div class="cc-dado"><dt>Senha</dt><dd>••••••••</dd></div>`);
  }

  /* =====================================================================
     MEUS PETS

     A lista mostra SOMENTE os pets que a API devolveu para esta conta —
     GET /api/cliente/conta ja filtra por cliente autenticado, entao a tela
     nunca tem em maos o pet de outra pessoa para poder exibir por engano.

     A idade nunca e um campo: ela e calculada a partir da data de nascimento
     na hora de desenhar o card. Idade guardada no banco fica velha sozinha.
     ===================================================================== */

  /* Idade legivel a partir da data de nascimento ISO. Pet sem data continua
     valido — a ficha simplesmente nao mostra a linha de idade. */
  function idadeDe(iso) {
    if (!iso) return '';
    const nasc = new Date(iso + 'T00:00:00');
    if (isNaN(nasc)) return '';
    const hoje = new Date();
    let meses = (hoje.getFullYear() - nasc.getFullYear()) * 12 + (hoje.getMonth() - nasc.getMonth());
    if (hoje.getDate() < nasc.getDate()) meses -= 1;
    if (meses < 0) return '';
    if (meses < 1) return 'Menos de 1 mês';
    if (meses < 12) return meses === 1 ? '1 mês' : `${meses} meses`;
    const anos = Math.floor(meses / 12);
    return anos === 1 ? '1 ano' : `${anos} anos`;
  }

  /* Data de hoje no fuso do navegador. Em UTC (toISOString) o Brasil vira o
     dia seguinte depois das 21h, o que marcaria o dia errado no calendario. */
  const hojeISO = () => {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  };

  const pesoTexto = v => Number(v || 0) > 0
    ? Number(v).toLocaleString('pt-BR', { minimumFractionDigits: 0, maximumFractionDigits: 2 }) + ' kg'
    : '';

  /* O retrato do card: foto quando existe, simbolo da especie quando nao. */
  const retratoPet = (p, tamanho = 34) => p.fotoUrl
    ? `<img class="cc-pet__foto pet-foto-real" src="${esc(p.fotoUrl)}" alt="Foto de ${esc(p.petNome)}"
         loading="lazy" data-tipo="${esc(p.tipo)}" data-tamanho="${tamanho}">`
    : `<span class="cc-pet__ico">${ico(iconePet(p.tipo), tamanho)}</span>`;

  function renderPets() {
    if (!conta.pets.length) {
      $('#pets').innerHTML = `
        <button class="cc-pet cc-pet--novo" type="button" data-pet-novo>
          ${ico('<path d="M12 5v14M5 12h14"/>', 26)}
          Adicionar novo pet
        </button>`;
      return;
    }
    const cards = conta.pets.map(p => {
      const idade = idadeDe(p.dataNascimento);
      const peso = pesoTexto(p.peso);
      return `
      <article class="cc-pet cc-pet--ficha">
        <div class="cc-pet__retrato">${retratoPet(p)}</div>
        <div class="cc-pet__corpo">
          <h3>${esc(p.petNome)}</h3>
          <p class="cc-pet__especie">${esc(p.tipo) || 'Espécie não informada'}${p.sexo ? ' · ' + esc(p.sexo) : ''}</p>
          <dl class="cc-pet__linhas">
            ${idade ? `<div><dt>Idade</dt><dd>${esc(idade)}</dd></div>` : ''}
            <div><dt>Raça</dt><dd>${esc(p.raca) || 'Não informada'}</dd></div>
            ${p.porte ? `<div><dt>Porte</dt><dd>${esc(p.porte)}</dd></div>` : ''}
            ${peso ? `<div><dt>Peso</dt><dd>${esc(peso)}</dd></div>` : ''}
          </dl>
          <div class="cc-pet__acoes">
            <button class="botao claro" type="button" data-pet-ver="${esc(p.id)}">Ver detalhes</button>
            <button class="botao primario" type="button" data-pet-editar="${esc(p.id)}">Editar</button>
          </div>
        </div>
      </article>`;
    }).join('');
    $('#pets').innerHTML = cards + `
      <button class="cc-pet cc-pet--novo" type="button" data-pet-novo>
        ${ico('<path d="M12 5v14M5 12h14"/>', 26)}
        Adicionar novo pet
      </button>`;
  }

  /* ---------------------------------------------------------------------
     Ficha completa (somente leitura)
     --------------------------------------------------------------------- */
  const petPorId = id => conta.pets.find(p => p.id === id) || null;

  const linhaFicha = (rotulo, valor) => valor
    ? `<div class="cc-resumo-linha"><span>${rotulo}</span><strong>${esc(valor)}</strong></div>` : '';

  function corpoFicha(p) {
    const idade = idadeDe(p.dataNascimento);
    const textos = [
      ['Observações', p.observacoes],
      ['Necessidades especiais', p.necessidadesEspeciais],
      ['Informações para o atendimento', p.infoAtendimento]
    ].filter(par => par[1]);
    return `
      ${p.fotoUrl
        ? `<img class="cc-pet__retrato-grande pet-foto-real" src="${esc(p.fotoUrl)}" alt="Foto de ${esc(p.petNome)}"
             data-tipo="${esc(p.tipo)}" data-tamanho="46">`
        : `<div class="cc-pet__retrato-grande cc-pet__retrato-grande--vazio">${ico(iconePet(p.tipo), 46)}</div>`}
      <div class="cc-resumo-bloco">
        ${linhaFicha('Nome', p.petNome)}
        ${linhaFicha('Espécie', p.tipo)}
        ${linhaFicha('Raça', p.raca || 'Não informada')}
        ${linhaFicha('Sexo', p.sexo)}
        ${linhaFicha('Nascimento', p.dataNascimento ? dataCurta(p.dataNascimento + 'T00:00:00') : '')}
        ${linhaFicha('Idade', idade)}
        ${linhaFicha('Porte', p.porte)}
        ${linhaFicha('Peso', pesoTexto(p.peso))}
        ${linhaFicha('Cor', p.cor)}
      </div>
      ${textos.map(par => `
        <div class="cc-pet__texto">
          <h4>${par[0]}</h4>
          <p>${esc(par[1])}</p>
        </div>`).join('')}`;
  }

  /* Historico de atendimentos DESTE pet (pendencia do roadmap, 25/09).
     Mesma regra do #historico (ehRealizado = Concluído) e os mesmos
     agendamentos de GET /api/cliente/conta — nada novo e buscado. So na ficha
     de leitura; a previa do cadastro em etapas continua sem historico. */
  const LIMITE_HIST_FICHA = 5;
  function historicoDoPet(p) {
    const itens = (conta.agendamentos || []).filter(a => a.petId === p.id && ehRealizado(a))
      .slice().sort((a, b) => quandoMs(b) - quandoMs(a));
    if (!itens.length) {
      return `<div class="cc-pet__texto"><h4>Histórico de atendimentos</h4>
        <p>Nenhum atendimento concluído ainda. Quando a equipe concluir um serviço deste pet, ele aparece aqui.</p></div>`;
    }
    const mostrados = itens.slice(0, LIMITE_HIST_FICHA);
    return `<div class="cc-pet__texto">
        <h4>Histórico de atendimentos · ${itens.length} ${itens.length === 1 ? 'concluído' : 'concluídos'}</h4>
        <div class="cc-lista">${mostrados.map(a => linha({
          icone: ICO.agenda,
          titulo: esc(servicosDo(a)),
          meta: `${dataHora(a.dataHora)} · ${esc(rotuloUnidade(a.unidade))}`,
          fim: `<strong>${brl(a.total)}</strong>`
        })).join('')}</div>
        ${itens.length > LIMITE_HIST_FICHA
          ? `<p class="cc-nota">Mostrando os ${LIMITE_HIST_FICHA} mais recentes.</p>` : ''}
        <button class="botao claro" type="button" data-hist-pet="${esc(p.id)}">Ver no Histórico de serviços</button>
      </div>`;
  }

  function abrirDetalhes(id) {
    const pet = petPorId(id);
    if (!pet) { toast('Pet não encontrado. Atualize a página.'); return; }
    $('#pd-titulo').textContent = pet.petNome;
    $('#pd-sub').textContent = 'Ficha cadastrada na sua conta.';
    $('#pd-corpo').innerHTML = corpoFicha(pet) + historicoDoPet(pet);
    $('#pd-editar').dataset.id = pet.id;
    $('#modal-pet-detalhes').showModal();
  }

  /* "Ver no Histórico": abre a tela de historico ja filtrada neste pet. */
  $('#pd-corpo').addEventListener('click', evento => {
    const botao = evento.target.closest('[data-hist-pet]');
    if (!botao) return;
    histPet = botao.dataset.histPet;
    $('#modal-pet-detalhes').close();
    renderHistorico();
    irPara('historico');
  });

  $('#pd-editar').onclick = () => {
    const id = $('#pd-editar').dataset.id;
    $('#modal-pet-detalhes').close();
    abrirFluxoPet('edicao', id);
  };

  /* ---------------------------------------------------------------------
     Cadastro e edicao em etapas

     O fluxo inteiro vive aqui, em memoria. Enquanto o cliente avanca pelas
     etapas NADA vai para a API: o pet so existe no banco depois do clique em
     "Confirmar cadastro" (ou "Confirmar alterações"), na ultima etapa.
     --------------------------------------------------------------------- */
  const ESPECIES_PET = ['Cachorro', 'Gato', 'Outro'];
  const SEXOS_PET = ['Macho', 'Fêmea'];
  const PORTES_PET = ['Pequeno', 'Médio', 'Grande'];
  const LIMITE_TEXTO_PET = 500;

  const ETAPAS_PET = [
    { id: 'basicas', rotulo: 'Informações' },
    { id: 'caracteristicas', rotulo: 'Características' },
    { id: 'adicionais', rotulo: 'Adicionais' },
    { id: 'revisao', rotulo: 'Revisão' }
  ];

  const fichaVazia = () => ({
    nome: '', tipo: 'Cachorro', raca: '', sexo: '', dataNascimento: '', porte: '',
    cor: '', peso: '', fotoUrl: '', removerFoto: false,
    observacoes: '', necessidadesEspeciais: '', infoAtendimento: ''
  });

  /* Converte o pet que veio da API para o formato do formulario. */
  const fichaDoPet = p => ({
    nome: p.petNome || '',
    /* Pets cadastrados no registro da conta podem ter a especie gravada em
       caixa diferente ("gato") ou como "Nao informado". Casar sem diferenciar
       maiuscula evita que abrir a edicao troque a especie do pet sozinha. */
    tipo: (ESPECIES_PET.find(e => e.toLowerCase() === String(p.tipo || '').trim().toLowerCase()) || 'Outro'),
    raca: p.raca || '',
    sexo: p.sexo || '',
    dataNascimento: p.dataNascimento || '',
    porte: p.porte || '',
    cor: p.cor || '',
    peso: Number(p.peso || 0) > 0 ? String(p.peso).replace('.', ',') : '',
    fotoUrl: p.fotoUrl || '',
    removerFoto: false,
    observacoes: p.observacoes || '',
    necessidadesEspeciais: p.necessidadesEspeciais || '',
    infoAtendimento: p.infoAtendimento || ''
  });

  let petFluxo = null;

  function abrirFluxoPet(modo, id) {
    const pet = modo === 'edicao' ? petPorId(id) : null;
    if (modo === 'edicao' && !pet) { toast('Pet não encontrado. Atualize a página.'); return; }
    petFluxo = {
      modo,
      id: pet ? pet.id : '',
      etapa: 0,
      ficha: pet ? fichaDoPet(pet) : fichaVazia(),
      /* Guardado para a etapa de revisao poder apontar o que mudou. */
      original: pet ? fichaDoPet(pet) : null,
      concluido: false,
      salvo: null
    };
    pintarEtapaPet();
    $('#modal-pet').showModal();
  }

  const etapaAtualPet = () => ETAPAS_PET[petFluxo.etapa];

  function pintarEtapaPet() {
    if (!petFluxo) return;
    const edicao = petFluxo.modo === 'edicao';
    const ultima = petFluxo.etapa === ETAPAS_PET.length - 1;

    $('#pw-etapas').innerHTML = petFluxo.concluido ? '' : ETAPAS_PET.map((e, i) => `
      <li class="${i < petFluxo.etapa ? 'is-feita' : i === petFluxo.etapa ? 'is-atual' : ''}">
        <span class="cc-etapas__num">${i + 1}</span><span class="cc-etapas__rotulo">${e.rotulo}</span>
      </li>`).join('');
    $('#pw-etapas').style.display = petFluxo.concluido ? 'none' : '';

    $('#pw-eyebrow').textContent = edicao ? 'Editar pet' : 'Novo pet';
    if (petFluxo.concluido) {
      $('#pw-titulo').textContent = edicao ? 'Alterações salvas! 🐾' : 'Pet cadastrado com sucesso! 🐾';
      $('#pw-sub').textContent = edicao
        ? `Os dados de ${petFluxo.salvo.petNome} foram atualizados.`
        : `O cadastro de ${petFluxo.salvo.petNome} foi concluído.`;
      $('#pw-corpo').innerHTML = corpoFicha(petFluxo.salvo);
    } else {
      $('#pw-titulo').textContent = edicao ? `Editar ${petFluxo.ficha.nome || 'pet'}` : 'Cadastrar pet';
      $('#pw-sub').textContent = ultima
        ? 'Tudo certo? Confira as informações antes de finalizar.'
        : 'Nada é salvo antes de você conferir e confirmar no final.';
      $('#pw-corpo').innerHTML = CORPOS_PET[etapaAtualPet().id]();
      ligarCamposPet(etapaAtualPet().id);
    }

    $('#pw-voltar').style.display = (petFluxo.etapa === 0 || petFluxo.concluido) ? 'none' : '';
    $('#pw-voltar').textContent = ultima ? '← Voltar e corrigir' : '← Voltar';
    $('#pw-avancar').textContent = petFluxo.concluido
      ? 'Voltar para Meus pets'
      : ultima ? (edicao ? '✓ Confirmar alterações' : '✓ Confirmar cadastro') : 'Continuar';
    document.querySelectorAll('#modal-pet [data-pet-sair]').forEach(b => {
      b.style.display = petFluxo.concluido ? 'none' : '';
    });
    aviso('#pw-msg', '');
    const corpo = $('#modal-pet .cc-modal__corpo');
    if (corpo) corpo.scrollTop = 0;
  }

  /* Campos ---------------------------------------------------------------
     Cada etapa e redesenhada do zero; por isso os valores sao lidos de
     petFluxo.ficha ao pintar e gravados de volta ao sair da etapa. O
     preenchimento nunca se perde ao ir e voltar. */
  const selectPet = (id, rotulo, valores, atual, vazioTexto) => `
    <div class="cc-campo">
      <label for="${id}">${rotulo}</label>
      <select id="${id}">
        ${vazioTexto ? `<option value="" ${!atual ? 'selected' : ''}>${vazioTexto}</option>` : ''}
        ${valores.map(v => `<option value="${esc(v)}" ${atual === v ? 'selected' : ''}>${esc(v)}</option>`).join('')}
      </select>
    </div>`;

  const CORPOS_PET = {
    basicas: () => {
      const f = petFluxo.ficha;
      return `
        <div class="cc-etapa__titulo">Sobre o seu pet</div>
        <div class="cc-etapa__form">
          <div class="cc-campo cc-campo--largo">
            <label for="pw-nome">Nome do pet *</label>
            <input id="pw-nome" type="text" maxlength="40" placeholder="Ex.: Thor" value="${esc(f.nome)}">
          </div>
          ${selectPet('pw-tipo', 'Espécie *', ESPECIES_PET, f.tipo, '')}
          <div class="cc-campo">
            <label for="pw-raca">Raça</label>
            <input id="pw-raca" type="text" maxlength="40" placeholder="Ex.: SRD, Golden Retriever…" value="${esc(f.raca)}">
          </div>
          ${selectPet('pw-sexo', 'Sexo', SEXOS_PET, f.sexo, 'Não informado')}
          ${selectPet('pw-porte', 'Porte', PORTES_PET, f.porte, 'Não informado')}
          <div class="cc-campo cc-campo--largo">
            <label for="pw-nascimento">Data de nascimento</label>
            <input id="pw-nascimento" type="date" max="${hojeISO()}" value="${esc(f.dataNascimento)}">
            <span class="cc-ajuda">Se não souber a data exata, pode deixar em branco. A idade é calculada por aqui.</span>
          </div>
        </div>`;
    },

    caracteristicas: () => {
      const f = petFluxo.ficha;
      return `
        <div class="cc-etapa__titulo">Características do pet</div>
        <div class="cc-etapa__form">
          <div class="cc-campo">
            <label for="pw-cor">Cor / pelagem</label>
            <input id="pw-cor" type="text" maxlength="30" placeholder="Ex.: Dourado" value="${esc(f.cor)}">
          </div>
          <div class="cc-campo">
            <label for="pw-peso">Peso (kg)</label>
            <input id="pw-peso" type="text" inputmode="decimal" placeholder="Ex.: 28 ou 4,5" value="${esc(f.peso)}">
          </div>
          <div class="cc-campo cc-campo--largo">
            <label>Foto do pet</label>
            <div class="cc-foto">
              <div class="cc-foto__previa" id="pw-foto-previa">
                ${f.fotoUrl
                  ? `<img class="pet-foto-real" src="${esc(f.fotoUrl)}" alt="Prévia da foto" data-tipo="${esc(f.tipo)}" data-tamanho="34">`
                  : ico(iconePet(f.tipo), 34)}
              </div>
              <div class="cc-foto__acoes">
                <input id="pw-foto" type="file" accept="image/jpeg,image/png,image/webp" hidden>
                <button class="botao claro" type="button" id="pw-foto-escolher">${f.fotoUrl ? 'Trocar foto' : 'Escolher foto'}</button>
                <button class="botao claro" type="button" id="pw-foto-remover" ${f.fotoUrl ? '' : 'hidden'}>Remover</button>
                <span class="cc-ajuda">JPG, PNG ou WebP. A imagem é reduzida aqui no navegador antes de ser enviada.</span>
              </div>
            </div>
          </div>
          <div class="cc-campo cc-campo--largo">
            <label for="pw-observacoes">Observações</label>
            <textarea id="pw-observacoes" rows="3" maxlength="500" placeholder="Ex.: Muito dócil e brincalhão.">${esc(f.observacoes)}</textarea>
          </div>
        </div>`;
    },

    adicionais: () => {
      const f = petFluxo.ficha;
      return `
        <div class="cc-etapa__titulo">Informações para o atendimento</div>
        <p class="cc-nota">Opcional. Serve para a equipe LanePets saber como cuidar melhor do seu pet no dia. Não é ficha veterinária.</p>
        <div class="cc-etapa__form">
          <div class="cc-campo cc-campo--largo">
            <label for="pw-necessidades">Necessidades especiais</label>
            <textarea id="pw-necessidades" rows="3" maxlength="500" placeholder="Ex.: enxerga pouco do olho esquerdo, precisa de apoio na escada.">${esc(f.necessidadesEspeciais)}</textarea>
          </div>
          <div class="cc-campo cc-campo--largo">
            <label for="pw-atendimento">Preferências no atendimento</label>
            <textarea id="pw-atendimento" rows="3" maxlength="500" placeholder="Ex.: fica agitado com secador alto, prefere tosa na tesoura.">${esc(f.infoAtendimento)}</textarea>
          </div>
        </div>`;
    },

    revisao: () => {
      const f = petFluxo.ficha;
      const previa = {
        petNome: f.nome, tipo: f.tipo, raca: f.raca, sexo: f.sexo,
        dataNascimento: f.dataNascimento, porte: f.porte,
        peso: Number(String(f.peso).replace(',', '.')) || 0,
        cor: f.cor, fotoUrl: f.fotoUrl, observacoes: f.observacoes,
        necessidadesEspeciais: f.necessidadesEspeciais, infoAtendimento: f.infoAtendimento
      };
      return `
        <div class="cc-etapa__titulo">${petFluxo.modo === 'edicao' ? 'Confirmar alterações' : 'Confira os dados do seu pet'}</div>
        ${corpoFicha(previa)}
        ${petFluxo.modo === 'edicao' ? listaDeAlteracoes() : ''}
        <p class="cc-nota">${petFluxo.modo === 'edicao'
          ? 'As alterações só são gravadas depois que você confirmar.'
          : 'O cadastro só é enviado depois que você confirmar. Nada foi salvo até aqui.'}</p>`;
    }
  };

  /* Na edicao, dizer o que mudou vale mais do que repetir a ficha inteira. */
  const ROTULOS_PET = {
    nome: 'Nome', tipo: 'Espécie', raca: 'Raça', sexo: 'Sexo', dataNascimento: 'Nascimento',
    porte: 'Porte', cor: 'Cor', peso: 'Peso', fotoUrl: 'Foto',
    observacoes: 'Observações', necessidadesEspeciais: 'Necessidades especiais',
    infoAtendimento: 'Informações para o atendimento'
  };

  function listaDeAlteracoes() {
    const antes = petFluxo.original || {};
    const mudou = Object.keys(ROTULOS_PET).filter(k => String(antes[k] == null ? '' : antes[k]) !== String(petFluxo.ficha[k] == null ? '' : petFluxo.ficha[k]));
    if (!mudou.length) return '<p class="cc-nota">Nenhuma informação foi alterada.</p>';
    return `
      <div class="cc-pet__texto">
        <h4>O que muda ao confirmar</h4>
        <ul class="cc-pet__diff">
          ${mudou.map(k => k === 'fotoUrl'
            ? `<li><strong>Foto</strong>: ${petFluxo.ficha.fotoUrl ? 'nova foto' : 'foto removida'}</li>`
            : `<li><strong>${ROTULOS_PET[k]}</strong>: ${esc(antes[k] || '—')} → ${esc(petFluxo.ficha[k] || '—')}</li>`).join('')}
        </ul>
      </div>`;
  }

  /* Liga o que so existe depois que a etapa foi pintada. */
  function ligarCamposPet(id) {
    if (id !== 'caracteristicas') return;
    const escolher = $('#pw-foto-escolher');
    const entrada = $('#pw-foto');
    const remover = $('#pw-foto-remover');
    if (!escolher || !entrada) return;
    escolher.onclick = () => entrada.click();
    remover.onclick = () => {
      petFluxo.ficha.fotoUrl = '';
      petFluxo.ficha.removerFoto = true;
      entrada.value = '';
      $('#pw-foto-previa').innerHTML = ico(iconePet(petFluxo.ficha.tipo), 34);
      remover.hidden = true;
      escolher.textContent = 'Escolher foto';
      aviso('#pw-msg', '');
    };
    entrada.onchange = async () => {
      const arquivo = entrada.files && entrada.files[0];
      if (!arquivo) return;
      if (!/^image\/(jpeg|png|webp)$/.test(arquivo.type)) {
        entrada.value = '';
        return aviso('#pw-msg', 'Envie uma imagem JPG, PNG ou WebP.', true);
      }
      if (arquivo.size > 8 * 1024 * 1024) {
        entrada.value = '';
        return aviso('#pw-msg', 'A imagem escolhida é muito grande. Use uma foto de até 8 MB.', true);
      }
      try {
        const dataUri = await reduzirImagem(arquivo);
        petFluxo.ficha.fotoUrl = dataUri;
        petFluxo.ficha.removerFoto = false;
        $('#pw-foto-previa').innerHTML = `<img src="${esc(dataUri)}" alt="Prévia da foto">`;
        remover.hidden = false;
        escolher.textContent = 'Trocar foto';
        aviso('#pw-msg', '');
      } catch (e) {
        aviso('#pw-msg', 'Não foi possível ler essa imagem. Tente outra foto.', true);
      } finally { entrada.value = ''; }
    };
  }

  /* A foto e reduzida AQUI, antes de sair do navegador: 320px no maior lado,
     JPEG. Uma foto de celular de 4 MB vira ~25 KB, que e o que cabe bem no
     cadastro e no carregamento da lista. */
  function reduzirImagem(arquivo) {
    return new Promise((resolve, reject) => {
      const leitor = new FileReader();
      leitor.onerror = () => reject(new Error('leitura'));
      leitor.onload = () => {
        const img = new Image();
        img.onerror = () => reject(new Error('imagem'));
        img.onload = () => {
          const lado = 320;
          const escala = Math.min(1, lado / Math.max(img.width, img.height));
          const largura = Math.max(1, Math.round(img.width * escala));
          const altura = Math.max(1, Math.round(img.height * escala));
          const tela = document.createElement('canvas');
          tela.width = largura; tela.height = altura;
          const ctx = tela.getContext('2d');
          ctx.fillStyle = '#ffffff';
          ctx.fillRect(0, 0, largura, altura);
          ctx.drawImage(img, 0, 0, largura, altura);
          resolve(tela.toDataURL('image/jpeg', 0.72));
        };
        img.src = leitor.result;
      };
      leitor.readAsDataURL(arquivo);
    });
  }

  /* Grava na ficha o que esta na tela da etapa atual. */
  function coletarEtapaPet(id) {
    const f = petFluxo.ficha;
    const val = sel => { const el = $(sel); return el ? el.value : undefined; };
    if (id === 'basicas') {
      f.nome = (val('#pw-nome') || '').trim();
      f.tipo = val('#pw-tipo') || 'Cachorro';
      f.raca = (val('#pw-raca') || '').trim();
      f.sexo = val('#pw-sexo') || '';
      f.porte = val('#pw-porte') || '';
      f.dataNascimento = val('#pw-nascimento') || '';
    }
    if (id === 'caracteristicas') {
      f.cor = (val('#pw-cor') || '').trim();
      f.peso = (val('#pw-peso') || '').trim();
      f.observacoes = (val('#pw-observacoes') || '').trim();
    }
    if (id === 'adicionais') {
      f.necessidadesEspeciais = (val('#pw-necessidades') || '').trim();
      f.infoAtendimento = (val('#pw-atendimento') || '').trim();
    }
  }

  /* Validacao da etapa. Devolve a mensagem de erro, ou string vazia.

     Isto e a validacao da EXPERIENCIA: o mesmo conjunto de regras existe no
     servidor (Services/PetFicha.cs) e e la que a decisao vale. O navegador
     pode ser contornado; a API nao. */
  function validarEtapaPet(id) {
    const f = petFluxo.ficha;
    if (id === 'basicas') {
      if (f.nome.length < 2) return 'Informe o nome do pet (mínimo 2 letras).';
      if (f.nome.length > 40) return 'O nome do pet pode ter até 40 caracteres.';
      if (ESPECIES_PET.indexOf(f.tipo) < 0) return 'Escolha a espécie do pet.';
      if (f.raca.length > 40) return 'A raça pode ter até 40 caracteres.';
      if (f.sexo && SEXOS_PET.indexOf(f.sexo) < 0) return 'Sexo inválido. Escolha Macho ou Fêmea.';
      if (f.porte && PORTES_PET.indexOf(f.porte) < 0) return 'Porte inválido. Escolha Pequeno, Médio ou Grande.';
      if (f.dataNascimento) {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(f.dataNascimento)) return 'Data de nascimento inválida. Use o seletor de data.';
        const d = new Date(f.dataNascimento + 'T00:00:00');
        if (isNaN(d)) return 'Data de nascimento inválida. Use o seletor de data.';
        if (f.dataNascimento > hojeISO()) return 'A data de nascimento não pode estar no futuro.';
        const limite = new Date(); limite.setFullYear(limite.getFullYear() - 40);
        if (d < limite) return 'Confira a data de nascimento: ela está muito antiga.';
      }
      return '';
    }
    if (id === 'caracteristicas') {
      if (f.cor.length > 30) return 'A cor pode ter até 30 caracteres.';
      if (f.peso) {
        if (!/^\d{1,3}([.,]\d{1,2})?$/.test(f.peso)) return 'Peso inválido. Informe apenas números, por exemplo 12,5.';
        const peso = Number(f.peso.replace(',', '.'));
        if (!(peso > 0)) return 'Informe um peso maior que zero ou deixe o campo em branco.';
        if (peso > 150) return 'Confira o peso: o valor informado está acima do esperado.';
      }
      if (f.observacoes.length > LIMITE_TEXTO_PET) return 'As observações podem ter até 500 caracteres.';
      return '';
    }
    if (id === 'adicionais') {
      if (f.necessidadesEspeciais.length > LIMITE_TEXTO_PET) return 'As necessidades especiais podem ter até 500 caracteres.';
      if (f.infoAtendimento.length > LIMITE_TEXTO_PET) return 'As informações para o atendimento podem ter até 500 caracteres.';
      return '';
    }
    return '';
  }

  /* Botao principal: avanca a etapa ou, na ultima, confirma de verdade.

     Aqui o botao NAO usa comBotao: aquele auxiliar devolve ao botao o texto que
     ele tinha antes da acao, e neste fluxo o texto muda de proposito a cada
     etapa ("Continuar" -> "Confirmar cadastro" -> "Voltar para Meus pets").
     Quem manda no rotulo e pintarEtapaPet. */
  $('#pw-avancar').onclick = async event => {
    if (!petFluxo) return;
    if (petFluxo.concluido) { fecharFluxoPet(); irPara('meus-pets'); return; }

    const etapa = etapaAtualPet().id;
    coletarEtapaPet(etapa);
    const erro = validarEtapaPet(etapa);
    if (erro) { aviso('#pw-msg', erro, true); return; }

    if (etapa !== 'revisao') {
      petFluxo.etapa += 1;
      pintarEtapaPet();
      return;
    }

    /* Ultima etapa: e a gravacao de verdade. O botao trava enquanto a
       requisicao corre, para dois cliques nao virarem dois pets. */
    const botao = event.currentTarget;
    const rotulo = botao.textContent;
    botao.disabled = true;
    botao.textContent = 'Salvando…';
    try { await confirmarPet(); }
    finally {
      botao.disabled = false;
      /* Se deu certo, pintarEtapaPet ja trocou o rotulo; se falhou, ele volta
         a ser o de confirmar. */
      if (petFluxo && !petFluxo.concluido) botao.textContent = rotulo;
    }
  };

  $('#pw-voltar').onclick = () => {
    if (!petFluxo || petFluxo.etapa === 0) return;
    coletarEtapaPet(etapaAtualPet().id);   /* volta sem perder o que foi digitado */
    petFluxo.etapa -= 1;
    pintarEtapaPet();
  };

  /* A UNICA chamada a API do fluxo. Antes daqui, nada saiu do navegador. */
  async function confirmarPet() {
    const f = petFluxo.ficha;
    const corpo = JSON.stringify({
      nome: f.nome, tipo: f.tipo, raca: f.raca, sexo: f.sexo,
      dataNascimento: f.dataNascimento, peso: f.peso, cor: f.cor, porte: f.porte,
      fotoUrl: f.fotoUrl, removerFoto: !!f.removerFoto, observacoes: f.observacoes,
      necessidadesEspeciais: f.necessidadesEspeciais, infoAtendimento: f.infoAtendimento
    });
    try {
      const salvo = petFluxo.modo === 'edicao'
        ? await api('pets/' + encodeURIComponent(petFluxo.id), { method: 'PUT', body: corpo })
        : await api('pets', { method: 'POST', body: corpo });

      /* A conta e relida para que a lista, o seletor de agendamento, o de
         avaliacao e o Seguro Pet passem a usar o pet atualizado. Nao existe
         uma segunda copia do pet em lugar nenhum. */
      await abrir();
      petFluxo.salvo = salvo;
      petFluxo.concluido = true;
      pintarEtapaPet();
      toast(petFluxo.modo === 'edicao' ? '✓ Pet atualizado.' : '✓ Pet cadastrado.');
    } catch (e) {
      aviso('#pw-msg', e.status === 401
        ? 'Sua sessão expirou. Entre novamente para salvar.'
        : (e.message || 'Não foi possível salvar. Tente novamente.'), true);
    }
  }

  /* Sair do fluxo ---------------------------------------------------------
     Um clique errado no X nao pode apagar um formulario inteiro. */
  function fecharFluxoPet() {
    petFluxo = null;
    const caixa = $('#modal-pet');
    if (caixa.open) caixa.close();
  }

  const fluxoTemConteudo = () => {
    if (!petFluxo || petFluxo.concluido) return false;
    coletarEtapaPet(etapaAtualPet().id);
    const vazio = petFluxo.modo === 'edicao' ? petFluxo.original : fichaVazia();
    return Object.keys(ROTULOS_PET).some(k => String(vazio[k] == null ? '' : vazio[k]) !== String(petFluxo.ficha[k] == null ? '' : petFluxo.ficha[k]));
  };

  function pedirParaSairDoFluxo() {
    if (!petFluxo) { $('#modal-pet').close(); return; }
    if (petFluxo.concluido || !fluxoTemConteudo()) { fecharFluxoPet(); return; }
    const edicao = petFluxo.modo === 'edicao';
    $('#px-titulo').textContent = edicao ? 'Descartar alterações?' : 'Cancelar cadastro?';
    $('#px-texto').textContent = edicao
      ? 'As alterações que você fez não serão salvas.'
      : 'As informações preenchidas serão descartadas.';
    $('#modal-pet-descartar').querySelector('.cc-form__acoes [data-fechar]').textContent = edicao ? 'Continuar editando' : 'Continuar cadastro';
    $('#px-confirmar').textContent = edicao ? 'Descartar alterações' : 'Cancelar cadastro';
    $('#modal-pet-descartar').showModal();
  }

  $('#px-confirmar').onclick = () => { $('#modal-pet-descartar').close(); fecharFluxoPet(); };

  /* Esc e clique no fundo passam pela mesma confirmacao que o botao Cancelar. */
  $('#modal-pet').addEventListener('cancel', event => { event.preventDefault(); pedirParaSairDoFluxo(); });
  $('#modal-pet').addEventListener('click', event => {
    if (event.target === $('#modal-pet')) { event.stopPropagation(); pedirParaSairDoFluxo(); }
  }, true);

  /* Acoes dos cards e do modal ------------------------------------------- */
  document.addEventListener('click', event => {
    if (event.target.closest('[data-pet-sair]')) { pedirParaSairDoFluxo(); return; }
    if (event.target.closest('[data-pet-novo]')) { abrirFluxoPet('novo'); return; }
    const ver = event.target.closest('[data-pet-ver]');
    if (ver) { abrirDetalhes(ver.dataset.petVer); return; }
    const editar = event.target.closest('[data-pet-editar]');
    if (editar) { abrirFluxoPet('edicao', editar.dataset.petEditar); return; }
  });

  /* =======================================================================
     AGENDAMENTOS — ÁREA DO CLIENTE

     Tudo o que aparece aqui vem do servidor:
       lista      -> GET  /api/cliente/conta        (a.agendamentos)
       catálogo   -> GET  /api/cliente/catalogo     (serviços, unidades)
       horários   -> GET  /api/cliente/horarios     (só os livres da unidade/dia)
       criar      -> POST /api/cliente/agendamentos
       cancelar   -> POST /api/cliente/agendamentos/{id}/cancelar

     Nenhuma rota nova, nenhuma coluna nova, nenhum dado inventado. O backend
     continua resolvendo o cliente pelo token da sessão (nunca por um id vindo
     do navegador), revalidando o dono do pet, o serviço e o conflito de
     horário no POST — a tela não é a dona dessas regras, só as apresenta.
     ======================================================================= */

  /* Item 4 (24/09): status do agendamento = Solicitado, Confirmado, Em
     andamento, Concluído e Cancelado. Nomes antigos (se algum cache trouxer)
     sao traduzidos. O cliente so cancela antes do atendimento comecar — a
     mesma regra que o servidor aplica. Cancelar nao apaga nada. */
  const STATUS_AG = {
    'pendente': 'Solicitado', 'solicitado': 'Solicitado', 'confirmado': 'Confirmado',
    'em processo': 'Em andamento', 'em andamento': 'Em andamento',
    'pronto': 'Concluído', 'entregue': 'Concluído', 'concluido': 'Concluído', 'concluído': 'Concluído',
    'cancelado': 'Cancelado'
  };
  const statusAg = a => STATUS_AG[String((a && a.status) || '').trim().toLowerCase()] || String((a && a.status) || 'Solicitado');
  const cancelavel = a => ['Solicitado', 'Confirmado'].includes(statusAg(a));
  /* "Atendimento com Ana": o servidor manda so o primeiro nome. */
  const responsavelDe = a => String((a && a.responsavelNome) || '').trim();

  /* Ícones próprios desta área (os demais vêm de ICO). */
  const ICO_AG = {
    calendario: ICO.agenda,
    relogio: ICO.relogio,
    local: ICO.local,
    carro: '<path d="M4.5 16.5h15"/><path d="M6 16.5V19a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-2.5"/><path d="M21 16.5V19a1 1 0 0 1-1 1h-1a1 1 0 0 1-1-1v-2.5"/><path d="M3.5 16.5v-4l2-5.2A2 2 0 0 1 7.4 6h9.2a2 2 0 0 1 1.9 1.3l2 5.2v4Z"/><path d="M6.5 13h2M15.5 13h2"/>',
    cartao: ICO.cartao,
    check: '<circle cx="12" cy="12" r="9"/><path d="m8.5 12 2.4 2.4 4.6-4.8"/>',
    alerta: '<path d="M12 3.6 2.6 20h18.8Z"/><path d="M12 10v4"/><path d="M12 17h.01"/>',
    servico: '<path d="M4.5 19.5 9 15"/><path d="m7.5 12.5 4 4"/><path d="M13.6 4.4a4.6 4.6 0 0 0 6 6L14.4 15.6a4.6 4.6 0 0 0-6-6Z"/>'
  };

  /* --------------------------------------------------------------------
     Leitura de um agendamento: data, status, tom do selo.
     -------------------------------------------------------------------- */
  const soData = valor => String(valor || '').slice(0, 10);
  const horaDe = valor => {
    const parte = String(valor || '').split('T')[1] || '';
    return parte ? parte.slice(0, 5) : '—';
  };
  const quandoMs = a => {
    const d = new Date(a.dataHora);
    return isNaN(d) ? 0 : d.getTime();
  };

  /* Cor de cada status (item 4). A tela só traduz em cor — não inventa estado. */
  const TOM_STATUS = {
    'Solicitado': 'aguarda', 'Confirmado': 'ok', 'Em andamento': 'info', 'Concluído': 'neutro', 'Cancelado': 'erro'
  };
  const tomDe = a => TOM_STATUS[statusAg(a)] || 'neutro';

  const ehCancelado = a => statusAg(a) === 'Cancelado';
  const ehConcluido = a => statusAg(a) === 'Concluído';

  /* --------------------------------------------------------------------
     Filtros. Só classificam o que já veio do servidor; nada é recarregado.
     -------------------------------------------------------------------- */
  const GRUPOS = {
    todos:      { rotulo: 'Todos',      teste: () => true },
    proximos:   { rotulo: 'Próximos',   teste: a => !ehCancelado(a) && !ehConcluido(a) && quandoMs(a) >= Date.now() },
    hoje:       { rotulo: 'Hoje',       teste: a => !ehCancelado(a) && soData(a.dataHora) === hojeISO() },
    concluidos: { rotulo: 'Concluídos', teste: ehConcluido },
    cancelados: { rotulo: 'Cancelados', teste: ehCancelado }
  };

  let agFiltro = 'todos';
  let agFiltroPet = '';
  let agFiltroUnidade = '';
  let agEstado = 'pronto';     /* 'carregando' | 'pronto' | 'erro' */

  const contarGrupo = chave => (conta && conta.agendamentos ? conta.agendamentos.filter(GRUPOS[chave].teste).length : 0);

  function agendamentosFiltrados() {
    if (!conta || !conta.agendamentos) return [];
    return conta.agendamentos
      .filter(GRUPOS[agFiltro].teste)
      .filter(a => !agFiltroPet || a.petId === agFiltroPet)
      .filter(a => !agFiltroUnidade || String(a.unidade || '') === agFiltroUnidade)
      .slice()
      .sort((a, b) => quandoMs(b) - quandoMs(a));
  }

  /* --------------------------------------------------------------------
     Indicadores. Números contados sobre os agendamentos reais da conta;
     sem dados, mostram 0.
     -------------------------------------------------------------------- */
  function renderResumoAgenda() {
    const alvo = $('#ag-resumo');
    if (!alvo) return;
    const proximos = contarGrupo('proximos');
    const hoje = contarGrupo('hoje');
    const concluidos = contarGrupo('concluidos');
    const cancelados = contarGrupo('cancelados');
    alvo.innerHTML = [
      metrica({ icone: ICO_AG.calendario, rotulo: 'Próximos', valor: proximos,
        nota: proximos === 1 ? '1 agendamento por vir' : `${proximos} agendamentos por vir` }),
      metrica({ icone: ICO_AG.relogio, rotulo: 'Hoje', valor: hoje, acento: true,
        nota: hoje === 1 ? '1 atendimento hoje' : `${hoje} atendimentos hoje` }),
      metrica({ icone: ICO_AG.check, rotulo: 'Concluídos', valor: concluidos, ok: true,
        nota: concluidos === 1 ? '1 atendimento concluído' : `${concluidos} atendimentos concluídos` }),
      metrica({ icone: ICO_AG.alerta, rotulo: 'Cancelados', valor: cancelados,
        nota: cancelados === 1 ? '1 agendamento cancelado' : `${cancelados} agendamentos cancelados` })
    ].join('');
    const cartoes = alvo.querySelectorAll('.cc-metrica');
    if (cartoes[3]) cartoes[3].classList.add('cc-metrica--erro');
  }

  /* --------------------------------------------------------------------
     Filtros na tela
     -------------------------------------------------------------------- */
  function renderFiltros() {
    const pilulas = $('#ag-pilulas');
    const seletor = $('#ag-filtro-select');
    if (!pilulas || !seletor) return;

    pilulas.innerHTML = Object.entries(GRUPOS).map(([chave, g]) => `
      <button type="button" role="tab" class="ag-pilula${chave === agFiltro ? ' is-ativa' : ''}"
              aria-selected="${chave === agFiltro}" data-ag-filtro="${chave}">
        ${g.rotulo}<span class="ag-pilula__num">${contarGrupo(chave)}</span>
      </button>`).join('');

    seletor.innerHTML = Object.entries(GRUPOS)
      .map(([chave, g]) => `<option value="${chave}" ${chave === agFiltro ? 'selected' : ''}>${g.rotulo} (${contarGrupo(chave)})</option>`)
      .join('');

    /* Pet e unidade só aparecem com o que a conta realmente tem. */
    const petSel = $('#ag-filtro-pet');
    if (petSel) {
      const usados = new Set((conta.agendamentos || []).map(a => a.petId).filter(Boolean));
      const pets = (conta.pets || []).filter(p => usados.has(p.id));
      petSel.innerHTML = '<option value="">Todos os pets</option>'
        + pets.map(p => `<option value="${esc(p.id)}" ${agFiltroPet === p.id ? 'selected' : ''}>${esc(p.petNome)}</option>`).join('');
      petSel.parentElement.hidden = pets.length < 2;
    }
    const uniSel = $('#ag-filtro-unidade');
    if (uniSel) {
      const unidades = [...new Set((conta.agendamentos || []).map(a => a.unidade).filter(Boolean))].sort();
      uniSel.innerHTML = '<option value="">Todas as unidades</option>'
        + unidades.map(u => `<option value="${esc(u)}" ${agFiltroUnidade === u ? 'selected' : ''}>${esc(u)}</option>`).join('');
      uniSel.parentElement.hidden = unidades.length < 2;
    }
  }

  /* --------------------------------------------------------------------
     Card de um agendamento
     -------------------------------------------------------------------- */
  const petDoAgendamento = a => (conta.pets || []).find(p => p.id === a.petId) || null;

  function retratoAgendamento(a, pet) {
    const foto = pet && pet.fotoUrl;
    if (foto) return `<span class="ag-card__retrato"><img src="${esc(foto)}" alt="" loading="lazy"></span>`;
    return `<span class="ag-card__retrato">${ico(iconePet(pet && pet.tipo), 24)}</span>`;
  }

  const unidadePorNome = nome => ((catalogo && catalogo.unidades) || []).find(u => u.nome === nome) || null;

  function cardAgendamento(a) {
    const pet = petDoAgendamento(a);
    const hoje = soData(a.dataHora) === hojeISO() && !ehCancelado(a);
    const unidade = unidadePorNome(a.unidade);
    return `
      <article class="ag-card${ehCancelado(a) ? ' ag-card--cancelado' : ''}" data-tom="${tomDe(a)}">
        ${retratoAgendamento(a, pet)}
        <div>
          <div class="ag-card__topo">
            <h3 class="ag-card__pet">${esc(a.pet || (pet ? pet.petNome : 'Pet'))}</h3>
            ${hoje ? '<span class="ag-hoje">Hoje</span>' : ''}
            ${selo(statusAg(a))}
          </div>
          <div class="ag-card__servico">${esc(servicosDo(a))}${responsavelDe(a) ? ` <span class="ag-card__resp">· Atendimento com ${esc(responsavelDe(a))}</span>` : ''}</div>
          <div class="ag-card__meta">
            <span>${ico(ICO_AG.calendario, 15)} ${dataCurta(a.dataHora)}</span>
            <span>${ico(ICO_AG.relogio, 15)} ${esc(horaDe(a.dataHora))}</span>
            <span>${ico(ICO_AG.local, 15)} ${esc(a.unidade || 'Unidade a confirmar')}${unidade && unidade.endereco ? ' — ' + esc(unidade.endereco) : ''}</span>
            <span>${ico(ICO_AG.carro, 15)} ${esc(a.transporte || 'Cliente leva')}</span>
            <span>${ico(ICO_AG.cartao, 15)} ${esc(a.formaPagamento || 'A combinar')}</span>
          </div>
          ${a.obs ? `<p class="ag-card__obs">${esc(a.obs)}</p>` : ''}
        </div>
        <div class="ag-card__fim">
          <span class="ag-card__valor">${brl(a.total)}</span>
          <div class="ag-card__acoes">
            <button class="botao claro" type="button" data-ag-ver="${esc(a.id)}">Ver detalhes</button>
            ${cancelavel(a) ? `<button class="botao claro" type="button" data-cancelar="${esc(a.id)}">Cancelar</button>` : ''}
          </div>
        </div>
      </article>`;
  }

  const SKELETON_AGENDA = `
    <div class="ag-skeleton" aria-hidden="true">
      ${[0, 1, 2].map(() => `
        <div class="ag-skel-card">
          <span class="ag-skel ag-skel--retrato"></span>
          <div>
            <div class="ag-skel ag-skel--titulo"></div>
            <div class="ag-skel ag-skel--linha"></div>
            <div class="ag-skel ag-skel--linha ag-skel--curta"></div>
          </div>
          <div><div class="ag-skel ag-skel--linha"></div></div>
        </div>`).join('')}
    </div>`;

  function renderAgendamentos() {
    const alvo = $('#agendamentos');
    if (!alvo) return;

    if (agEstado === 'carregando') { alvo.innerHTML = SKELETON_AGENDA; return; }
    if (agEstado === 'erro') {
      alvo.innerHTML = `
        <div class="ag-erro">
          ${ico(ICO_AG.alerta, 40)}
          <h3>Não foi possível carregar seus agendamentos</h3>
          <p>Verifique sua conexão e tente novamente. Nada do que você já agendou foi perdido.</p>
          <button class="botao primario" type="button" data-ag-recarregar>Tentar novamente</button>
        </div>`;
      return;
    }

    renderResumoAgenda();
    renderFiltros();

    const lista = agendamentosFiltrados();

    if (!conta.agendamentos.length) {
      alvo.innerHTML = vazio(
        'Você ainda não possui agendamentos',
        'Agende um serviço para seu pet e acompanhe tudo por aqui.',
        '<button class="botao primario" type="button" data-ag-novo>+ Novo Agendamento</button>');
      return;
    }
    if (!lista.length) {
      alvo.innerHTML = vazio('Nenhum agendamento encontrado', 'Tente alterar os filtros.',
        '<button class="botao claro" type="button" data-ag-limpar>Limpar filtros</button>');
      return;
    }
    alvo.innerHTML = `<div class="ag-lista">${lista.map(cardAgendamento).join('')}</div>`;
  }

  /* --------------------------------------------------------------------
     Detalhes
     -------------------------------------------------------------------- */
  const agPorId = id => (conta.agendamentos || []).find(a => a.id === id) || null;

  const linhaResumo = (rotulo, valor, total) =>
    `<div class="cc-resumo-linha${total ? ' cc-resumo-linha--total' : ''}"><span>${rotulo}</span><strong>${valor}</strong></div>`;

  function abrirDetalhesAgendamento(id) {
    const a = agPorId(id);
    if (!a) { toast('Agendamento não encontrado. Atualize a página.'); return; }
    const pet = petDoAgendamento(a);
    const unidade = unidadePorNome(a.unidade);
    const valorServico = Number(a.total || 0) - Number(a.valorTransporte || 0);

    $('#ad-titulo').textContent = `${a.pet || (pet ? pet.petNome : 'Pet')} · ${servicosDo(a)}`;
    $('#ad-sub').textContent = `${dataCurta(a.dataHora)} às ${horaDe(a.dataHora)} · ${a.unidade || 'Unidade a confirmar'}`;

    $('#ad-corpo').innerHTML = `
      <div class="ag-detalhe">
        <div class="ag-bloco ag-bloco__pet">
          ${retratoAgendamento(a, pet)}
          <div>
            <strong>${esc(a.pet || (pet ? pet.petNome : 'Pet'))}</strong>
            <span>${pet ? esc([pet.tipo, pet.raca, pet.porte].filter(Boolean).join(' · ')) : 'Ficha do pet indisponível'}</span>
          </div>
        </div>
        <div class="ag-detalhe__grade">
          <section class="ag-bloco">
            <h4>Atendimento</h4>
            <dl>
              ${linhaResumo('Serviço', esc(servicosDo(a)))}
              ${linhaResumo('Valor do serviço', brl(valorServico))}
              ${Number(a.valorTransporte || 0) > 0 ? linhaResumo('Busca / entrega', brl(a.valorTransporte)) : ''}
              ${linhaResumo('Total', brl(a.total), true)}
            </dl>
          </section>
          <section class="ag-bloco">
            <h4>Data e horário</h4>
            <dl>
              ${linhaResumo('Data', dataCurta(a.dataHora))}
              ${linhaResumo('Horário', esc(horaDe(a.dataHora)))}
            </dl>
          </section>
          <section class="ag-bloco">
            <h4>Local</h4>
            <dl>
              ${linhaResumo('Unidade', esc(a.unidade || 'A confirmar'))}
              ${responsavelDe(a) ? linhaResumo('Atendimento com', esc(responsavelDe(a))) : ''}
              ${unidade && unidade.endereco ? linhaResumo('Endereço', esc(unidade.endereco)) : ''}
              ${unidade && unidade.telefone ? linhaResumo('Telefone', esc(unidade.telefone)) : ''}
              ${unidade && unidade.horarioFuncionamento ? linhaResumo('Funcionamento', esc(unidade.horarioFuncionamento)) : ''}
            </dl>
          </section>
          <section class="ag-bloco">
            <h4>Transporte</h4>
            <dl>${linhaResumo('Forma', esc(a.transporte || 'Cliente leva'))}</dl>
          </section>
          <section class="ag-bloco">
            <h4>Pagamento</h4>
            <dl>
              ${linhaResumo('Forma', esc(a.formaPagamento || 'A combinar'))}
              ${linhaResumo('Situação', selo(a.pagamentoStatus))}
            </dl>
          </section>
          <section class="ag-bloco">
            <h4>Status</h4>
            <dl>
              ${linhaResumo('Situação', selo(a.status))}
              ${linhaResumo('Código', esc(a.id))}
            </dl>
          </section>
        </div>
        ${a.obs ? `<section class="ag-bloco"><h4>Observação</h4><p class="ag-aviso">${esc(a.obs)}</p></section>` : ''}
      </div>`;

    const botao = $('#ad-cancelar');
    botao.hidden = !cancelavel(a);
    botao.dataset.id = a.id;
    $('#modal-agenda-detalhes').showModal();
  }

  $('#ad-cancelar').onclick = () => {
    const id = $('#ad-cancelar').dataset.id;
    $('#modal-agenda-detalhes').close();
    abrirCancelamentoAgendamento(id);
  };

  /* --------------------------------------------------------------------
     Cancelamento — confirmação antes de chamar a API
     -------------------------------------------------------------------- */
  let agCancelando = null;

  function abrirCancelamentoAgendamento(id) {
    const a = agPorId(id);
    if (!a) { toast('Agendamento não encontrado. Atualize a página.'); return; }
    agCancelando = a.id;
    $('#agc-corpo').innerHTML = [
      linhaResumo('Pet', esc(a.pet)),
      linhaResumo('Serviço', esc(servicosDo(a))),
      linhaResumo('Data', `${dataCurta(a.dataHora)} às ${esc(horaDe(a.dataHora))}`),
      linhaResumo('Unidade', esc(a.unidade || 'A confirmar')),
      linhaResumo('Valor', brl(a.total), true)
    ].join('');
    aviso('#agc-msg', '');
    $('#modal-agenda-cancelar').showModal();
  }

  /* Mantém o nome usado pelo delegado de clique já existente. */
  function cancelarAgendamento(id) { abrirCancelamentoAgendamento(id); }

  $('#agc-confirmar').onclick = event => comBotao(event.currentTarget, 'Cancelando…', async () => {
    if (!agCancelando) return;
    aviso('#agc-msg', '');
    try {
      await api(`agendamentos/${encodeURIComponent(agCancelando)}/cancelar`, { method: 'POST' });
      /* Recarrega a conta do servidor: o status que vale e o que ficou no
         banco, nunca o que a tela imaginou. O registro continua existindo —
         o horário volta a ficar livre porque o backend ignora cancelados. */
      conta = await api('conta');
      $('#modal-agenda-cancelar').close();
      agCancelando = null;
      renderAgendamentos();
      renderResumo();
      renderProximo();
      renderPagamentos();
      toast('Agendamento cancelado.');
    } catch (erro) {
      aviso('#agc-msg', erro.message, true);
    }
  });

  /* =======================================================================
     NOVO AGENDAMENTO — fluxo em etapas

     O fluxo inteiro vive em memória. Enquanto o cliente percorre as etapas
     NADA vai para a API: o agendamento só existe no banco depois do clique em
     "Confirmar agendamento", na etapa de revisão. É o mesmo POST de antes.
     ======================================================================= */
  const TRANSPORTES = [
    { valor: 'Cliente leva o pet',          rotulo: 'Vou levar meu pet',        meta: 'Sem custo adicional',   extra: 0 },
    { valor: 'Petshop busca o pet',         rotulo: 'LanePets busca meu pet',   meta: 'R$ 15,00 pela busca',   extra: 15 },
    { valor: 'Petshop leva o pet de volta', rotulo: 'LanePets entrega meu pet', meta: 'R$ 15,00 pela entrega', extra: 15 },
    { valor: 'Busca e entrega',             rotulo: 'Busca e entrega',          meta: 'R$ 30,00 no total',     extra: 30 }
  ];
  const PAGAMENTOS_AG = ['Pix', 'Cartão', 'Dinheiro', 'A combinar'];

  const ETAPAS_AG = [
    { id: 'pet',        rotulo: 'Pet' },
    { id: 'servico',    rotulo: 'Serviço' },
    { id: 'unidade',    rotulo: 'Unidade' },
    { id: 'quando',     rotulo: 'Data e hora' },
    { id: 'transporte', rotulo: 'Transporte' },
    { id: 'pagamento',  rotulo: 'Pagamento' },
    { id: 'revisao',    rotulo: 'Revisão' }
  ];

  let agFluxo = null;

  function abrirFluxoAgendamento() {
    if (!conta.pets.length) {
      toast('Cadastre um pet antes de agendar um serviço.');
      irPara('meus-pets');
      return;
    }
    const unidades = (catalogo && catalogo.unidades) || [];
    const mes = new Date();
    mes.setDate(1);
    agFluxo = {
      etapa: 0,
      petId: conta.pets.length === 1 ? conta.pets[0].id : '',
      servicoId: '',
      unidade: unidades.length === 1 ? unidades[0].nome : '',
      data: '',
      horario: '',
      transporte: TRANSPORTES[0].valor,
      pagamento: PAGAMENTOS_AG[0],
      observacao: '',
      mesRef: mes,
      horariosLivres: null,      /* null = ainda não consultado */
      carregandoHorarios: false,
      concluido: false,
      salvo: null
    };
    pintarEtapaAgenda();
    $('#modal-agendar').showModal();
  }

  const etapaAtualAg = () => ETAPAS_AG[agFluxo.etapa];
  const servicoEscolhido = () => ((catalogo && catalogo.servicos) || []).find(s => String(s.id) === String(agFluxo.servicoId)) || null;
  const unidadeEscolhida = () => unidadePorNome(agFluxo.unidade);
  /* Item 5 do roadmap: cada unidade pode oferecer so parte dos servicos.
     Lista vazia = oferece todos (padrao das unidades antigas). */
  const unidadeOferece = (u, servicoId) => {
    const lista = (u && Array.isArray(u.servicos)) ? u.servicos : [];
    return !servicoId || !lista.length || lista.map(String).includes(String(servicoId));
  };
  const algumaUnidadeOferece = servicoId =>
    ((catalogo && catalogo.unidades) || []).some(u => unidadeOferece(u, servicoId));
  const petDoFluxoAg = () => (conta.pets || []).find(p => p.id === agFluxo.petId) || null;
  const extraTransporte = () => (TRANSPORTES.find(t => t.valor === agFluxo.transporte) || TRANSPORTES[0]).extra;
  const totalAg = () => {
    const s = servicoEscolhido();
    return (s ? Number(s.preco || 0) : 0) + extraTransporte();
  };

  function pintarEtapaAgenda() {
    if (!agFluxo) return;
    const ultima = agFluxo.etapa === ETAPAS_AG.length - 1;

    $('#aw-etapas').innerHTML = agFluxo.concluido ? '' : ETAPAS_AG.map((e, i) => `
      <li class="${i < agFluxo.etapa ? 'is-feita' : i === agFluxo.etapa ? 'is-atual' : ''}">
        <span class="cc-etapas__num">${i + 1}</span><span class="cc-etapas__rotulo">${e.rotulo}</span>
      </li>`).join('');
    $('#aw-etapas').style.display = agFluxo.concluido ? 'none' : '';

    if (agFluxo.concluido) {
      $('#aw-eyebrow').textContent = 'Agendamento';
      $('#aw-titulo').textContent = 'Agendamento confirmado! 🎉';
      $('#aw-sub').textContent = 'Seu atendimento foi agendado com sucesso.';
      $('#aw-corpo').innerHTML = corpoSucessoAg();
    } else {
      $('#aw-eyebrow').textContent = 'Novo agendamento';
      $('#aw-titulo').textContent = 'Agendar um serviço';
      $('#aw-sub').textContent = ultima
        ? 'Confira seu agendamento antes de confirmar.'
        : 'Nada é enviado antes de você conferir e confirmar no final.';
      $('#aw-corpo').innerHTML = CORPOS_AG[etapaAtualAg().id]();
      ligarEtapaAg(etapaAtualAg().id);
    }

    $('#aw-voltar').style.display = (agFluxo.etapa === 0 || agFluxo.concluido) ? 'none' : '';
    $('#aw-voltar').textContent = ultima ? '← Voltar e corrigir' : '← Voltar';
    $('#aw-avancar').textContent = agFluxo.concluido
      ? 'Ver meus agendamentos'
      : ultima ? '✓ Confirmar agendamento' : 'Continuar';
    document.querySelectorAll('#modal-agendar [data-ag-sair]').forEach(b => {
      b.style.display = agFluxo.concluido ? 'none' : '';
    });
    aviso('#aw-msg', '');
    const corpo = $('#modal-agendar .cc-modal__corpo');
    if (corpo) corpo.scrollTop = 0;
  }

  /* Corpos de cada etapa ------------------------------------------------- */
  const CORPOS_AG = {
    pet: () => `
      <div class="cc-etapa__titulo">Escolha o pet</div>
      <p class="ag-aviso">Os pets cadastrados na sua conta.</p>
      <div class="ag-escolhas">
        ${conta.pets.map(p => `
          <button type="button" class="ag-escolha${agFluxo.petId === p.id ? ' is-ativa' : ''}" data-ag-pet="${esc(p.id)}">
            <span class="ag-escolha__ico">${p.fotoUrl
              ? `<img class="pet-foto-real" src="${esc(p.fotoUrl)}" alt="" data-tipo="${esc(p.tipo)}" data-tamanho="20">`
              : ico(iconePet(p.tipo), 20)}</span>
            <span>
              <span class="ag-escolha__nome">${esc(p.petNome)}</span>
              <span class="ag-escolha__meta">${esc([p.tipo, p.raca].filter(Boolean).join(' · ') || 'Sem raça informada')}</span>
            </span>
          </button>`).join('')}
      </div>`,

    servico: () => {
      const servicos = (catalogo && catalogo.servicos) || [];
      if (!servicos.length) return '<p class="ag-aviso ag-aviso--alerta">Nenhum serviço disponível no momento. Fale com a equipe LanePets.</p>';
      return `
        <div class="cc-etapa__titulo">Escolha o serviço</div>
        <p class="ag-aviso">Serviços e preços cadastrados pela equipe LanePets.</p>
        <div class="ag-escolhas">
          ${servicos.map(s => { const livre = algumaUnidadeOferece(s.id); return `
            <button type="button" class="ag-escolha${String(agFluxo.servicoId) === String(s.id) ? ' is-ativa' : ''}" data-ag-servico="${esc(String(s.id))}"${livre ? '' : ' disabled aria-disabled="true" style="opacity:.55;cursor:not-allowed"'}>
              <span class="ag-escolha__ico">${ico(ICO_AG.servico, 20)}</span>
              <span>
                <span class="ag-escolha__nome">${esc(s.nome)}</span>
                ${livre ? '' : '<span class="ag-escolha__meta">Indisponível nas unidades no momento</span>'}
                ${s.porte ? `<span class="ag-escolha__meta">Porte ${esc(s.porte)}</span>` : ''}
                <span class="ag-escolha__preco">${brl(s.preco)}</span>
              </span>
            </button>`; }).join('')}
        </div>`;
    },

    unidade: () => {
      const unidades = (catalogo && catalogo.unidades) || [];
      if (!unidades.length) return '<p class="ag-aviso ag-aviso--alerta">Nenhuma unidade disponível no momento.</p>';
      return `
        <div class="cc-etapa__titulo">Escolha a unidade</div>
        <p class="ag-aviso">Onde o atendimento vai acontecer.</p>
        <div class="ag-escolhas ag-escolhas--largas">
          ${unidades.map(u => { const atende = unidadeOferece(u, agFluxo.servicoId); return `
            <button type="button" class="ag-escolha${agFluxo.unidade === u.nome ? ' is-ativa' : ''}" data-ag-unidade="${esc(u.nome)}"${atende ? '' : ' disabled aria-disabled="true" style="opacity:.55;cursor:not-allowed"'}>
              <span class="ag-escolha__ico">${ico(ICO_AG.local, 20)}</span>
              <span>
                <span class="ag-escolha__nome">${esc(u.nome)}</span>
                ${atende ? '' : '<span class="ag-escolha__meta">Não oferece o serviço escolhido</span>'}
                ${u.endereco ? `<span class="ag-escolha__meta">${esc(u.endereco)}</span>` : ''}
                ${u.telefone ? `<span class="ag-escolha__meta">${esc(u.telefone)}</span>` : ''}
                ${u.horarioFuncionamento ? `<span class="ag-escolha__meta">${esc(u.horarioFuncionamento)}</span>` : ''}
              </span>
            </button>`; }).join('')}
        </div>`;
    },

    quando: () => `
      <div class="cc-etapa__titulo">Escolha a data e o horário</div>
      <p class="ag-aviso">Só aparecem os horários realmente livres em ${esc(agFluxo.unidade || 'a unidade escolhida')}. A disponibilidade é conferida outra vez pelo servidor na confirmação.</p>
      <div class="ag-agenda">
        ${calendarioAg()}
        <div class="ag-horarios">
          <div class="ag-horarios__titulo">${agFluxo.data ? `Horários em ${dataCurta(agFluxo.data + 'T12:00:00')}` : 'Horários disponíveis'}</div>
          ${blocoHorariosAg()}
        </div>
      </div>`,

    transporte: () => `
      <div class="cc-etapa__titulo">Como o pet chegará até a LanePets?</div>
      <p class="ag-aviso">Busca e entrega somam R$ 15,00 cada.</p>
      <div class="ag-escolhas ag-escolhas--largas">
        ${TRANSPORTES.map(t => `
          <button type="button" class="ag-escolha${agFluxo.transporte === t.valor ? ' is-ativa' : ''}" data-ag-transporte="${esc(t.valor)}">
            <span class="ag-escolha__ico">${ico(ICO_AG.carro, 20)}</span>
            <span>
              <span class="ag-escolha__nome">${esc(t.rotulo)}</span>
              <span class="ag-escolha__meta">${esc(t.meta)}</span>
            </span>
          </button>`).join('')}
      </div>`,

    pagamento: () => `
      <div class="cc-etapa__titulo">Forma de pagamento</div>
      <p class="ag-aviso">O pagamento é combinado com a equipe no atendimento — nada é cobrado por aqui.</p>
      <div class="ag-escolhas">
        ${PAGAMENTOS_AG.map(p => `
          <button type="button" class="ag-escolha${agFluxo.pagamento === p ? ' is-ativa' : ''}" data-ag-pagamento="${esc(p)}">
            <span class="ag-escolha__ico">${ico(ICO_AG.cartao, 20)}</span>
            <span><span class="ag-escolha__nome">${esc(p)}</span></span>
          </button>`).join('')}
      </div>
      <div class="cc-campo">
        <label for="aw-obs">Observação (opcional)</label>
        <textarea id="aw-obs" rows="3" maxlength="400" placeholder="Algo que a equipe precisa saber sobre o seu pet?">${esc(agFluxo.observacao)}</textarea>
      </div>`,

    revisao: () => {
      const s = servicoEscolhido();
      const u = unidadeEscolhida();
      const p = petDoFluxoAg();
      return `
        <div class="cc-etapa__titulo">Confira seu agendamento</div>
        <div class="cc-resumo-bloco">
          ${linhaResumo('Pet', esc(p ? p.petNome : '—'))}
          ${linhaResumo('Serviço', esc(s ? s.nome : '—'))}
          ${linhaResumo('Unidade', esc(u ? u.nome : agFluxo.unidade || '—'))}
          ${u && u.endereco ? linhaResumo('Endereço', esc(u.endereco)) : ''}
          ${linhaResumo('Data', dataCurta(agFluxo.data + 'T12:00:00'))}
          ${linhaResumo('Horário', esc(agFluxo.horario))}
          ${linhaResumo('Transporte', esc(agFluxo.transporte))}
          ${linhaResumo('Pagamento', esc(agFluxo.pagamento))}
          ${agFluxo.observacao ? linhaResumo('Observação', esc(agFluxo.observacao)) : ''}
          ${extraTransporte() > 0 ? linhaResumo('Busca / entrega', brl(extraTransporte())) : ''}
          ${linhaResumo('Valor', brl(totalAg()), true)}
        </div>
        <p class="ag-aviso">O agendamento só é enviado depois que você confirmar. O horário é conferido novamente pelo servidor neste momento.</p>`;
    }
  };

  /* Calendário ------------------------------------------------------------
     Mês a mês, sem datas passadas. O dia só vira escolha de horário depois
     que o servidor disser quais horários estão livres naquela unidade. */
  const NOMES_SEMANA = ['dom', 'seg', 'ter', 'qua', 'qui', 'sex', 'sáb'];

  function calendarioAg() {
    const ref = agFluxo.mesRef;
    const ano = ref.getFullYear(), mes = ref.getMonth();
    const primeiro = new Date(ano, mes, 1);
    const dias = new Date(ano, mes + 1, 0).getDate();
    const hoje = hojeISO();
    const agora = new Date();
    const semAnterior = ano < agora.getFullYear() || (ano === agora.getFullYear() && mes <= agora.getMonth());

    const celulas = [];
    for (let i = 0; i < primeiro.getDay(); i++) celulas.push('<span class="ag-cal__dia ag-cal__dia--vazio"></span>');
    for (let d = 1; d <= dias; d++) {
      const iso = `${ano}-${String(mes + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
      const passado = iso < hoje;
      celulas.push(`<button type="button" class="ag-cal__dia${iso === hoje ? ' is-hoje' : ''}${iso === agFluxo.data ? ' is-ativo' : ''}"
        ${passado ? 'disabled' : ''} data-ag-dia="${iso}">${d}</button>`);
    }

    return `
      <div class="ag-cal">
        <div class="ag-cal__topo">
          <button type="button" class="ag-cal__nav" data-ag-mes="-1" ${semAnterior ? 'disabled' : ''} aria-label="Mês anterior">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M14 6l-6 6 6 6"/></svg>
          </button>
          <span class="ag-cal__mes">${ref.toLocaleDateString('pt-BR', { month: 'long', year: 'numeric' })}</span>
          <button type="button" class="ag-cal__nav" data-ag-mes="1" aria-label="Próximo mês">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="m10 6 6 6-6 6"/></svg>
          </button>
        </div>
        <div class="ag-cal__semana">${NOMES_SEMANA.map(d => `<span>${d}</span>`).join('')}</div>
        <div class="ag-cal__grade">${celulas.join('')}</div>
      </div>`;
  }

  function blocoHorariosAg() {
    if (!agFluxo.data) return '<p class="ag-aviso">Escolha uma data no calendário para ver os horários livres.</p>';
    if (agFluxo.carregandoHorarios) {
      return `<div class="ag-horarios__grade">${[0,1,2,3,4,5].map(() => '<span class="ag-skel" style="height:38px"></span>').join('')}</div>`;
    }
    if (agFluxo.horariosLivres === null) return '<p class="ag-aviso">Escolha uma data no calendário para ver os horários livres.</p>';
    if (!agFluxo.horariosLivres.length) {
      return '<p class="ag-aviso ag-aviso--alerta">Nenhum horário livre nesta data. Escolha outro dia ou outra unidade.</p>';
    }
    return `<div class="ag-horarios__grade">
      ${agFluxo.horariosLivres.map(h => `
        <button type="button" class="ag-hora${agFluxo.horario === h ? ' is-ativa' : ''}" data-ag-hora="${esc(h)}">${esc(h)}</button>`).join('')}
    </div>`;
  }

  function corpoSucessoAg() {
    const a = agFluxo.salvo || {};
    const u = unidadePorNome(a.unidade);
    return `
      <div class="ag-sucesso">
        <span class="ag-sucesso__marca">${ico(ICO_AG.check, 30)}</span>
        <p>Guarde os dados abaixo. A equipe LanePets confirma o atendimento com você.</p>
        <div class="cc-resumo-bloco">
          ${linhaResumo('Pet', esc(a.pet))}
          ${linhaResumo('Serviço', esc(servicosDo(a)))}
          ${linhaResumo('Data', dataCurta(a.dataHora))}
          ${linhaResumo('Horário', esc(horaDe(a.dataHora)))}
          ${linhaResumo('Unidade', esc(a.unidade || '—'))}
          ${u && u.endereco ? linhaResumo('Endereço', esc(u.endereco)) : ''}
          ${linhaResumo('Status', selo(a.status))}
          ${linhaResumo('Valor', brl(a.total), true)}
        </div>
      </div>`;
  }

  /* Interações de cada etapa --------------------------------------------- */
  function ligarEtapaAg(id) {
    if (id === 'pagamento') {
      const campo = $('#aw-obs');
      if (campo) campo.oninput = () => { agFluxo.observacao = campo.value; };
    }
  }

  async function carregarHorariosAg() {
    agFluxo.carregandoHorarios = true;
    agFluxo.horario = '';
    pintarEtapaAgenda();
    try {
      const livres = await api(`horarios?unidade=${encodeURIComponent(agFluxo.unidade)}&data=${encodeURIComponent(agFluxo.data)}`);
      agFluxo.horariosLivres = Array.isArray(livres) ? livres : [];
      agFluxo.carregandoHorarios = false;
      pintarEtapaAgenda();
    } catch (erro) {
      agFluxo.horariosLivres = [];
      agFluxo.carregandoHorarios = false;
      pintarEtapaAgenda();
      aviso('#aw-msg', 'Não foi possível consultar os horários. ' + erro.message, true);
    }
  }

  /* Cliques dentro do wizard, delegados no corpo do modal. */
  $('#aw-corpo').addEventListener('click', async event => {
    if (!agFluxo || agFluxo.concluido) return;
    const alvo = event.target.closest('[data-ag-pet],[data-ag-servico],[data-ag-unidade],[data-ag-transporte],[data-ag-pagamento],[data-ag-dia],[data-ag-hora],[data-ag-mes]');
    if (!alvo) return;
    const d = alvo.dataset;

    if (d.agPet !== undefined)             { agFluxo.petId = d.agPet; }
    else if (d.agServico !== undefined)    {
      agFluxo.servicoId = d.agServico;
      const u = unidadeEscolhida();
      if (u && !unidadeOferece(u, agFluxo.servicoId)) {
        agFluxo.unidade = ''; agFluxo.data = ''; agFluxo.horario = ''; agFluxo.horariosLivres = null;
      }
    }
    else if (d.agUnidade !== undefined)    {
      /* Trocar de unidade invalida a data e os horários já consultados:
         a disponibilidade é por unidade. */
      if (agFluxo.unidade !== d.agUnidade) {
        agFluxo.unidade = d.agUnidade;
        agFluxo.data = ''; agFluxo.horario = ''; agFluxo.horariosLivres = null;
      }
    }
    else if (d.agTransporte !== undefined) { agFluxo.transporte = d.agTransporte; }
    else if (d.agPagamento !== undefined)  { agFluxo.pagamento = d.agPagamento; }
    else if (d.agMes !== undefined) {
      agFluxo.mesRef = new Date(agFluxo.mesRef.getFullYear(), agFluxo.mesRef.getMonth() + Number(d.agMes), 1);
    }
    else if (d.agDia !== undefined) { agFluxo.data = d.agDia; await carregarHorariosAg(); return; }
    else if (d.agHora !== undefined) { agFluxo.horario = d.agHora; }

    pintarEtapaAgenda();
  });

  /* Validação de cada etapa antes de avançar. */
  function validarEtapaAg() {
    switch (etapaAtualAg().id) {
      case 'pet':        return agFluxo.petId ? '' : 'Escolha o pet que vai ser atendido.';
      case 'servico':    return agFluxo.servicoId ? '' : 'Escolha o serviço desejado.';
      case 'unidade':
        if (!agFluxo.unidade) return 'Escolha a unidade do atendimento.';
        return unidadeOferece(unidadeEscolhida(), agFluxo.servicoId) ? '' : 'Esta unidade não oferece o serviço escolhido.';
      case 'quando':
        if (!agFluxo.data) return 'Escolha uma data no calendário.';
        if (!agFluxo.horario) return 'Escolha um horário disponível.';
        return '';
      case 'transporte': return agFluxo.transporte ? '' : 'Escolha como o pet chega até a LanePets.';
      case 'pagamento':  return agFluxo.pagamento ? '' : 'Escolha a forma de pagamento.';
      default: return '';
    }
  }

  $('#aw-voltar').onclick = () => {
    if (!agFluxo || agFluxo.etapa === 0) return;
    agFluxo.etapa -= 1;
    pintarEtapaAgenda();
  };

  $('#aw-avancar').onclick = async event => {
    if (!agFluxo) return;
    if (agFluxo.concluido) { fecharFluxoAgenda(); irPara('agendar'); return; }

    const erro = validarEtapaAg();
    if (erro) { aviso('#aw-msg', erro, true); return; }

    if (etapaAtualAg().id !== 'revisao') {
      agFluxo.etapa += 1;
      /* Ao chegar na etapa de data com uma data já preservada, os horários são
         reconsultados: outro cliente pode ter ocupado o mesmo horário nesse
         meio tempo. */
      if (etapaAtualAg().id === 'quando' && agFluxo.data) { await carregarHorariosAg(); return; }
      pintarEtapaAgenda();
      return;
    }

    /* Última etapa: é a gravação de verdade. O botão trava enquanto a
       requisição corre, para dois cliques não virarem dois agendamentos.
       O rótulo NÃO é restaurado quando dá certo — nesse caso pintarEtapaAgenda
       já o trocou para "Ver meus agendamentos". */
    const botao = event.currentTarget;
    const rotulo = botao.textContent;
    botao.disabled = true;
    botao.textContent = 'Confirmando…';
    try { await confirmarAgendamento(); }
    finally {
      botao.disabled = false;
      if (agFluxo && !agFluxo.concluido) botao.textContent = rotulo;
    }
  };

  async function confirmarAgendamento() {
    aviso('#aw-msg', '');
    try {
      const salvo = await api('agendamentos', { method: 'POST', body: JSON.stringify({
        petId: agFluxo.petId,
        servicoId: agFluxo.servicoId,
        unidade: agFluxo.unidade,
        data: agFluxo.data,
        horario: agFluxo.horario,
        transporte: agFluxo.transporte,
        formaPagamento: agFluxo.pagamento,
        observacao: agFluxo.observacao
      }) });
      agFluxo.salvo = salvo;
      agFluxo.concluido = true;

      /* A conta é relida do servidor: a lista, os indicadores e a área de
         pagamentos passam a mostrar o registro como ele ficou no banco. */
      conta = await api('conta');
      renderAgendamentos();
      renderResumo();
      renderProximo();
      renderPagamentos();
      pintarEtapaAgenda();
    } catch (erro) {
      /* Conflito de horário, pet de outra conta, serviço inexistente: a
         mensagem vem do servidor, que é quem manda. Se o horário foi tomado,
         o cliente volta para a etapa de data com a lista já recarregada. */
      const mensagem = erro.message || 'Não foi possível concluir o agendamento.';
      if (/hor[áa]rio/i.test(mensagem)) {
        agFluxo.etapa = ETAPAS_AG.findIndex(e => e.id === 'quando');
        agFluxo.horario = '';
        await carregarHorariosAg();
      }
      aviso('#aw-msg', mensagem, true);
    }
  }

  function fecharFluxoAgenda() {
    $('#modal-agendar').close();
    agFluxo = null;
  }

  function pedirParaSairDoAgendamento() {
    if (!agFluxo || agFluxo.concluido) { fecharFluxoAgenda(); return; }
    const semNada = !agFluxo.servicoId && !agFluxo.data && !agFluxo.horario && !agFluxo.observacao;
    if (semNada) { fecharFluxoAgenda(); return; }
    $('#modal-agendar-descartar').showModal();
  }
  $('#ax-confirmar').onclick = () => { $('#modal-agendar-descartar').close(); fecharFluxoAgenda(); };
  $('#modal-agendar').addEventListener('cancel', event => { event.preventDefault(); pedirParaSairDoAgendamento(); });
  $('#modal-agendar').addEventListener('click', event => {
    if (event.target === $('#modal-agendar')) { event.stopPropagation(); pedirParaSairDoAgendamento(); }
  }, true);

  /* Ações da tela de agendamentos ---------------------------------------- */
  document.addEventListener('click', event => {
    if (event.target.closest('[data-ag-sair]')) { pedirParaSairDoAgendamento(); return; }
    if (event.target.closest('[data-ag-novo]')) { abrirFluxoAgendamento(); return; }
    const ver = event.target.closest('[data-ag-ver]');
    if (ver) { abrirDetalhesAgendamento(ver.dataset.agVer); return; }
    const filtro = event.target.closest('[data-ag-filtro]');
    if (filtro) { agFiltro = filtro.dataset.agFiltro; renderAgendamentos(); return; }
    if (event.target.closest('[data-ag-limpar]')) {
      agFiltro = 'todos'; agFiltroPet = ''; agFiltroUnidade = '';
      renderAgendamentos();
      return;
    }
    if (event.target.closest('[data-ag-recarregar]')) { recarregarAgendamentos(); return; }
  });

  document.addEventListener('change', event => {
    if (event.target.id === 'ag-filtro-select') { agFiltro = event.target.value; renderAgendamentos(); }
    else if (event.target.id === 'ag-filtro-pet') { agFiltroPet = event.target.value; renderAgendamentos(); }
    else if (event.target.id === 'ag-filtro-unidade') { agFiltroUnidade = event.target.value; renderAgendamentos(); }
    else if (event.target.id === 'pg-filtro-tipo') { pgFiltroTipo = event.target.value; renderPagamentos(); }
    else if (event.target.id === 'pg-filtro-situacao') { pgFiltroSituacao = event.target.value; renderPagamentos(); }
  });

  /* Busca em tempo real, sem esperar o Enter — mesmo comportamento das
     buscas do painel administrativo (Clientes, Pedidos, Produtos). */
  document.addEventListener('input', event => {
    if (event.target.id === 'pg-busca') { pgBusca = event.target.value; renderPagamentos(); }
  });

  document.addEventListener('click', event => {
    if (event.target.closest('[data-pg-limpar]')) {
      pgBusca = ''; pgFiltroTipo = ''; pgFiltroSituacao = '';
      const busca = $('#pg-busca'); if (busca) busca.value = '';
      const sitSel = $('#pg-filtro-situacao'); if (sitSel) sitSel.value = '';
      renderPagamentos();
    }
  });

  /* "Tentar novamente" do estado de erro: relê a conta, sem recarregar a
     página e sem tocar nas outras áreas que já estão na tela. */
  async function recarregarAgendamentos() {
    agEstado = 'carregando';
    renderAgendamentos();
    try {
      conta = await api('conta');
      agEstado = 'pronto';
      renderAgendamentos();
      renderResumo();
      renderProximo();
      renderPagamentos();
    } catch (erro) {
      console.error('[LanePets] Falha ao recarregar agendamentos:', erro);
      agEstado = 'erro';
      renderAgendamentos();
    }
  }

  /* =======================================================================
     MEUS PEDIDOS

     Antes era uma linha de lista dentro da mesma tela do formulario. Agora e
     area propria e cada pedido e um card: numero, data, produto, valor e
     status, com "Ver detalhes" abrindo o resto.

     Os dados sao os de sempre, de GET /api/cliente/conta — que o backend ja
     limita ao cliente da sessao (ClientPortalController.Cliente() resolve o
     cliente pelo token, nao por parametro da URL). Um cliente nao alcanca o
     pedido de outro nem trocando nada no navegador.
     ======================================================================= */

  /** Numero curto e estavel a partir do id que o pedido ja tem. */
  const numeroPedido = pedido => '#' + String(pedido.id || '').replace(/[^0-9A-Za-z]/g, '').slice(-6).toUpperCase();

  /* Nome distinto de propósito: cardPedido() ja existe nesta mesma IIFE para o
     card da VITRINE. Duas funcoes com o mesmo nome — a segunda vence — faziam a
     vitrine desenhar cards de historico. */
  /* Pedido.Unidade guarda o id ("franco"). O nome vem do catalogo; uma
     unidade desativada nao esta no catalogo, entao cai no proprio id. */
  function nomeRetirada(id) {
    const u = ((catalogo && catalogo.unidades) || []).find(x => x.id === id || x.nome === id);
    return u ? u.nome : (String(id).charAt(0).toUpperCase() + String(id).slice(1));
  }

  function cardHistorico(pedido, indice) {
    return `<article class="ped-card">
      <div class="ped-card__topo">
        <div>
          <strong>Pedido ${numeroPedido(pedido)}</strong>
          <span>${dataCurta(pedido.criadoEm)}</span>
        </div>
        ${selo(pedido.status)}
      </div>
      <div class="ped-card__corpo">
        <span class="ped-card__ico">${ico(ICO.carrinho, 19)}</span>
        <div class="ped-card__txt">
          <strong>${esc(pedido.produtoNome)}</strong>
          <span>${pedido.quantidade} ${Number(pedido.quantidade) === 1 ? 'unidade' : 'unidades'} · ${esc(pedido.formaPagamento || 'A combinar')}</span>
          ${pedido.unidade ? `<span>Retirada: ${esc(nomeRetirada(pedido.unidade))}</span>` : ''}
        </div>
        <span class="cc-valor">${brl(pedido.total)}</span>
      </div>
      <div class="ped-card__acoes">
        <button class="botao claro" type="button" data-pedido="${indice}">Ver detalhes</button>
        ${String(pedido.status || 'Pendente').toLowerCase() === 'pendente'
          ? `<button class="botao claro" type="button" data-pedido-cancelar="${esc(pedido.id)}">Cancelar pedido</button>` : ''}
      </div>
    </article>`;
  }

  function renderPedidos() {
    if (!conta.pedidos.length) {
      $('#lista-pedidos').innerHTML = vazio(
        'Nenhum pedido ainda',
        'Escolha um produto no catálogo para fazer o seu primeiro pedido.',
        '<a class="botao primario" href="#produtos" data-rota="produtos">Ver produtos</a>');
      return;
    }
    /* Mais recentes primeiro: o pedido que interessa e quase sempre o ultimo. */
    const ordenados = conta.pedidos
      .map((pedido, indice) => ({ pedido, indice }))
      .sort((a, b) => String(b.pedido.criadoEm).localeCompare(String(a.pedido.criadoEm)));

    $('#lista-pedidos').innerHTML =
      `<div class="ped-grade">${ordenados.map(({ pedido, indice }) => cardHistorico(pedido, indice)).join('')}</div>`;
  }

  /** Detalhes do pedido. So mostra campo que existe no sistema. */
  function abrirPedido(indice) {
    const pedido = conta.pedidos[indice];
    if (!pedido) return;

    $('#ped-titulo').textContent = 'Pedido ' + numeroPedido(pedido);
    $('#ped-data').textContent = 'Feito em ' + dataCurta(pedido.criadoEm);

    const unitario = Number(pedido.quantidade) > 0
      ? Number(pedido.total || 0) / Number(pedido.quantidade)
      : Number(pedido.total || 0);

    $('#ped-item').innerHTML = `<div class="ped-item">
      <span class="ped-card__ico">${ico(ICO.carrinho, 19)}</span>
      <div class="ped-card__txt">
        <strong>${esc(pedido.produtoNome)}</strong>
        <span>${pedido.quantidade} × ${brl(unitario)}</span>
      </div>
      <span class="cc-valor">${brl(pedido.total)}</span>
    </div>`;

    /* O pedido do LanePets tem um produto, uma quantidade e um total. Nao ha
       desconto nem frete no sistema, entao nao ha linha de desconto nem de
       entrega aqui — inventar essas linhas seria mostrar numero que ninguem
       cobrou. A retirada e combinada na unidade, como diz o formulario. */
    $('#ped-resumo').innerHTML = [
      ['Quantidade', `${pedido.quantidade}`],
      ['Valor unitário', brl(unitario)],
      ['Total', brl(pedido.total)],
      ['Pagamento', esc(pedido.formaPagamento || 'A combinar')],
      ['Status', selo(pedido.status)],
      ['Retirada', pedido.unidade ? esc(nomeRetirada(pedido.unidade)) : 'Combinada com a equipe na unidade']
    ].map(([rotulo, valor]) => `<div><dt>${rotulo}</dt><dd>${valor}</dd></div>`).join('');

    $('#modal-pedido').showModal();
  }

  /* Item 16: busca + filtros no extrato de Pagamentos, mesmo padrao ja usado
     em Agendamentos (filtro por pilula/select) e no Historico (filtro por pet):
     estado em variavel de modulo, select populado uma vez, re-render no change/input. */
  let pgBusca = '';
  let pgFiltroTipo = '';
  let pgFiltroSituacao = '';

  /* Item 8 (25/09): o status vem da entidade Pagamento (Pendente, Aprovado,
     Recusado, Cancelado, Reembolsado). "Pago" antigo conta como Aprovado. */
  const STATUS_PAG = { pago: 'Aprovado', aprovado: 'Aprovado', pendente: 'Pendente', 'a pagar': 'Pendente',
    recusado: 'Recusado', cancelado: 'Cancelado', cancelada: 'Cancelado', reembolsado: 'Reembolsado' };
  const statusPag = v => STATUS_PAG[String(v || '').trim().toLowerCase()] || 'Pendente';
  const situacaoPagamento = l => l.cancelado ? 'Cancelado' : statusPag(l.status);
  const TIPO_ORIGEM = { agendamento: ['Serviço', 'agenda'], pedido: ['Produto', 'carrinho'], seguro: ['Seguro', 'escudo'] };

  function lancamentosDaConta() {
    /* Servidor novo: lista pronta de pagamentos, com o status real. */
    if (Array.isArray(conta.pagamentos)) {
      return conta.pagamentos.map(p => {
        const [tipo, icone] = TIPO_ORIGEM[p.origem] || ['Pagamento', 'cartao'];
        return {
          tipo, icone: ICO[icone] || ICO.cartao,
          buscaTexto: `${p.descricao || ''}`.toLowerCase(),
          titulo: esc(p.descricao),
          quando: p.dataReferencia, valor: Number(p.valor || 0),
          forma: p.forma || 'A combinar',
          status: statusPag(p.status),
          cancelado: statusPag(p.status) === 'Cancelado',
          reembolso: !!p.reembolsoPendente
        };
      }).sort((a, b) => new Date(b.quando) - new Date(a.quando));
    }
    return lancamentosDerivados();
  }

  function renderPagamentos() {
    const lancamentos = lancamentosDaConta();
    renderPagamentosLista(lancamentos);
  }

  /* Servidor antigo (sem pagamentos na conta): extrato derivado, como antes. */
  function lancamentosDerivados() {
    return [
      ...conta.agendamentos.map(a => ({
        tipo: 'Serviço', icone: ICO.agenda,
        buscaTexto: `${a.pet || ''} ${servicosDo(a) || ''}`.toLowerCase(),
        titulo: `${esc(a.pet)} · ${esc(servicosDo(a))}`,
        quando: a.dataHora, valor: Number(a.total || 0),
        forma: a.formaPagamento || 'A combinar',
        status: a.pagamentoStatus || a.status || 'Pendente',
        cancelado: String(a.status).toLowerCase() === 'cancelado'
      })),
      ...conta.pedidos.map(p => ({
        tipo: 'Produto', icone: ICO.carrinho,
        buscaTexto: `${p.produtoNome || ''}`.toLowerCase(),
        titulo: esc(p.produtoNome),
        quando: p.criadoEm, valor: Number(p.total || 0),
        forma: p.formaPagamento || 'A combinar',
        status: p.status || 'Pendente',
        cancelado: String(p.status).toLowerCase() === 'cancelado'
      })),
      /* Seguro Pet: a mensalidade contratada aparece no mesmo extrato dos
         servicos e dos pedidos, com a forma de pagamento escolhida e o status
         real do pagamento — nunca "pago" sem confirmacao da equipe. */
      ...segurosContratados.map(s => ({
        tipo: 'Seguro', icone: ICO.escudo,
        buscaTexto: `${s.nomePlano || ''} ${s.nomePet || ''}`.toLowerCase(),
        titulo: `${esc(s.nomePlano)} · ${esc(s.nomePet || '')}`,
        quando: s.criadoEm, valor: Number(s.valor || s.valorMensal || 0),
        forma: s.metodoPagamento || 'A combinar',
        status: s.pagamentoStatus || 'Pendente',
        cancelado: String(s.status).toLowerCase() === 'cancelada'
      }))
    ].sort((a, b) => new Date(b.quando) - new Date(a.quando));
  }

  function renderPagamentosLista(lancamentos) {

    /* O resumo (Total/Confirmados/A confirmar) sempre reflete a conta
       inteira, nao o filtro — igual ao resumo de Agendamentos, que nao muda
       quando o cliente troca de pilula. Quem filtra e so a lista abaixo. */
    const validos = lancamentos.filter(l => !l.cancelado && statusPag(l.status) !== 'Reembolsado');
    const pagos = validos.filter(l => statusPag(l.status) === 'Aprovado');
    const pendentes = validos.filter(l => ['Pendente', 'Recusado'].includes(statusPag(l.status)));

    $('#cc-resumo-pagamentos').innerHTML = [
      metrica({ icone: ICO.cartao, rotulo: 'Total', valor: brl(validos.reduce((s, l) => s + l.valor, 0)), nota: `${validos.length} lançamento(s)` }),
      metrica({ icone: ICO.escudo, rotulo: 'Confirmados', valor: brl(pagos.reduce((s, l) => s + l.valor, 0)), nota: `${pagos.length} com pagamento registrado` }),
      metrica({ icone: ICO.relogio, rotulo: 'A confirmar', valor: brl(pendentes.reduce((s, l) => s + l.valor, 0)), nota: `${pendentes.length} aguardando a equipe`, acento: true })
    ].join('');

    /* Opcoes do filtro de tipo: so os tipos que a conta realmente tem, na
       mesma logica ja usada para pet/unidade em Agendamentos. */
    const selTipo = $('#pg-filtro-tipo');
    if (selTipo) {
      const tipos = [...new Set(lancamentos.map(l => l.tipo))];
      selTipo.innerHTML = '<option value="">Todos os tipos</option>'
        + tipos.map(t => `<option value="${esc(t)}" ${pgFiltroTipo === t ? 'selected' : ''}>${esc(t)}</option>`).join('');
      selTipo.parentElement.hidden = tipos.length < 2;
    }

    const termo = pgBusca.trim().toLowerCase();
    const filtrados = lancamentos.filter(l =>
      (!termo || l.buscaTexto.includes(termo)) &&
      (!pgFiltroTipo || l.tipo === pgFiltroTipo) &&
      (!pgFiltroSituacao || situacaoPagamento(l) === pgFiltroSituacao));

    $('#cc-pagamentos').innerHTML = filtrados.length
      ? '<div class="cc-lista">' + filtrados.map(l => linha({
          icone: l.icone,
          titulo: l.titulo,
          meta: `${l.tipo} · ${dataCurta(l.quando)}<br>${esc(l.forma)}`,
          fim: `${selo(l.cancelado ? 'Cancelado' : statusPag(l.status))}${l.reembolso ? '<span class="cc-nota" style="display:block">Reembolso em análise</span>' : ''}<span class="cc-valor">${brl(l.valor)}</span>`
        })).join('') + '</div>'
      : (lancamentos.length
          ? vazio('Nenhum lançamento encontrado', 'Tente ajustar a busca ou os filtros.',
              '<button class="botao claro" type="button" data-pg-limpar>Limpar filtros</button>')
          : vazio('Nenhum lançamento', 'Agendamentos e pedidos aparecem aqui com o valor e a forma de pagamento combinada.'));
  }

  /* =====================================================================
     SEGURO PET
     Os planos sao catalogo (existem antes de qualquer cliente); o contrato
     nasce so quando esta pessoa conclui as etapas, e fica vinculado a conta,
     ao pet escolhido e a forma de pagamento informada.

     A contratacao NAO acontece no clique do plano: o clique abre o fluxo
     (plano -> pet -> responsavel -> pagamento -> resumo) e so o "Confirmar
     contratacao" do resumo chama a API. Antes disso nada e gravado.
     ===================================================================== */
  const listaDe = texto => String(texto || '').split(';').map(t => t.trim()).filter(Boolean);
  const digitos = v => String(v || '').replace(/\D/g, '');

  const METODOS = [
    { id: 'PIX', nota: 'Chave enviada pela equipe. O pagamento fica pendente até a confirmação.' },
    { id: 'Cartão de crédito', nota: 'Cobrança mensal combinada com a equipe LanePets.' },
    { id: 'Cartão de débito', nota: 'Débito combinado com a equipe LanePets.' }
  ];
  const ETAPAS = [
    { id: 'plano', rotulo: '1. Plano' },
    { id: 'pet', rotulo: '2. Pet' },
    { id: 'dados', rotulo: '3. Responsável' },
    { id: 'pagamento', rotulo: '4. Pagamento' },
    { id: 'resumo', rotulo: '5. Resumo' }
  ];

  let planosSeguro = [];        /* catalogo vindo de /api/public/seguros */
  let segurosContratados = [];  /* contratos desta conta, vindos da API   */
  let segurosCarregados = false; /* so vira true depois da resposta real  */
  let fluxo = null;             /* estado da contratacao em andamento     */

  const ehCartao = metodo => /cart/i.test(String(metodo || ''));

  async function renderSeguro() {
    const caixa = $('#cc-planos');
    caixa.innerHTML = '<div class="cc-esqueleto" style="height:150px;grid-column:1/-1"></div>';

    try {
      const resposta = await fetch('/api/public/seguros');
      const corpo = await resposta.json();
      if (!resposta.ok || !corpo.ok) throw new Error(corpo.error || 'Não foi possível carregar os planos.');
      planosSeguro = corpo.data || [];
    } catch (erro) {
      caixa.innerHTML = vazio('Não foi possível carregar os planos', erro.message);
      return;
    }

    /* Falha ao ler os contratos NAO pode virar "você ainda não contratou":
       isso faria o cliente achar que perdeu o seguro dele. */
    try {
      segurosContratados = await api('seguros');
      segurosCarregados = true;
      /* O card "Seguro Pet" da visao geral so sabe o status depois desta
         resposta; por isso o resumo e redesenhado aqui. */
      renderResumo();
    } catch (erro) {
      caixa.innerHTML = vazio('Não foi possível carregar seus contratos', erro.message);
      return;
    }

    const meus = segurosContratados.length
      ? `<div class="cc-lista" style="grid-column:1/-1">` + segurosContratados.map(c => linha({
          icone: ICO.escudo,
          titulo: `${esc(c.nomePlano)} · ${esc(c.nomePet || 'pet não informado')}`,
          meta: [
            `Contratado em ${dataCurta(c.criadoEm)}`,
            c.metodoPagamento
              ? `Pagamento: ${esc(c.metodoPagamento)}${c.cartaoFinal ? ' **** ' + esc(c.cartaoFinal) : ''} · ${esc(c.pagamentoStatus || 'Pendente')}`
              : '',
            c.dataCancelamento ? `<strong>Cancelado em ${dataCurta(c.dataCancelamento)}</strong>` : '',
            listaDe(c.coberturas).length ? 'Cobertura: ' + esc(listaDe(c.coberturas).join(', ')) : '',
            c.beneficios ? 'Benefícios: ' + esc(c.beneficios) : '',
            c.condicoes ? 'Condições: ' + esc(c.condicoes) : '',
            c.observacao ? esc(c.observacao) : ''
          ].filter(Boolean).join('<br>'),
          /* O botao de cancelar so aparece enquanto o contrato vale. Quem
             decide e o servidor (podeCancelar), nao a tela. */
          fim: `${selo(c.status)}<span class="cc-valor">${brl(c.valor || c.valorMensal)}/mês</span>`
            + (c.podeCancelar ? `<button type="button" class="botao claro" data-cancelar-seguro="${esc(c.id)}">Cancelar seguro</button>` : '')
        })).join('') + '</div>'
      : `<p class="cc-nota" style="grid-column:1/-1">Você ainda não contratou nenhum plano. Escolha um abaixo.</p>`;

    const semPet = !conta.pets.length
      ? `<p class="cc-nota" style="grid-column:1/-1">Você pode cadastrar o pet durante a contratação ou em <a href="#meus-pets" data-ir="meus-pets">Meus pets</a>.</p>`
      : '';

    caixa.innerHTML = meus + semPet + (planosSeguro.length ? planosSeguro.map((plano, i) => `
      <article class="cc-plano${i === 1 ? ' cc-plano--destaque' : ''}">
        <h3>${esc(plano.nome)}</h3>
        <p>${esc(plano.descricao)}</p>
        <ul>${listaDe(plano.coberturas).map(c => `<li>${esc(c)}</li>`).join('')}</ul>
        ${plano.beneficios ? `<p class="cc-nota">${esc(plano.beneficios)}</p>` : ''}
        ${plano.condicoes ? `<p class="cc-nota"><strong>Condições:</strong> ${esc(plano.condicoes)}</p>` : ''}
        <div class="cc-plano__preco">${brl(plano.valorMensal)}<small>/mês</small></div>
        <button type="button" class="botao ${i === 1 ? 'primario' : 'claro'}" data-contratar="${esc(plano.id)}">Contratar este plano</button>
      </article>`).join('')
      : vazio('Nenhum plano disponível', 'Fale com a equipe LanePets para conhecer as opções de proteção para o seu pet.'));

    /* Os contratos acabaram de ser lidos: o extrato de Pagamentos e repintado
       para incluir as mensalidades do seguro. */
    renderPagamentos();
  }

  /* ---------------------------------------------------------------------
     Fluxo de contratacao em etapas
     --------------------------------------------------------------------- */
  function abrirContratacao(planoId) {
    const plano = planosSeguro.find(p => p.id === planoId);
    if (!plano) { toast('Plano indisponível. Atualize a página.'); return; }
    fluxo = {
      etapa: 0,
      plano,
      petId: conta.pets.length ? conta.pets[0].id : '',
      novoPet: false,
      metodo: '',
      cartao: { numero: '', nome: '', validade: '', cvv: '' },
      concluido: false
    };
    aviso('#sg-msg', '');
    pintarEtapa();
    $('#modal-seguro').showModal();
  }

  function pintarEtapa() {
    if (!fluxo) return;
    const etapa = ETAPAS[fluxo.etapa];
    $('#sg-etapas').innerHTML = ETAPAS.map((e, i) =>
      `<li class="${i < fluxo.etapa ? 'is-feita' : i === fluxo.etapa ? 'is-atual' : ''}">${e.rotulo}</li>`).join('');
    $('#sg-etapas').style.display = fluxo.concluido ? 'none' : '';
    $('#sg-corpo').innerHTML = fluxo.concluido ? corpoSucesso() : CORPOS[etapa.id]();
    $('#sg-titulo').textContent = fluxo.concluido ? 'Seguro contratado com sucesso!' : `Contratar ${fluxo.plano.nome}`;
    $('#sg-sub').textContent = fluxo.concluido
      ? 'O contrato já aparece em Seguro Pet, na sua conta.'
      : 'Nada é registrado antes de você confirmar no resumo.';
    $('#sg-voltar').style.display = (fluxo.etapa === 0 || fluxo.concluido) ? 'none' : '';
    $('#sg-avancar').textContent = fluxo.concluido
      ? 'Fechar'
      : (etapa.id === 'resumo' ? 'Confirmar contratação' : 'Continuar');
    aviso('#sg-msg', '');
    ligarCamposDaEtapa(etapa.id);
  }

  const CORPOS = {
    plano: () => {
      const p = fluxo.plano;
      return `
        <div class="cc-etapa__titulo">Plano escolhido</div>
        <div class="cc-resumo-bloco">
          <div class="cc-resumo-linha"><span>Plano</span><strong>${esc(p.nome)}</strong></div>
          <div class="cc-resumo-linha cc-resumo-linha--total"><span>Preço</span><strong>${brl(p.valorMensal)}/mês</strong></div>
          <div class="cc-resumo-linha"><span>Cobertura</span><strong>${esc(listaDe(p.coberturas).join(', ') || '—')}</strong></div>
          <div class="cc-resumo-linha"><span>Benefícios</span><strong>${esc(p.beneficios || '—')}</strong></div>
          <div class="cc-resumo-linha"><span>Condições</span><strong>${esc(p.condicoes || '—')}</strong></div>
        </div>
        <p class="cc-nota">${esc(p.descricao || '')}</p>`;
    },

    pet: () => `
      <div class="cc-etapa__titulo">Selecione o pet</div>
      ${conta.pets.map(p => `
        <label class="cc-opcao">
          <input type="radio" name="sg-pet" value="${esc(p.id)}" ${!fluxo.novoPet && fluxo.petId === p.id ? 'checked' : ''}>
          <span>
            <span class="cc-opcao__titulo">${esc(p.petNome)}</span>
            <span class="cc-opcao__meta">${esc(p.tipo || 'Espécie não informada')}${p.raca ? ' · ' + esc(p.raca) : ''}</span>
          </span>
        </label>`).join('')}
      <label class="cc-opcao">
        <input type="radio" name="sg-pet" value="__novo" ${fluxo.novoPet || !conta.pets.length ? 'checked' : ''}>
        <span>
          <span class="cc-opcao__titulo">+ Cadastrar novo pet</span>
          <span class="cc-opcao__meta">O pet é cadastrado na sua conta e fica disponível para agendamentos também.</span>
        </span>
      </label>
      <div class="cc-etapa__form" id="sg-novo-pet" ${fluxo.novoPet || !conta.pets.length ? '' : 'hidden'}>
        <div class="cc-campo cc-campo--largo"><label for="sg-pet-nome">Nome do pet *</label><input id="sg-pet-nome" type="text" placeholder="Ex.: Thor"></div>
        <div class="cc-campo"><label for="sg-pet-tipo">Espécie *</label><select id="sg-pet-tipo"><option>Cachorro</option><option>Gato</option><option>Outro</option></select></div>
        <div class="cc-campo"><label for="sg-pet-raca">Raça</label><input id="sg-pet-raca" type="text" placeholder="Ex.: SRD, Poodle…"></div>
      </div>`,

    dados: () => `
      <div class="cc-etapa__titulo">Dados do responsável</div>
      <div class="cc-resumo-bloco">
        <div class="cc-resumo-linha"><span>Nome</span><strong>${esc(conta.nome || '—')}</strong></div>
        <div class="cc-resumo-linha"><span>E-mail</span><strong>${esc(conta.email || '—')}</strong></div>
        <div class="cc-resumo-linha"><span>Telefone</span><strong>${esc(conta.telefone || '—')}</strong></div>
        <div class="cc-resumo-linha"><span>Endereço</span><strong>${esc(conta.endereco || 'Não informado')}</strong></div>
      </div>
      <p class="cc-nota">Esses são os dados da sua conta. Para alterá-los, use <a href="#configuracoes" data-ir="configuracoes">Configurações</a>.</p>`,

    pagamento: () => `
      <div class="cc-etapa__titulo">Forma de pagamento</div>
      ${METODOS.map(m => `
        <label class="cc-opcao">
          <input type="radio" name="sg-metodo" value="${esc(m.id)}" ${fluxo.metodo === m.id ? 'checked' : ''}>
          <span>
            <span class="cc-opcao__titulo">${esc(m.id)}</span>
            <span class="cc-opcao__meta">${esc(m.nota)}</span>
          </span>
        </label>`).join('')}
      <div class="cc-etapa__form" id="sg-cartao" ${ehCartao(fluxo.metodo) ? '' : 'hidden'}>
        <div class="cc-campo cc-campo--largo"><label for="sg-c-numero">Número do cartão</label><input id="sg-c-numero" type="text" inputmode="numeric" autocomplete="off" placeholder="0000 0000 0000 0000" maxlength="23"></div>
        <div class="cc-campo cc-campo--largo"><label for="sg-c-nome">Nome no cartão</label><input id="sg-c-nome" type="text" autocomplete="off" placeholder="Como está impresso"></div>
        <div class="cc-campo"><label for="sg-c-validade">Validade</label><input id="sg-c-validade" type="text" inputmode="numeric" autocomplete="off" placeholder="MM/AA" maxlength="5"></div>
        <div class="cc-campo"><label for="sg-c-cvv">CVV</label><input id="sg-c-cvv" type="password" inputmode="numeric" autocomplete="off" placeholder="123" maxlength="4"></div>
        <p class="cc-nota cc-campo--largo">O número completo e o CVV não são enviados nem guardados: o sistema registra apenas os quatro últimos dígitos para você reconhecer o cartão.</p>
      </div>
      <p class="cc-nota">O pagamento é confirmado pela equipe LanePets no atendimento. Até lá ele fica como <strong>Pendente</strong>.</p>`,

    resumo: () => {
      const pet = petEscolhido();
      return `
        <div class="cc-etapa__titulo">Resumo do seguro</div>
        <div class="cc-resumo-bloco">
          <div class="cc-resumo-linha"><span>Plano</span><strong>${esc(fluxo.plano.nome)}</strong></div>
          <div class="cc-resumo-linha"><span>Pet</span><strong>${esc(pet ? pet.nome : '—')}</strong></div>
          <div class="cc-resumo-linha"><span>Responsável</span><strong>${esc(conta.nome || '—')}</strong></div>
          <div class="cc-resumo-linha"><span>Forma de pagamento</span><strong>${esc(fluxo.metodo)}${fluxo.cartaoFinal ? ' **** ' + esc(fluxo.cartaoFinal) : ''}</strong></div>
          <div class="cc-resumo-linha cc-resumo-linha--total"><span>Valor</span><strong>${brl(fluxo.plano.valorMensal)}/mês</strong></div>
        </div>
        <p class="cc-nota">Ao confirmar, o contrato é registrado com pagamento <strong>Pendente</strong> e a equipe LanePets conclui a contratação com você.</p>`;
    }
  };

  function corpoSucesso() {
    const r = fluxo.resultado || {};
    return `
      <div class="cc-resumo-bloco">
        <div class="cc-resumo-linha"><span>Pet</span><strong>${esc(r.nomePet || '')}</strong></div>
        <div class="cc-resumo-linha"><span>Plano</span><strong>${esc(r.nomePlano || fluxo.plano.nome)}</strong></div>
        <div class="cc-resumo-linha cc-resumo-linha--total"><span>Valor</span><strong>${brl(r.valor || fluxo.plano.valorMensal)}/mês</strong></div>
        <div class="cc-resumo-linha"><span>Pagamento</span><strong>${esc(r.metodoPagamento || fluxo.metodo)}</strong></div>
        <div class="cc-resumo-linha"><span>Status do pagamento</span><strong>${esc(r.pagamentoStatus || 'Pendente')}</strong></div>
        <div class="cc-resumo-linha"><span>Status do seguro</span><strong>${esc(r.status || 'Pendente')}</strong></div>
      </div>
      <p class="cc-nota">O status fica como recebido pela equipe enquanto o pagamento não for confirmado. Nada é marcado como pago sem confirmação real.</p>`;
  }

  /* Pet escolhido na etapa 2 — ja cadastrado ou o que sera criado agora. */
  function petEscolhido() {
    if (fluxo.novoPet) return fluxo.petNovoDados ? { nome: fluxo.petNovoDados.nome } : null;
    const p = conta.pets.find(x => x.id === fluxo.petId);
    return p ? { id: p.id, nome: p.petNome } : null;
  }

  /* Liga os campos que so existem dentro da etapa recem-pintada. */
  function ligarCamposDaEtapa(id) {
    if (id === 'pet') {
      document.querySelectorAll('input[name="sg-pet"]').forEach(radio => radio.onchange = () => {
        fluxo.novoPet = radio.value === '__novo';
        if (!fluxo.novoPet) fluxo.petId = radio.value;
        const form = $('#sg-novo-pet');
        if (form) form.hidden = !fluxo.novoPet;
      });
      if (fluxo.petNovoDados && $('#sg-pet-nome')) {
        $('#sg-pet-nome').value = fluxo.petNovoDados.nome || '';
        $('#sg-pet-raca').value = fluxo.petNovoDados.raca || '';
      }
    }
    if (id === 'pagamento') {
      document.querySelectorAll('input[name="sg-metodo"]').forEach(radio => radio.onchange = () => {
        fluxo.metodo = radio.value;
        const bloco = $('#sg-cartao');
        if (bloco) bloco.hidden = !ehCartao(fluxo.metodo);
      });
      const numero = $('#sg-c-numero');
      if (numero) {
        numero.value = fluxo.cartao.numero;
        numero.oninput = () => {
          numero.value = digitos(numero.value).slice(0, 19).replace(/(.{4})/g, '$1 ').trim();
          fluxo.cartao.numero = numero.value;
        };
        $('#sg-c-nome').value = fluxo.cartao.nome;
        $('#sg-c-nome').oninput = e => fluxo.cartao.nome = e.target.value;
        const validade = $('#sg-c-validade');
        validade.value = fluxo.cartao.validade;
        validade.oninput = () => {
          const d = digitos(validade.value).slice(0, 4);
          validade.value = d.length > 2 ? d.slice(0, 2) + '/' + d.slice(2) : d;
          fluxo.cartao.validade = validade.value;
        };
        const cvv = $('#sg-c-cvv');
        cvv.value = fluxo.cartao.cvv;
        cvv.oninput = () => { cvv.value = digitos(cvv.value).slice(0, 4); fluxo.cartao.cvv = cvv.value; };
      }
    }
  }

  /* Validacao da etapa atual. Devolve mensagem de erro ou string vazia. */
  async function validarEtapa(id) {
    if (id === 'pet') {
      if (fluxo.novoPet) {
        const nome = ($('#sg-pet-nome')?.value || '').trim();
        if (nome.length < 2) return 'Informe o nome do pet para continuar.';
        fluxo.petNovoDados = { nome, tipo: $('#sg-pet-tipo').value, raca: ($('#sg-pet-raca').value || '').trim() };
        return '';
      }
      if (!fluxo.petId) return 'Selecione um pet para continuar.';
      if (!conta.pets.some(p => p.id === fluxo.petId)) return 'Selecione um pet para continuar.';
      return '';
    }
    if (id === 'pagamento') {
      if (!fluxo.metodo) return 'Escolha uma forma de pagamento para continuar.';
      if (ehCartao(fluxo.metodo)) {
        const numero = digitos(fluxo.cartao.numero);
        if (numero.length < 13 || numero.length > 19) return 'Confira o número do cartão.';
        if ((fluxo.cartao.nome || '').trim().length < 3) return 'Informe o nome impresso no cartão.';
        const validade = digitos(fluxo.cartao.validade);
        const mes = Number(validade.slice(0, 2));
        if (validade.length !== 4 || mes < 1 || mes > 12) return 'Informe a validade no formato MM/AA.';
        if (digitos(fluxo.cartao.cvv).length < 3) return 'Informe o CVV do cartão.';
        /* Somente os quatro ultimos digitos seguem adiante. O numero completo
           e o CVV ficam nesta tela e nunca sao enviados. */
        fluxo.cartaoFinal = numero.slice(-4);
      } else {
        fluxo.cartaoFinal = '';
      }
      return '';
    }
    return '';
  }

  $('#sg-voltar').onclick = () => {
    if (!fluxo || fluxo.concluido) return;
    fluxo.etapa = Math.max(0, fluxo.etapa - 1);
    pintarEtapa();
  };

  /* Este botao NAO usa comBotao: comBotao devolve o rotulo original no fim, e
     aqui o rotulo muda com a etapa ("Continuar" -> "Confirmar contratacao" ->
     "Fechar"). O estado de carregando e feito na mao para o rotulo da proxima
     etapa sobreviver. */
  $('#sg-avancar').onclick = async () => {
    const botao = $('#sg-avancar');
    if (botao.disabled) return;
    const rotuloAnterior = botao.textContent;
    botao.disabled = true;
    botao.textContent = 'Aguarde…';
    try { await avancarEtapa(); }
    finally {
      botao.disabled = false;
      if (botao.textContent === 'Aguarde…') botao.textContent = rotuloAnterior;
    }
  };

  async function avancarEtapa() {
    if (!fluxo) return;
    if (fluxo.concluido) { $('#modal-seguro').close(); fluxo = null; return; }

    const etapa = ETAPAS[fluxo.etapa];
    const erro = await validarEtapa(etapa.id);
    if (erro) { aviso('#sg-msg', erro, true); return; }

    /* O pet novo e criado ao SAIR da etapa do pet: assim ele ja existe no
       banco e vinculado a esta conta antes do resumo e da contratacao. */
    if (etapa.id === 'pet' && fluxo.novoPet) {
      try {
        const criado = await api('pets', { method: 'POST', body: JSON.stringify(fluxo.petNovoDados) });
        conta = await api('conta');
        fluxo.petId = criado.id;
        fluxo.novoPet = false;
        fluxo.petNovoDados = null;
        renderPets();
      } catch (e) { aviso('#sg-msg', e.message, true); return; }
    }

    if (etapa.id === 'resumo') { await confirmarContratacao(); return; }

    fluxo.etapa = Math.min(ETAPAS.length - 1, fluxo.etapa + 1);
    pintarEtapa();
  }

  async function confirmarContratacao() {
    try {
      const resultado = await api('seguros', {
        method: 'POST',
        body: JSON.stringify({
          planoId: fluxo.plano.id,
          petId: fluxo.petId,
          metodoPagamento: fluxo.metodo,
          cartaoFinal: fluxo.cartaoFinal || ''
        })
      });
      fluxo.resultado = resultado;
      fluxo.concluido = true;
      pintarEtapa();
      toast(resultado.message || 'Seguro contratado.');
      /* A tela volta a ler do servidor: o que vale e o que ficou no banco. */
      await renderSeguro();
      renderPagamentos();
    } catch (e) {
      aviso('#sg-msg', e.message, true);
    }
  }

  /* ---------------------------------------------------------------------
     Cancelamento — confirmacao antes de qualquer chamada a API
     --------------------------------------------------------------------- */
  let seguroParaCancelar = null;

  function abrirCancelamento(id) {
    const seguro = segurosContratados.find(s => s.id === id);
    if (!seguro) return;
    if (!seguro.podeCancelar) { toast('Este seguro já está cancelado.'); return; }
    seguroParaCancelar = seguro;
    $('#sgc-corpo').innerHTML = `
      <div class="cc-resumo-linha"><span>Pet</span><strong>${esc(seguro.nomePet || '—')}</strong></div>
      <div class="cc-resumo-linha"><span>Plano</span><strong>${esc(seguro.nomePlano)}</strong></div>
      <div class="cc-resumo-linha"><span>Contratado em</span><strong>${dataCurta(seguro.criadoEm)}</strong></div>
      <div class="cc-resumo-linha cc-resumo-linha--total"><span>Valor</span><strong>${brl(seguro.valor || seguro.valorMensal)}/mês</strong></div>`;
    aviso('#sgc-msg', '');
    $('#modal-seguro-cancelar').showModal();
  }

  $('#sgc-confirmar').onclick = event => comBotao(event.currentTarget, 'Cancelando…', async () => {
    if (!seguroParaCancelar) return;
    aviso('#sgc-msg', '');
    try {
      await api(`seguros/${encodeURIComponent(seguroParaCancelar.id)}/cancelar`, { method: 'POST' });
      $('#modal-seguro-cancelar').close();
      seguroParaCancelar = null;
      await renderSeguro();
      renderPagamentos();
      toast('Seguro cancelado.');
    } catch (e) {
      aviso('#sgc-msg', e.message, true);
    }
  });

  async function renderAvaliacoes() {
    const caixa = $('#cc-avaliacoes');
    caixa.innerHTML = '<div class="cc-esqueleto" style="height:60px"></div>';
    try {
      const lista = await api('avaliacoes');
      caixa.innerHTML = lista.length
        ? '<div class="cc-lista">' + lista.map(a => linha({
            icone: ICO.balao,
            titulo: `<span style="color:var(--laranja);letter-spacing:2px">${'★'.repeat(a.avaliacao)}${'☆'.repeat(5 - a.avaliacao)}</span>`,
            meta: `“${esc(a.comentario)}”<br>Enviada em ${dataCurta(a.criadoEm)}${a.nomePet ? ' · ' + esc(a.nomePet) : ''}`,
            fim: selo(a.status)
          })).join('') + '</div>'
        : vazio('Você ainda não avaliou', 'Conte como foi a experiência do seu pet — o comentário vai para a moderação da equipe.');
    } catch (erro) {
      caixa.innerHTML = vazio('Não foi possível carregar', erro.message);
    }
  }
  /* ---------------------------------------------------------------------
     Entrar
     --------------------------------------------------------------------- */
  formLogin.addEventListener('submit', async event => {
    event.preventDefault();
    UI.limpar(formLogin);
    UI.alerta('#login-aviso', '');
    UI.alerta('#login-info', '');

    const campoEmail = $('#login-email'), campoSenha = $('#login-senha');
    const email = campoEmail.value.trim();
    const senha = campoSenha.value;

    let valido = true;
    if (!email) valido = UI.erro(campoEmail, 'Informe seu e-mail.');
    else if (!UI.EMAIL_RE.test(email)) valido = UI.erro(campoEmail, 'Digite um e-mail válido, como nome@email.com.');
    if (!senha) valido = UI.erro(campoSenha, 'Informe sua senha.');
    if (!valido) { UI.alerta('#login-aviso', 'Revise os campos destacados para continuar.'); return; }

    UI.carregando('#login', true, 'Entrando…');
    let credenciaisAceitas = false;
    try {
      const d = await api('login', { method: 'POST', body: JSON.stringify({ email, senha }) });
      token = d.token;
      localStorage.setItem('lanePetsClienteToken', token);
      credenciaisAceitas = true;      /* o servidor validou e-mail e senha */
      await abrir();
    } catch (erro) {
      console.error('[LanePets] Falha ao entrar na área do cliente:', erro);
      if (credenciaisAceitas) {
        /* A senha estava certa — o que falhou foi carregar a conta.
           Marcar o campo de senha aqui só confundiria o cliente. */
        UI.alerta('#login-aviso', 'Sua senha está correta, mas não foi possível carregar a conta: ' + (erro.message || 'erro desconhecido.'));
      } else {
        UI.alerta('#login-aviso', erro.message || 'Não foi possível entrar.');
        UI.erro(campoSenha, 'Verifique seus dados e tente novamente.');
        campoSenha.value = '';
        campoSenha.focus();
      }
    } finally {
      UI.carregando('#login', false);
    }
  });

  /* ---------------------------------------------------------------------
     Criar conta
     --------------------------------------------------------------------- */
  formCadastro.addEventListener('submit', async event => {
    event.preventDefault();
    UI.limpar(formCadastro);
    UI.alerta('#cad-aviso', '');

    const cNome = $('#cad-nome'), cEmail = $('#cad-email'), cTel = $('#cad-tel');
    const cSenha = $('#cad-senha'), cSenha2 = $('#cad-senha2'), cPet = $('#cad-pet'), cTipo = $('#cad-tipo');

    const nome = cNome.value.trim(), email = cEmail.value.trim(), telefone = cTel.value.trim();
    const senha = cSenha.value, senha2 = cSenha2.value;
    const pet = cPet.value.trim(), tipo = cTipo.value.trim();

    let valido = true;
    if (nome.length < 3) valido = UI.erro(cNome, 'Informe seu nome completo (mínimo 3 letras).');
    if (!email) valido = UI.erro(cEmail, 'Informe seu e-mail.');
    else if (!UI.EMAIL_RE.test(email)) valido = UI.erro(cEmail, 'Digite um e-mail válido, como nome@email.com.');
    if (telefone.replace(/\D/g, '').length < 10) valido = UI.erro(cTel, 'Informe um telefone com DDD.');
    if (senha.length < 8) valido = UI.erro(cSenha, 'A senha precisa ter ao menos 8 caracteres.');
    else if (!senhaValida(senha)) valido = UI.erro(cSenha, 'A senha precisa ter pelo menos uma letra e um número.');
    if (!senha2) valido = UI.erro(cSenha2, 'Repita a senha para confirmar.');
    else if (senha !== senha2) valido = UI.erro(cSenha2, 'As senhas não são iguais.');
    else if (senhaValida(senha)) UI.ok(cSenha2);
    if (pet.length < 2) valido = UI.erro(cPet, 'Informe o nome do seu pet.');
    if (!valido) { UI.alerta('#cad-aviso', 'Revise os campos destacados para criar sua conta.'); return; }

    UI.carregando('#cadastro', true, 'Criando conta…');
    try {
      await api('cadastro', { method: 'POST', body: JSON.stringify({ nome, email, senha, telefone, pet, tipo }) });
      /* Conta criada — o cliente segue para o login, conforme o fluxo definido. */
      formCadastro.reset();
      UI.limpar(formCadastro);
      $('#forca-senha').dataset.nivel = '0';
      $('#forca-texto').textContent = NIVEIS[0];
      mostrarPainel('sucesso');
      $('#login-email').value = email;
    } catch (e) {
      UI.alerta('#cad-aviso', e.message || 'Não foi possível criar a conta.');
    } finally {
      UI.carregando('#cadastro', false);
    }
  });


  /* ---------------------------------------------------------------------
     Carregamento e exibição do portal
     --------------------------------------------------------------------- */
  function exibirPortal(ativo) {
    const auth = $('#auth'), portal = $('#portal');
    if (auth) auth.style.display = ativo ? 'none' : 'block';
    if (portal) portal.style.display = ativo ? 'grid' : 'none';
    document.body.classList.toggle('lp-auth', !ativo);
    document.body.classList.toggle('lp-auth--cliente', !ativo);
    if (!ativo) fecharMenu();
  }

  async function abrir() {
    try {
      conta = await api('conta');
      exibirPortal(true);

      renderPerfil();
      renderResumo();
      renderProximo();
      renderPets();
      renderVisaoPets();
      renderAgendamentos();
      renderHistorico();
      renderPedidos();
      renderPedidosRecentes();
      renderPagamentos();

      catalogo = await api('catalogo');
      renderProdutosDestaque();
      options($('#av-pet'), conta.pets, p => p.petNome);
      montarVitrinePedido();
      /* Os cards de agendamento mostram o endereco da unidade, que so existe
         no catalogo. Com ele em maos a lista e repintada — a primeira pintura
         acima ja deixou a tela util antes da segunda chamada terminar. */
      renderAgendamentos();
      renderHistorico();

      irPara(rotaDoHash(), false);
      renderAvaliacoes();
      renderSeguro();
    } catch (erro) {
      /* ---------------------------------------------------------------
         Antes este catch engolia a falha em silêncio: o cliente era
         deslogado e voltava para a tela de entrada sem nenhuma mensagem,
         ou via um TypeError que substituía a causa real.
         Agora a causa sempre aparece — no aviso da tela e no console.
         --------------------------------------------------------------- */
      console.error('[LanePets] Falha ao abrir a área do cliente:', erro);
      exibirPortal(false);

      /* 401 = a sessão do cliente não vale mais (token expirado ou o
         servidor foi reiniciado, já que as sessões vivem em memória).
         Só nesse caso faz sentido descartar o token guardado. */
      if (erro && (erro.status === 401 || erro.semConexao)) {
        localStorage.removeItem('lanePetsClienteToken');
        token = '';
      }
      throw erro;   /* quem chamou decide como mostrar */
    }
  }

  /* Mensagens de formulário --------------------------------------------- */
  function aviso(seletor, texto, erro) {
    const el = $(seletor);
    el.textContent = texto;
    if (erro) el.dataset.erro = '1'; else delete el.dataset.erro;
  }
  /* Confirmacao curta e discreta depois de uma acao bem sucedida. */
  let toastTimer = null;
  function toast(mensagem) {
    let el = document.getElementById('cc-toast');
    if (!el) {
      el = document.createElement('div');
      el.id = 'cc-toast';
      el.className = 'cc-toast';
      el.setAttribute('role', 'status');
      document.body.appendChild(el);
    }
    el.textContent = mensagem;
    el.classList.add('is-ativo');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => el.classList.remove('is-ativo'), 3200);
  }

  async function comBotao(botao, textoCarregando, acao) {
    const original = botao.textContent;
    botao.disabled = true;
    botao.textContent = textoCarregando;
    try { await acao(); }
    finally { botao.disabled = false; botao.textContent = original; }
  }

  /* ---------------------------------------------------------------------
     Ações do portal
     --------------------------------------------------------------------- */
  /* O formulário em linha de "novo agendamento" saiu: a criação agora passa
     pelo fluxo em etapas (abrirFluxoAgendamento), que chama o MESMO
     POST /api/cliente/agendamentos na confirmação. */

  /* =======================================================================
     VITRINE DO NOVO PEDIDO

     Os produtos continuam vindo de GET /api/cliente/catalogo — a mesma rota,
     o mesmo registro que o painel administrativo edita. O que mudou e como o
     cliente escolhe: em vez de abrir um <select> e ler texto, ele ve card,
     preco e estoque lado a lado.

     O <select id="produto"> nao saiu: a vitrine escreve nele e o envio
     continua lendo dele. Nenhuma linha do POST /api/cliente/pedidos mudou.
     ======================================================================= */

  let vitrineCategoria = '';
  let vitrineBusca = '';
  let vitrineOrdem = 'nome';

  const ICO_PRODUTO = {
    'Brinquedo': '<circle cx="12" cy="12" r="8.4"/><path d="M6.1 6.4c2.9 1.5 4.4 4.3 4.4 8.6"/><path d="M17.9 6.4c-2.9 1.5-4.4 4.3-4.4 8.6"/>',
    'Acessório': '<circle cx="12" cy="10.5" r="6.2"/><path d="M12 16.7v2.4"/><circle cx="12" cy="20.6" r="1.3"/>',
    'Roupa':     '<path d="M8.5 3.5 12 6l3.5-2.5 4.5 3-2.5 4-2-1v11h-7v-11l-2 1-2.5-4Z"/>',
    'Ração':     '<path d="M6 8.5h12l-1.2 11a1.8 1.8 0 0 1-1.8 1.6H9a1.8 1.8 0 0 1-1.8-1.6Z"/><path d="M9 8.5V6a3 3 0 0 1 6 0v2.5"/><path d="M10 13h4"/>',
    'Higiene':   '<path d="M9 8.5h6v11a1.8 1.8 0 0 1-1.8 1.8h-2.4A1.8 1.8 0 0 1 9 19.5Z"/><path d="M10.5 8.5V5.5h3v3"/><path d="M11 3h2"/>'
  };
  const ICO_PADRAO = '<path d="m3 8 9-5 9 5-9 5-9-5Z"/><path d="M3 8v8l9 5 9-5V8"/><path d="M12 13v8"/>';
  const svgP = miolo => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${miolo}</svg>`;

  /** Estado do estoque a partir dos campos reais do produto. Nada e estimado. */
  function estoqueDoProduto(produto) {
    if (!produto.controlaEstoque) return { chave: 'ok', texto: 'Disponível', fora: false };
    const saldo = Number(produto.estoque) || 0;
    if (saldo <= 0) return { chave: 'fora', texto: 'Indisponível', fora: true };
    if (saldo <= 3) return { chave: 'pouco', texto: `Últimas ${saldo}`, fora: false };
    return { chave: 'ok', texto: `${saldo} em estoque`, fora: false };
  }

  const produtosDoCatalogo = () => (catalogo && catalogo.produtos) || [];

  /** Limita a quantidade ao saldo real e explica o limite embaixo do campo. */
  function ajustarQuantidade() {
    const escolhido = produtosDoCatalogo().find(p => String(p.id) === $('#produto').value);
    const campo = $('#quantidade');
    const ajuda = $('#pedidoEstoqueAjuda');
    if (!escolhido) { campo.max = 99; ajuda.textContent = ''; return; }

    if (escolhido.controlaEstoque) {
      const saldo = Math.max(0, Number(escolhido.estoque) || 0);
      campo.max = saldo || 1;
      if (Number(campo.value) > saldo) campo.value = saldo || 1;
      ajuda.textContent = saldo ? `Disponível: ${saldo} unidade${saldo === 1 ? '' : 's'}.` : 'Sem estoque no momento.';
    } else {
      campo.max = 99;
      ajuda.textContent = '';
    }
  }

  function escolherProduto(id) {
    $('#produto').value = String(id);
    $('#pedidoVitrine').querySelectorAll('.pedido-card').forEach(card =>
      card.classList.toggle('escolhido', card.dataset.id === String(id)));
    ajustarQuantidade();
  }

  function cardPedido(produto) {
    const estoque = estoqueDoProduto(produto);
    const selecionado = $('#produto').value === String(produto.id);
    return `<button type="button" class="card loja-card pedido-card${selecionado ? ' escolhido' : ''}"
                    data-id="${esc(produto.id)}" ${estoque.fora ? 'disabled' : ''}
                    aria-pressed="${selecionado}">
      <div class="loja-media">
        <span class="loja-selo loja-selo--${estoque.chave}">${estoque.texto}</span>
        <span class="pedido-card__marca">${svgP('<path d="m5 12.5 4.5 4.5L19 7.5"/>')}</span>
        ${produto.fotoUrl && /^data:image\//.test(produto.fotoUrl)
          ? `<img src="${esc(produto.fotoUrl)}" alt="" loading="lazy">`
          : `<span class="loja-simbolo">${svgP(ICO_PRODUTO[produto.categoria] || ICO_PADRAO)}</span>`}
      </div>
      <div class="loja-corpo">
        <span class="tag">${esc(produto.categoria || 'LanePets')}</span>
        <h3>${esc(produto.nome)}</h3>
        ${produto.descricao ? `<p class="loja-desc">${esc(produto.descricao)}</p>` : ''}
        <div class="loja-preco"><strong>${brl(produto.valorVenda)}</strong></div>
      </div>
    </button>`;
  }

  function montarCategoriasPedido() {
    const caixa = $('#pedidoCategorias');
    if (!caixa) return;
    const categorias = [...new Set(produtosDoCatalogo().map(p => p.categoria).filter(Boolean))]
      .sort((a, b) => a.localeCompare(b, 'pt-BR'));
    caixa.innerHTML = [['', 'Todos'], ...categorias.map(c => [c, c])]
      .map(([valor, rotulo]) => `<button type="button" class="loja-chip" data-categoria="${esc(valor)}"
             aria-pressed="${valor === vitrineCategoria}">${esc(rotulo)}</button>`).join('');
  }

  function desenharVitrine() {
    const alvo = $('#pedidoVitrine');
    if (!alvo) return;
    const termo = vitrineBusca.trim().toLowerCase();

    let lista = produtosDoCatalogo().filter(produto => {
      if (vitrineCategoria && produto.categoria !== vitrineCategoria) return false;
      if (termo && !(produto.nome + ' ' + (produto.categoria || '')).toLowerCase().includes(termo)) return false;
      return true;
    });

    lista = lista.slice().sort((a, b) => {
      if (vitrineOrdem === 'menor') return Number(a.valorVenda) - Number(b.valorVenda);
      if (vitrineOrdem === 'maior') return Number(b.valorVenda) - Number(a.valorVenda);
      return String(a.nome).localeCompare(String(b.nome), 'pt-BR');
    });

    if (!lista.length) {
      alvo.innerHTML = `<div class="loja-estado pedido-vazio-alerta">
        <h3>Nenhum produto encontrado</h3>
        <p>Não encontramos produtos para a busca ou o filtro selecionado.</p>
        <button class="botao claro" type="button" id="pedidoLimparTudo">Limpar filtros</button>
      </div>`;
      const limpar = $('#pedidoLimparTudo');
      if (limpar) limpar.onclick = () => {
        vitrineBusca = ''; vitrineCategoria = '';
        const campo = $('#pedidoBusca');
        if (campo) { campo.value = ''; campo.parentElement.classList.remove('tem-texto'); }
        montarCategoriasPedido();
        desenharVitrine();
      };
      return;
    }

    alvo.innerHTML = lista.map(cardPedido).join('');
  }

  /**
   * Preenche o <select> (como sempre fez) e monta a vitrine em cima dele.
   * Chamada depois que o catalogo chega.
   */
  function montarVitrinePedido() {
    /* Estoque unico: o rotulo mostra o saldo real do banco, o mesmo numero
       que o painel administrativo exibe. */
    options($('#produto'), produtosDoCatalogo(), p => `${p.nome} — ${brl(p.valorVenda)}`
      + (p.controlaEstoque ? (p.estoque > 0 ? ` (${p.estoque} em estoque)` : ' (sem estoque)') : ''));

    /* A escolha inicial cai no primeiro card COMO ELE APARECE na vitrine (ordem
       alfabetica), pulando o que estiver sem estoque. Sem isto, o card marcado
       poderia estar la embaixo, fora da vista, e o cliente confirmaria um
       pedido de um produto que nem viu. */
    const primeiroDisponivel = produtosDoCatalogo()
      .filter(p => !estoqueDoProduto(p).fora)
      .sort((a, b) => String(a.nome).localeCompare(String(b.nome), 'pt-BR'))[0];
    if (primeiroDisponivel) $('#produto').value = String(primeiroDisponivel.id);

    montarCategoriasPedido();
    desenharVitrine();
    ajustarQuantidade();
    montarRetirada();
  }

  /* Item 5: o cliente escolhe onde retira o pedido. Com uma unidade ativa so,
     o campo some e o servidor escolhe sozinho. */
  function montarRetirada() {
    const sel = $('#pedido-unidade');
    if (!sel) return;
    const unidades = (catalogo && catalogo.unidades) || [];
    const atual = sel.value;
    sel.innerHTML = (unidades.length > 1 ? '<option value="">Escolha a unidade</option>' : '')
      + unidades.map(u => `<option value="${esc(u.id)}">${esc(u.nome)}${u.endereco ? ' — ' + esc(u.endereco) : ''}</option>`).join('');
    if (atual && unidades.some(u => u.id === atual)) sel.value = atual;
    sel.closest('.cc-campo').hidden = unidades.length < 2;
  }

  function ligarVitrinePedido() {
    const campo = $('#pedidoBusca');
    if (campo) {
      campo.oninput = () => {
        vitrineBusca = campo.value;
        campo.parentElement.classList.toggle('tem-texto', campo.value.length > 0);
        desenharVitrine();
      };
    }
    const limparBusca = $('#pedidoLimparBusca');
    if (limparBusca) limparBusca.onclick = () => {
      vitrineBusca = ''; campo.value = '';
      campo.parentElement.classList.remove('tem-texto');
      campo.focus(); desenharVitrine();
    };

    const ordem = $('#pedidoOrdem');
    if (ordem) ordem.onchange = () => { vitrineOrdem = ordem.value; desenharVitrine(); };

    const chips = $('#pedidoCategorias');
    if (chips) chips.onclick = evento => {
      const chip = evento.target.closest('.loja-chip');
      if (!chip) return;
      vitrineCategoria = chip.dataset.categoria || '';
      chips.querySelectorAll('.loja-chip').forEach(b => b.setAttribute('aria-pressed', String(b === chip)));
      desenharVitrine();
    };

    const vitrine = $('#pedidoVitrine');
    if (vitrine) vitrine.onclick = evento => {
      const card = evento.target.closest('.pedido-card');
      if (!card || card.disabled) return;
      escolherProduto(card.dataset.id);
      vitrine.querySelectorAll('.pedido-card').forEach(c =>
        c.setAttribute('aria-pressed', String(c === card)));
    };

    /* Quem usa o select por teclado continua mandando na escolha. */
    const select = $('#produto');
    if (select) select.onchange = () => escolherProduto(select.value);
  }

  ligarVitrinePedido();

  /* "Ver detalhes" em qualquer card de pedido. Delegado porque a lista e
     redesenhada a cada atualizacao da conta. */
  $('#lista-pedidos').addEventListener('click', async evento => {
    /* Itens 6/7: cancelar pedido ainda Pendente. O estoque volta no servidor. */
    const cancelar = evento.target.closest('[data-pedido-cancelar]');
    if (cancelar) {
      if (!window.confirm('Cancelar este pedido? Esta ação não pode ser desfeita.')) return;
      const texto = cancelar.textContent;
      cancelar.disabled = true; cancelar.textContent = 'Cancelando…';
      try {
        await api(`pedidos/${encodeURIComponent(cancelar.dataset.pedidoCancelar)}/cancelar`, { method: 'POST' });
        await abrir();
        irPara('meus-pedidos');
        toast('Pedido cancelado.');
      } catch (erro) {
        console.error('[LanePets] cancelar pedido:', erro);
        toast(erro.message);
        cancelar.disabled = false; cancelar.textContent = texto;
      }
      return;
    }
    const botao = evento.target.closest('[data-pedido]');
    if (botao) abrirPedido(Number(botao.dataset.pedido));
  });

  $('#criar-pedido').onclick = event => comBotao(event.currentTarget, 'Enviando…', async () => {
    aviso('#pedido-msg', '');
    const retirada = $('#pedido-unidade') ? $('#pedido-unidade').value : '';
    if (((catalogo && catalogo.unidades) || []).length > 1 && !retirada) {
      aviso('#pedido-msg', 'Escolha a unidade onde você vai retirar o pedido.', true);
      return;
    }
    try {
      await api('pedidos', { method: 'POST', body: JSON.stringify({
        produtoId: $('#produto').value, quantidade: Number($('#quantidade').value), formaPagamento: $('#pedido-pagamento').value,
        unidade: retirada || null
      }) });
      aviso('#pedido-msg', 'Pedido recebido!');
      await abrir();
      irPara('meus-pedidos');
    } catch (e) { aviso('#pedido-msg', e.message, true); }
  });

  /* =====================================================================
     HISTORICO DE SERVICOS (item 2 do roadmap, 24/09)

     Visao propria dos atendimentos ja realizados. Usa os mesmos
     agendamentos que GET /api/cliente/conta devolveu — nada novo e buscado.
     "Realizado" = status Concluído (item 4; antes Pronto/Entregue, que o
     servidor converteu). Solicitado, Confirmado, Em andamento e Cancelado nao entram.
     ===================================================================== */
  const ehRealizado = a => statusAg(a) === 'Concluído';
  let histPet = '';

  /* O painel grava a unidade como id ("franco"), a area do cliente como nome
     ("Franco da Rocha"). O rotulo usa o catalogo para mostrar sempre o nome. */
  function rotuloUnidade(valor) {
    const v = String(valor || '').trim();
    if (!v) return 'Unidade não informada';
    const u = ((catalogo && catalogo.unidades) || []).find(x => x.id === v.toLowerCase() || x.nome === v);
    return u ? u.nome : v;
  }

  function renderHistorico() {
    const lista = $('#hist-lista');
    if (!lista || !conta) return;

    const todos = (conta.agendamentos || []).filter(ehRealizado)
      .slice().sort((a, b) => quandoMs(b) - quandoMs(a));

    /* Filtro por pet: so os pets que tem atendimento aparecem. */
    const sel = $('#hist-pet');
    const petsComHistorico = (conta.pets || []).filter(p => todos.some(a => a.petId === p.id));
    if (histPet && !petsComHistorico.some(p => p.id === histPet)) histPet = '';
    sel.innerHTML = '<option value="">Todos os pets</option>' +
      petsComHistorico.map(p => `<option value="${esc(p.id)}"${p.id === histPet ? ' selected' : ''}>${esc(p.petNome)}</option>`).join('');

    const itens = todos.filter(a => !histPet || a.petId === histPet);

    const total = itens.reduce((s, a) => s + Number(a.total || 0), 0);
    const ultimo = itens[0];
    const porPet = {};
    itens.forEach(a => { porPet[a.pet || '—'] = (porPet[a.pet || '—'] || 0) + 1; });
    const [petTop, qtdTop] = Object.entries(porPet).sort((a, b) => b[1] - a[1])[0] || ['—', 0];

    $('#hist-resumo').innerHTML = [
      metrica({ icone: ICO.agenda, rotulo: 'Serviços realizados', valor: String(itens.length), nota: histPet ? 'deste pet' : 'em todos os pets' }),
      metrica({ icone: ICO.relogio, rotulo: 'Último atendimento', valor: ultimo ? dataCurta(ultimo.dataHora) : '—', nota: ultimo ? esc(servicosDo(ultimo)) : 'Nenhum ainda' }),
      metrica({ icone: ICO.cartao, rotulo: 'Total dos serviços', valor: brl(total), nota: 'valor registrado nos atendimentos' }),
      metrica({ icone: ICO.pata, rotulo: 'Pet mais atendido', valor: esc(petTop), nota: qtdTop ? `${qtdTop} atendimento(s)` : '—', acento: true })
    ].join('');

    if (!itens.length) {
      lista.innerHTML = vazio('Nenhum serviço realizado ainda',
        'Quando a equipe concluir um atendimento, ele aparece aqui com a data, o serviço e o valor.',
        '<button class="botao primario" type="button" data-ir="agendar">Agendar um serviço</button>');
      return;
    }

    lista.innerHTML = `<div class="cc-lista">${itens.map(a => linha({
      icone: iconePet((conta.pets || []).find(p => p.id === a.petId)?.tipo),
      titulo: `${esc(a.pet)} · ${esc(servicosDo(a))}`,
      meta: `${dataHora(a.dataHora)} · ${esc(rotuloUnidade(a.unidade))}${a.transporte && a.transporte !== 'Cliente leva' ? ' · ' + esc(a.transporte) : ''}`,
      fim: `<strong>${brl(a.total)}</strong>${selo(a.status)}`
    })).join('')}</div>`;
  }

  document.addEventListener('change', event => {
    if (event.target && event.target.id === 'hist-pet') { histPet = event.target.value; renderHistorico(); }
  });

  /* Alterar e-mail / senha (item 2 do roadmap) ---------------------------
     As duas trocas pedem a senha atual; quem decide e o backend
     (PUT /api/cliente/conta/email e /conta/senha). */
  $('#cc-alterar-email').onclick = () => {
    $('#em-novo').value = '';
    $('#em-senha').value = '';
    aviso('#em-msg', '');
    $('#modal-email').showModal();
    $('#em-novo').focus();
  };
  $('#em-salvar').onclick = event => comBotao(event.currentTarget, 'Salvando…', async () => {
    aviso('#em-msg', '');
    const novoEmail = $('#em-novo').value.trim();
    const senhaAtual = $('#em-senha').value;
    if (!UI.EMAIL_RE.test(novoEmail)) return aviso('#em-msg', 'Informe um e-mail válido, como nome@exemplo.com.', true);
    if (!senhaAtual) return aviso('#em-msg', 'Informe a sua senha atual.', true);
    try {
      const r = await api('conta/email', { method: 'PUT', body: JSON.stringify({ novoEmail, senhaAtual }) });
      $('#modal-email').close();
      await abrir();
      toast('\u2713 ' + ((r && r.message) || 'E-mail atualizado.'));
    } catch (e) {
      console.error('[LanePets] Falha ao alterar o e-mail:', e);
      aviso('#em-msg', e.status === 401 ? 'Sua sessão expirou. Entre novamente.' : e.message, true);
    }
  });

  $('#cc-alterar-senha').onclick = () => {
    ['#sn-atual', '#sn-nova', '#sn-confirma'].forEach(id => { $(id).value = ''; });
    aviso('#sn-msg', '');
    $('#modal-senha').showModal();
    $('#sn-atual').focus();
  };
  $('#sn-salvar').onclick = event => comBotao(event.currentTarget, 'Salvando…', async () => {
    aviso('#sn-msg', '');
    const senhaAtual = $('#sn-atual').value;
    const novaSenha = $('#sn-nova').value;
    const confirmarSenha = $('#sn-confirma').value;
    if (!senhaAtual) return aviso('#sn-msg', 'Informe a sua senha atual.', true);
    if (novaSenha.length < 8) return aviso('#sn-msg', 'A nova senha precisa ter ao menos 8 caracteres.', true);
    if (!senhaValida(novaSenha)) return aviso('#sn-msg', 'A nova senha precisa ter pelo menos uma letra e um número.', true);
    if (novaSenha !== confirmarSenha) return aviso('#sn-msg', 'A confirmação da nova senha não confere.', true);
    try {
      const r = await api('conta/senha', { method: 'PUT', body: JSON.stringify({ senhaAtual, novaSenha, confirmarSenha }) });
      $('#modal-senha').close();
      toast('\u2713 ' + ((r && r.message) || 'Senha alterada.'));
    } catch (e) {
      console.error('[LanePets] Falha ao alterar a senha:', e);
      aviso('#sn-msg', e.status === 401 ? 'Sua sessão expirou. Entre novamente.' : e.message, true);
    }
  });

  /* Editar perfil -------------------------------------------------------- */
  function abrirPerfil() {
    $('#pf-nome').value = conta.nome || '';
    $('#pf-telefone').value = conta.telefone || '';
    $('#pf-endereco').value = conta.endereco || '';
    aviso('#pf-msg', '');
    $('#modal-perfil').showModal();
    $('#pf-nome').focus();
  }
  $('#cc-editar-perfil-2').onclick = abrirPerfil;
  UI.mascaraTelefone($('#pf-telefone'));

  $('#pf-salvar').onclick = event => comBotao(event.currentTarget, 'Salvando…', async () => {
    aviso('#pf-msg', '');
    const nome = $('#pf-nome').value.trim();
    const telefone = $('#pf-telefone').value.trim();
    if (nome.length < 3) return aviso('#pf-msg', 'Informe seu nome completo (mínimo 3 letras).', true);
    if (telefone.replace(/\D/g, '').length < 10) return aviso('#pf-msg', 'Informe um telefone com DDD.', true);
    try {
      /* O backend identifica o cliente pelo token da sessao (header
         X-LanePets-Client); nenhum id de cliente viaja no corpo. */
      const atualizado = await api('conta', { method: 'PUT', body: JSON.stringify({ nome, telefone, endereco: $('#pf-endereco').value.trim() }) });
      $('#modal-perfil').close();
      await abrir();                                   /* recarrega a conta e repinta a tela */
      toast(atualizado && atualizado.message ? '\u2713 ' + atualizado.message : '\u2713 Dados atualizados com sucesso!');
    } catch (e) {
      console.error('[LanePets] Falha ao atualizar o perfil:', e);
      aviso('#pf-msg', e.status === 401 ? 'Sua sess\u00e3o expirou. Entre novamente para salvar seus dados.' : 'N\u00e3o foi poss\u00edvel atualizar seus dados. ' + e.message, true);
    }
  });

  /* Adicionar pet -------------------------------------------------------- */
  document.addEventListener('click', event => {
    const botaoCancelar = event.target.closest('[data-cancelar]');
    if (botaoCancelar) { cancelarAgendamento(botaoCancelar.dataset.cancelar); return; }
    const botaoContratar = event.target.closest('[data-contratar]');
    if (botaoContratar) { abrirContratacao(botaoContratar.dataset.contratar); return; }
    const botaoCancelarSeguro = event.target.closest('[data-cancelar-seguro]');
    if (botaoCancelarSeguro) { abrirCancelamento(botaoCancelarSeguro.dataset.cancelarSeguro); return; }
    const ir = event.target.closest('[data-ir]');
    if (ir) irPara(ir.dataset.ir);
  });

  /* Avaliação ------------------------------------------------------------ */
  function pintarEstrelas(nota) {
    document.querySelectorAll('#av-estrelas button').forEach(b => b.classList.toggle('is-on', Number(b.dataset.nota) <= nota));
    $('#av-nota').value = String(nota);
  }
  document.querySelectorAll('#av-estrelas button').forEach(botao => {
    botao.onclick = () => pintarEstrelas(Number(botao.dataset.nota));
  });
  pintarEstrelas(5);

  $('#av-enviar').onclick = event => comBotao(event.currentTarget, 'Enviando…', async () => {
    aviso('#av-msg', '');
    const comentario = $('#av-comentario').value.trim();
    if (comentario.length < 10) return aviso('#av-msg', 'Escreva um comentário com pelo menos 10 caracteres.', true);
    try {
      const dados = await api('avaliacoes', { method: 'POST', body: JSON.stringify({
        petId: $('#av-pet').value, avaliacao: Number($('#av-nota').value), comentario
      }) });
      aviso('#av-msg', dados.message || 'Avaliação enviada!');
      $('#av-comentario').value = '';
      pintarEstrelas(5);
      renderAvaliacoes();
    } catch (e) { aviso('#av-msg', e.message, true); }
  });

  /* Foto de pet que nao carrega (arquivo corrompido, dado antigo invalido) --
     "error" em <img> nao borbulha, entao o listener precisa ser de captura.
     O aviso vai pro console (nao e escondido silenciosamente); a tela mostra
     o mesmo simbolo de fallback que ja usa para pet sem foto nenhuma. */
  function substituirFotoQuebrada(img) {
    console.warn('[LanePets] Não foi possível carregar a foto do pet — mostrando o ícone padrão.', img.src);
    const tipo = img.dataset.tipo || '';
    const tamanho = Number(img.dataset.tamanho || 34);
    if (img.classList.contains('cc-pet__retrato-grande')) {
      img.outerHTML = `<div class="cc-pet__retrato-grande cc-pet__retrato-grande--vazio">${ico(iconePet(tipo), tamanho)}</div>`;
      return;
    }
    const alvo = img.parentElement;
    if (!alvo) return;
    alvo.innerHTML = img.classList.contains('cc-pet__foto')
      ? `<span class="cc-pet__ico">${ico(iconePet(tipo), tamanho)}</span>`
      : ico(iconePet(tipo), tamanho);
  }
  document.addEventListener('error', evento => {
    const img = evento.target;
    if (img instanceof HTMLImageElement && img.classList.contains('pet-foto-real')) substituirFotoQuebrada(img);
  }, true);

  /* Navegação ------------------------------------------------------------ */
  /* Delegado no documento: vale para o menu lateral, para os atalhos do
     painel e para o "voltar" das areas internas — inclusive os que nascem
     depois, como o botao "Ver produtos" do estado vazio. */
  document.addEventListener('click', evento => {
    const link = evento.target.closest('a[data-rota]');
    if (!link) return;
    evento.preventDefault();
    irPara(link.dataset.rota);
  });
  $('#cc-menu').onclick = abrirMenu;
  $('#cc-fundo').onclick = fecharMenu;
  document.addEventListener('keydown', event => { if (event.key === 'Escape') fecharMenu(); });
  window.addEventListener('hashchange', () => { if (conta) irPara(rotaDoHash(), false); });

  /* Fechar modais -------------------------------------------------------- */
  document.addEventListener('click', event => {
    const alvo = event.target.closest('[data-fechar]');
    if (!alvo) return;
    event.preventDefault();
    const caixa = alvo.closest('dialog');
    if (caixa && typeof caixa.close === 'function') caixa.close();
  });
  document.querySelectorAll('dialog.cc-modal').forEach(caixa => {
    caixa.addEventListener('click', event => { if (event.target === caixa) caixa.close(); });
  });

  /* Sair ------------------------------------------------------------------ */
  async function sair(event) {
    if (event) event.preventDefault();
    try { await api('logout', { method: 'POST' }); }
    finally { localStorage.removeItem('lanePetsClienteToken'); location.href = 'cliente.html'; }
  }
  $('#sair').onclick = sair;
  $('#cc-sair-2').onclick = sair;
  const sair3 = $('#cc-sair-3');
  if (sair3) sair3.onclick = sair;

  /* Menu da conta no topo: abre, fecha no Esc, no clique fora e ao escolher
     um item. Os itens sao os mesmos destinos do menu lateral. */
  (function menuDaConta() {
    const caixa = $('#cc-conta'), botao = $('#cc-conta-botao'), menu = $('#cc-conta-menu');
    if (!caixa || !botao || !menu) return;
    const fechar = () => { menu.classList.remove('is-aberto'); botao.setAttribute('aria-expanded', 'false'); };
    botao.addEventListener('click', event => {
      event.stopPropagation();
      const abrindo = !menu.classList.contains('is-aberto');
      menu.classList.toggle('is-aberto', abrindo);
      botao.setAttribute('aria-expanded', String(abrindo));
    });
    menu.addEventListener('click', fechar);
    document.addEventListener('click', event => { if (!event.target.closest('#cc-conta')) fechar(); });
    document.addEventListener('keydown', event => { if (event.key === 'Escape') fechar(); });
  })();

  /* ---------------------------------------------------------------------
     Abertura automática quando já existe um token guardado.
     --------------------------------------------------------------------- */
  if (token) {
    abrir().catch(erro => {
      if (erro && erro.status === 401) {
        UI.alerta('#login-info', 'Sua sessão expirou. Entre novamente para continuar.');
      } else if (erro && erro.semConexao) {
        UI.alerta('#login-aviso', erro.message);
      } else if (erro) {
        UI.alerta('#login-aviso', erro.message || 'Não foi possível carregar a sua conta.');
      }
    });
  }
})();
