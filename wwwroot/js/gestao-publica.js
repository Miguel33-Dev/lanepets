/* =========================================================
   LanePets — Gestão da área pública
   Mesmas rotas e mesmas regras de negócio de sempre (planos de seguro,
   moderação de avaliações, solicitações de seguro). O que mudou é a
   apresentação: virou um hub de módulos (mesmo padrão do Cadastro em
   index.html) com KPIs reais, busca, filtros e ações em ícone
   (css/admin-acoes.js, já usado em Produtos e Usuários Administrativos).
   Nenhum dado é inventado: o que a API não devolve não aparece na tela.
   ========================================================= */
(function () {
  let token = '';
  let planoEmEdicao = null;
  const $ = seletor => document.querySelector(seletor);
  const $$ = seletor => Array.from(document.querySelectorAll(seletor));

  const api = async (url, opcoes = {}) => {
    const resposta = await fetch(url, opcoes);
    const corpo = await resposta.json();
    if (!resposta.ok || !corpo.ok) throw new Error(corpo.error || 'Erro ao processar.');
    return corpo.data;
  };
  const post = (url, corpo) => api(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ...corpo, token })
  });
  const esc = valor => { const d = document.createElement('div'); d.textContent = valor ?? ''; return d.innerHTML; };
  const brl = valor => Number(valor || 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  const aviso = (mensagem, tipo) => (window.toast ? toast(mensagem, tipo) : console.log(mensagem));
  const confirmar = (opcoes) => (window.confirmarAcao ? confirmarAcao(opcoes) : Promise.resolve(window.confirm(opcoes.texto)));

  const vazio = (titulo, texto) =>
    `<div class="empty-state"><h3>${titulo}</h3><p>${texto}</p></div>`;
  const tabela = (cabecalhos, linhas) =>
    `<div class="table-wrap"><table><thead><tr>${cabecalhos.map(c => `<th>${c}</th>`).join('')}</tr></thead><tbody>${linhas}</tbody></table></div>`;

  const BADGE = {
    'Pendente': 'badge-warning',
    'Aprovado': 'badge-success',
    'Concluída': 'badge-success',
    'Em contato': 'badge-info',
    'Recusado': 'badge-danger',
    'Cancelada': 'badge-danger',
    'Cancelado': 'badge-danger',
    'Pago': 'badge-success'
  };
  const badge = status => `<span class="badge ${BADGE[status] || 'badge-neutral'}">${esc(status)}</span>`;

  function dataCurta(iso) {
    if (!iso) return '—';
    const d = new Date(iso);
    return isNaN(d) ? '—' : d.toLocaleDateString('pt-BR');
  }

  /* ------------------------------------------------------------------
     ÍCONES — mesmo traço do resto do painel (stroke 1.8, sem preenchimento).
     ------------------------------------------------------------------ */
  const svg = miolo => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">${miolo}</svg>`;
  const ICONES = {
    visualizar: svg('<path d="M2.3 12S5.6 5 12 5s9.7 7 9.7 7-3.3 7-9.7 7-9.7-7-9.7-7Z"/><circle cx="12" cy="12" r="3.1"/>'),
    editar:     svg('<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/>'),
    ativar:     svg('<circle cx="12" cy="12" r="9"/><path d="M10.2 8.6 15.5 12l-5.3 3.4Z"/>'),
    desativar:  svg('<circle cx="12" cy="12" r="9"/><path d="M10 9.5v5M14 9.5v5"/>'),
    excluir:    svg('<path d="M3.5 6h17"/><path d="M8.5 6V4.5A1.5 1.5 0 0 1 10 3h4a1.5 1.5 0 0 1 1.5 1.5V6"/><path d="M6.5 6.5 7.4 19a2 2 0 0 0 2 1.9h5.2a2 2 0 0 0 2-1.9l.9-12.5"/><path d="M10.5 10.5v6M13.5 10.5v6"/>'),
    menu:       svg('<circle cx="12" cy="5" r="1.4"/><circle cx="12" cy="12" r="1.4"/><circle cx="12" cy="19" r="1.4"/>')
  };

  /* ------------------------------------------------------------------
     PRÉVIA — modal simples, reaproveitando o modal-box do design system
     (o mesmo de confirmarAcao). Mostra o conteúdo como ele aparece no
     site, sem duplicar a página pública inteira.
     ------------------------------------------------------------------ */
  function abrirPreview(titulo, corpoHtml) {
    const overlay = document.createElement('div');
    overlay.className = 'modal-overlay ativo';
    overlay.innerHTML = `
      <div class="modal-box modal-wide" role="dialog" aria-modal="true">
        <div class="modal-head"><h2>Pré-visualização</h2>
          <button type="button" class="modal-close" data-fechar aria-label="Fechar"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg></button>
        </div>
        <p style="margin:0 0 var(--sp-4);font-size:.82rem;color:var(--ink-500)">Assim é como aparece hoje na área pública do site.</p>
        ${corpoHtml}
        <div class="modal-actions"><button type="button" class="btn btn-primary" data-fechar>Fechar</button></div>
      </div>`;
    document.body.appendChild(overlay);
    const fechar = () => overlay.remove();
    overlay.addEventListener('mousedown', e => { if (e.target === overlay) fechar(); });
    overlay.querySelectorAll('[data-fechar]').forEach(b => b.addEventListener('click', fechar));
  }

  function previewPlano(p) {
    const coberturas = String(p.coberturas || '').split(';').map(i => i.trim()).filter(Boolean);
    abrirPreview('Plano', `<div class="preview-plano">
      <h3>${esc(p.nome)}</h3>
      <p style="margin:0;color:var(--ink-500);font-size:.88rem">${esc(p.descricao || '')}</p>
      <div class="valor">${brl(p.valorMensal)}<small style="font-size:.7rem;font-weight:600;color:var(--ink-500)"> /mês</small></div>
      ${coberturas.length ? `<ul>${coberturas.map(c => `<li>${esc(c)}</li>`).join('')}</ul>` : ''}
      ${p.beneficios ? `<p style="margin-top:var(--sp-3);font-size:.86rem">${esc(p.beneficios)}</p>` : ''}
      ${p.condicoes ? `<p class="preview-nota"><strong>Condições:</strong> ${esc(p.condicoes)}</p>` : ''}
      <p class="preview-nota">${p.ativo ? 'Visível no site agora.' : 'Oculto — não aparece no site enquanto estiver desativado.'}</p>
    </div>`);
  }

  function previewDepoimento(d) {
    abrirPreview('Avaliação', `<div class="avaliacao-card" style="box-shadow:none">
      <span class="avaliacao-estrelas">${'★'.repeat(d.avaliacao)}${'☆'.repeat(5 - d.avaliacao)}</span>
      <blockquote>“${esc(d.comentario)}”</blockquote>
      <cite>${esc(d.nomeCliente)} · tutor(a) de ${esc(d.nomePet)}</cite>
      <p class="preview-nota">${d.status === 'Aprovado' ? 'Publicado no site agora.' : 'Ainda não aparece publicamente — status: ' + esc(d.status) + '.'}</p>
    </div>`);
  }

  /* ------------------------------------------------------------------
     ESTADO
     ------------------------------------------------------------------ */
  let planosCache = [];
  let depoimentosCache = [];
  let solicitacoesCache = [];

  function normalizar(v) {
    return String(v || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
  }

  /* ------------------------------------------------------------------
     ROTEAMENTO — hub / #seguro / #solicitacoes / #avaliacoes, sem recarga
     de página. Mesmo padrão do hub de Cadastro (index.html).
     ------------------------------------------------------------------ */
  const VIEWS = { hub: 'viewHub', seguro: 'viewSeguro', solicitacoes: 'viewSolicitacoes', avaliacoes: 'viewAvaliacoes' };
  function aplicarRota() {
    if ($('#gestao').style.display === 'none') return; // ainda não logou
    const rota = (location.hash || '').replace('#', '');
    const alvo = VIEWS[rota] ? rota : 'hub';
    Object.keys(VIEWS).forEach(chave => {
      const el = $('#' + VIEWS[chave]);
      if (el) el.style.display = chave === alvo ? '' : 'none';
    });
    if (alvo !== 'hub') window.scrollTo({ top: 0, behavior: 'instant' in window ? 'instant' : 'auto' });
  }
  window.addEventListener('hashchange', aplicarRota);

  /* ------------------------------------------------------------------
     HUB — KPIs e cartões de módulo, tudo a partir dos dados já carregados.
     ------------------------------------------------------------------ */
  function preencher(seletor, texto) {
    $$(seletor).forEach(el => { el.textContent = texto; el.classList.remove('skeleton'); });
  }

  function renderHub() {
    const planosAtivos = planosCache.filter(p => p.ativo).length;
    const planosOcultos = planosCache.length - planosAtivos;
    const depAprovados = depoimentosCache.filter(d => d.status === 'Aprovado').length;
    const depPendentes = depoimentosCache.filter(d => d.status === 'Pendente').length;
    const depRecusados = depoimentosCache.length - depAprovados - depPendentes;
    const solPendentes = solicitacoesCache.filter(s => s.status === 'Pendente').length;

    preencher('[data-kpi="publicados"]', String(planosAtivos + depAprovados));
    preencher('[data-kpi="ocultos"]', String(planosOcultos + depRecusados));
    preencher('[data-kpi="pendentes"]', String(depPendentes + solPendentes));

    const datas = [...depoimentosCache.map(d => d.criadoEm), ...solicitacoesCache.map(s => s.criadoEm)]
      .filter(Boolean).map(v => new Date(v)).filter(d => !isNaN(d));
    const ultima = datas.length ? new Date(Math.max(...datas)) : null;
    preencher('[data-kpi="ultimaAtividade"]', ultima ? ultima.toLocaleDateString('pt-BR') : 'Sem atividade');

    preencher('[data-count="seguro"]', planosCache.length + (planosCache.length === 1 ? ' plano cadastrado' : ' planos cadastrados'));
    preencher('[data-count="solicitacoes"]', solicitacoesCache.length + (solicitacoesCache.length === 1 ? ' solicitação recebida' : ' solicitações recebidas'));
    preencher('[data-count="avaliacoes"]', depoimentosCache.length + (depoimentosCache.length === 1 ? ' avaliação recebida' : ' avaliações recebidas'));

    const badgeSolicitacoes = $('[data-badge="solicitacoes"]');
    if (badgeSolicitacoes) { badgeSolicitacoes.hidden = solPendentes === 0; badgeSolicitacoes.textContent = solPendentes + ' pendente' + (solPendentes === 1 ? '' : 's'); }
    const badgeAvaliacoes = $('[data-badge="avaliacoes"]');
    if (badgeAvaliacoes) { badgeAvaliacoes.hidden = depPendentes === 0; badgeAvaliacoes.textContent = depPendentes + ' pendente' + (depPendentes === 1 ? '' : 's'); }

    filtrarModulosHub();
  }

  function filtrarModulosHub() {
    const input = $('#buscaPublica');
    const termo = normalizar(input ? input.value : '');
    const cartoes = $$('#gradeModulosPublica .module-card');
    let visiveis = 0;
    cartoes.forEach(card => {
      if (card.style.display === 'none' && card.dataset.escondidoPermissao === '1') return;
      const texto = normalizar((card.querySelector('h3')?.textContent || '') + ' ' + (card.dataset.search || ''));
      const bate = !termo || texto.includes(termo);
      card.style.display = bate ? '' : 'none';
      if (bate) visiveis++;
    });
    const vazioEl = $('#buscaPublicaVazio');
    if (vazioEl) vazioEl.style.display = (termo && visiveis === 0) ? '' : 'none';
  }

  function iniciarBuscaHub() {
    const input = $('#buscaPublica');
    if (input) input.addEventListener('input', filtrarModulosHub);
  }

  /* Produtos e Serviços são administrados em telas próprias (Produtos.html e
     o Cadastro em index.html) — aqui só mostramos a contagem real do mesmo
     catálogo que a área pública lê, sem duplicar CRUD nenhum. */
  async function contarAtalhos() {
    try {
      const produtos = await api('/api/public/produtos');
      const disponiveis = produtos.filter(p => p.disponivel).length;
      preencher('[data-count="produtos"]', produtos.length + ' cadastrados · ' + disponiveis + ' à venda');
    } catch (_) { preencher('[data-count="produtos"]', '—'); }
    try {
      const servicos = await api('/api/public/servicos');
      preencher('[data-count="servicos"]', servicos.length + (servicos.length === 1 ? ' serviço no site' : ' serviços no site'));
    } catch (_) { preencher('[data-count="servicos"]', '—'); }
  }

  /* ------------------------------------------------------------------
     SEGURO PET
     ------------------------------------------------------------------ */
  function planosFiltrados() {
    const termo = normalizar($('#buscaSeguro') ? $('#buscaSeguro').value : '');
    const filtro = $('#filtroSeguroStatus') ? $('#filtroSeguroStatus').value : 'todos';
    return planosCache.filter(p => {
      if (filtro === 'visiveis' && !p.ativo) return false;
      if (filtro === 'ocultos' && p.ativo) return false;
      if (termo && !normalizar(p.nome + ' ' + (p.descricao || '')).includes(termo)) return false;
      return true;
    });
  }

  function linhaAcoesPlano(p) {
    const itens = [
      { acao: 'visualizar', rotulo: 'Visualizar' },
      { acao: 'editar', rotulo: 'Editar' },
      p.ativo ? { acao: 'despublicar', icone: 'desativar', rotulo: 'Desativar', curto: 'Desativar' }
              : { acao: 'publicar', icone: 'ativar', rotulo: 'Publicar', curto: 'Publicar' },
      { acao: 'excluir', icone: 'excluir', rotulo: 'Excluir', perigo: true }
    ];
    const linha = itens.map(i => `<button type="button" class="acao" data-acao="${i.acao}" data-id="${esc(p.id)}" data-dica="${esc(i.rotulo)}" aria-label="${esc(i.rotulo)} plano ${esc(p.nome)}">${ICONES[i.icone || i.acao]}</button>`).join('');
    const lista = itens.map(i => `<button type="button" role="menuitem" class="acao-item${i.perigo ? ' acao-item--perigo' : ''}" data-acao="${i.acao}" data-id="${esc(p.id)}">${ICONES[i.icone || i.acao]}<span>${esc(i.curto || i.rotulo)}</span></button>`).join('');
    return `<div class="acoes"><div class="acoes-linha">${linha}</div>
      <div class="acoes-compacta">
        <button type="button" class="acao acao--menu" data-menu="${esc(p.id)}" aria-label="Ações do plano ${esc(p.nome)}" aria-haspopup="menu" aria-expanded="false">${ICONES.menu}</button>
        <div class="acoes-lista" role="menu" hidden>${lista}</div>
      </div></div>`;
  }

  function renderSeguro() {
    const lista = planosFiltrados();
    $('#planos').innerHTML = lista.length
      ? tabela(['Plano', 'Valor mensal', 'Situação', 'Ações'], lista.map(p => `
          <tr>
            <td><strong>${esc(p.nome)}</strong><br><span class="text-muted">${esc(p.descricao || '')}</span>
              ${p.condicoes ? `<br><span class="text-muted"><strong>Condições:</strong> ${esc(p.condicoes)}</span>` : ''}</td>
            <td class="num">${brl(p.valorMensal)}</td>
            <td>${p.ativo ? '<span class="badge badge-success">Visível no site</span>' : '<span class="badge badge-neutral">Oculto</span>'}</td>
            <td>${linhaAcoesPlano(p)}</td>
          </tr>`).join(''))
      : (planosCache.length
          ? vazio('Nenhum plano encontrado', 'Tente buscar por outro termo ou limpar os filtros.')
          : vazio('Nenhum plano cadastrado', 'Crie o primeiro plano no formulário ao lado.'));
  }

  function limparFormularioPlano() {
    ['nome', 'descricao', 'coberturas', 'beneficios', 'condicoes', 'valor']
      .forEach(id => { if ($('#' + id)) $('#' + id).value = ''; });
    planoEmEdicao = null;
    $('#criar-plano').textContent = 'Salvar plano';
    if ($('#cancelar-edicao')) $('#cancelar-edicao').style.display = 'none';
  }

  function editarPlano(plano) {
    $('#nome').value = plano.nome || '';
    $('#descricao').value = plano.descricao || '';
    $('#coberturas').value = plano.coberturas || '';
    $('#beneficios').value = plano.beneficios || '';
    if ($('#condicoes')) $('#condicoes').value = plano.condicoes || '';
    $('#valor').value = plano.valorMensal || '';
    planoEmEdicao = plano.id;
    $('#criar-plano').textContent = 'Salvar alterações';
    if ($('#cancelar-edicao')) $('#cancelar-edicao').style.display = '';
    $('#nome').focus();
  }

  async function acaoPlano(acao, id) {
    const plano = planosCache.find(p => p.id === id);
    if (!plano) return;
    if (acao === 'visualizar') return previewPlano(plano);
    if (acao === 'editar') { location.hash = '#seguro'; return editarPlano(plano); }
    if (acao === 'publicar' || acao === 'despublicar') {
      const publicando = acao === 'publicar';
      const ok = await confirmar({
        titulo: publicando ? 'Publicar plano?' : 'Desativar plano?',
        texto: publicando
          ? `O plano "${plano.nome}" passará a ser exibido na área pública.`
          : `O plano "${plano.nome}" deixará de ser exibido publicamente. O histórico é mantido.`,
        corBotao: publicando ? 'btn-primary' : 'btn-danger',
        textoBotao: publicando ? 'Publicar' : 'Desativar'
      });
      if (!ok) return;
      try { await post('/api/admin/seguros/' + id + '/ativo', { ativo: publicando }); aviso('Plano atualizado.', 'success'); await carregarTudo(); }
      catch (erro) { aviso(erro.message, 'error'); }
      return;
    }
    if (acao === 'excluir') {
      const ok = await confirmar({
        titulo: 'Excluir plano?',
        texto: `O plano "${plano.nome}" será removido. Planos com contrato não podem ser excluídos — nesse caso, desative.`,
        corBotao: 'btn-danger', textoBotao: 'Excluir'
      });
      if (!ok) return;
      try {
        await api('/api/admin/seguros/' + id + '?token=' + encodeURIComponent(token), { method: 'DELETE' });
        aviso('Plano excluído.', 'success');
        await carregarTudo();
      } catch (erro) { aviso(erro.message, 'error'); }
    }
  }

  /* ------------------------------------------------------------------
     SOLICITAÇÕES DE SEGURO
     ------------------------------------------------------------------ */
  function solicitacoesFiltradas() {
    const termo = normalizar($('#buscaSolicitacoes') ? $('#buscaSolicitacoes').value : '');
    const filtro = $('#filtroSolicitacaoStatus') ? $('#filtroSolicitacaoStatus').value : 'todos';
    return solicitacoesCache.filter(s => {
      if (filtro !== 'todos' && s.status !== filtro) return false;
      if (termo && !normalizar((s.clienteAtual || s.nomeCliente) + ' ' + (s.petAtual || s.nomePet)).includes(termo)) return false;
      return true;
    });
  }

  function renderSolicitacoes() {
    const lista = solicitacoesFiltradas();
    $('#solicitacoes').innerHTML = lista.length
      ? tabela(['Plano', 'Cliente / pet', 'Telefone', 'Valor', 'Pagamento', 'Status', 'Atualizar'], lista.map(s => `
          <tr>
            <td><strong>${esc(s.nomePlano)}</strong><br><span class="text-muted">${brl(s.valorMensal)}/mês · ${dataCurta(s.criadoEm)}</span></td>
            <td><strong>${esc(s.clienteAtual || s.nomeCliente)}</strong>
              <br><span class="text-muted">${esc(s.petAtual || s.nomePet)}</span>
              <br>${s.temCadastro
                  ? '<span class="badge badge-success">Conta vinculada</span>'
                  : '<span class="badge badge-neutral">Pedido pelo site</span>'}</td>
            <td>${esc(s.telefoneAtual || s.telefone)}</td>
            <td class="num">${brl(s.valor || s.valorMensal)}<br><span class="text-muted">contratado</span></td>
            <td>${esc(s.metodoPagamento || 'A combinar')}${s.cartaoFinal ? `<br><span class="text-muted">**** ${esc(s.cartaoFinal)}</span>` : ''}<br>${badge(s.pagamentoStatus || 'Pendente')}</td>
            <td>${badge(s.status)}${s.dataCancelamento ? `<br><span class="text-muted">Cancelado em ${dataCurta(s.dataCancelamento)}</span>` : ''}</td>
            <td><select class="input sol-status" data-id="${esc(s.id)}" style="min-width:150px">
              ${['Pendente', 'Em contato', 'Concluída', 'Cancelada'].map(op => `<option ${s.status === op ? 'selected' : ''}>${op}</option>`).join('')}
            </select></td>
          </tr>`).join(''))
      : (solicitacoesCache.length
          ? vazio('Nenhuma solicitação encontrada', 'Tente buscar por outro termo ou limpar os filtros.')
          : vazio('Nenhuma solicitação recebida', 'As contratações pedidas pelo site aparecem nesta lista.'));

    $$('.sol-status').forEach(campo => campo.onchange = async () => {
      try { await post('/api/admin/solicitacoes/' + campo.dataset.id + '/status', { status: campo.value }); aviso('Status atualizado.', 'success'); await carregarTudo(); }
      catch (erro) { aviso(erro.message, 'error'); }
    });
  }

  /* ------------------------------------------------------------------
     AVALIAÇÕES — cartões, no mesmo formato do site, não tabela.
     ------------------------------------------------------------------ */
  function avaliacoesFiltradas() {
    const termo = normalizar($('#buscaAvaliacoes') ? $('#buscaAvaliacoes').value : '');
    const filtro = $('#filtroAvaliacaoStatus') ? $('#filtroAvaliacaoStatus').value : 'todos';
    return depoimentosCache.filter(d => {
      if (filtro !== 'todos' && d.status !== filtro) return false;
      if (termo && !normalizar(d.nomeCliente + ' ' + d.comentario).includes(termo)) return false;
      return true;
    });
  }

  function renderAvaliacoes() {
    const lista = avaliacoesFiltradas();
    const alvo = $('#avaliacoesGrade');
    if (!lista.length) {
      alvo.innerHTML = depoimentosCache.length
        ? vazio('Nenhuma avaliação encontrada', 'Tente buscar por outro termo ou limpar os filtros.')
        : vazio('Nenhuma avaliação enviada', 'Assim que uma família avaliar pelo site, ela aparece aqui para moderação.');
      return;
    }
    alvo.innerHTML = lista.map(d => `
      <article class="avaliacao-card">
        <span class="avaliacao-estrelas" aria-label="${d.avaliacao} de 5 estrelas">${'★'.repeat(d.avaliacao)}${'☆'.repeat(5 - d.avaliacao)}</span>
        <blockquote>“${esc(d.comentario)}”</blockquote>
        <cite>${esc(d.nomeCliente)} · tutor(a) de ${esc(d.nomePet)}</cite>
        <div class="avaliacao-card-foot">
          ${badge(d.status)}
          <div class="row-actions">
            <button class="btn btn-outline btn-sm" type="button" data-acao-dep="visualizar" data-id="${esc(d.id)}">Visualizar</button>
            ${d.status === 'Pendente' ? `
              <button class="btn btn-secondary btn-sm" type="button" data-acao-dep="Aprovado" data-id="${esc(d.id)}">Aprovar</button>
              <button class="btn btn-danger btn-sm" type="button" data-acao-dep="Recusado" data-id="${esc(d.id)}">Recusar</button>` : ''}
          </div>
        </div>
      </article>`).join('');
  }

  async function acaoDepoimento(acao, id) {
    const dep = depoimentosCache.find(d => d.id === id);
    if (!dep) return;
    if (acao === 'visualizar') return previewDepoimento(dep);
    try { await post('/api/admin/depoimentos/' + id + '/status', { status: acao }); aviso('Avaliação ' + acao.toLowerCase() + '.', 'success'); await carregarTudo(); }
    catch (erro) { aviso(erro.message, 'error'); }
  }

  /* ------------------------------------------------------------------
     CARREGAMENTO GERAL
     ------------------------------------------------------------------ */
  async function carregarTudo() {
    $('#erroGeralPublica').style.display = 'none';
    try {
      const [planos, depoimentos, solicitacoes] = await Promise.all([
        api('/api/admin/seguros?token=' + encodeURIComponent(token)),
        api('/api/admin/depoimentos?token=' + encodeURIComponent(token)),
        api('/api/admin/seguros/solicitacoes?token=' + encodeURIComponent(token))
      ]);
      planosCache = planos;
      depoimentosCache = depoimentos;
      solicitacoesCache = solicitacoes;
      renderHub();
      renderSeguro();
      renderSolicitacoes();
      renderAvaliacoes();
    } catch (erro) {
      console.error('[LanePets] falha ao carregar a área pública:', erro);
      $('#erroGeralPublica').style.display = '';
      aviso('Não foi possível carregar este conteúdo.', 'error');
    }
  }

  /* Ações de linha/menu compacto do Seguro Pet: um único listener, porque
     data-acao é reaproveitado tanto na linha de ícones quanto no menu "⋮". */
  document.addEventListener('click', evento => {
    const botaoPlano = evento.target.closest('[data-acao][data-id]');
    if (botaoPlano && botaoPlano.closest('#planos')) { acaoPlano(botaoPlano.dataset.acao, botaoPlano.dataset.id); return; }
    const botaoDep = evento.target.closest('[data-acao-dep]');
    if (botaoDep) { acaoDepoimento(botaoDep.dataset.acaoDep, botaoDep.dataset.id); return; }
  });

  /* ------------------------------------------------------------------
     LOGIN — mesmo fluxo de sempre (POST /api/login exige e-mail + senha).
     ------------------------------------------------------------------ */
  async function entrar() {
    const botao = $('#entrar');
    const mensagem = $('#mensagem');
    mensagem.textContent = '';
    const email = $('#email').value.trim();
    const senha = $('#senha').value;
    if (!email || !senha) { mensagem.textContent = 'Informe e-mail e senha.'; return; }

    const textoOriginal = botao.innerHTML;
    botao.disabled = true;
    botao.innerHTML = '<span class="spinner"></span> Entrando…';
    try {
      const dados = await api('/api/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, senha })
      });
      token = dados.token;
      $('#login').style.display = 'none';
      $('#gestao').style.display = 'block';
      aplicarRota();
      iniciarBuscaHub();
      await carregarTudo();
      contarAtalhos();
    } catch (erro) {
      mensagem.textContent = erro.message;
      botao.disabled = false;
      botao.innerHTML = textoOriginal;
    }
  }

  $('#entrar').onclick = entrar;
  $('#senha').addEventListener('keydown', e => { if (e.key === 'Enter') entrar(); });

  if ($('#cancelar-edicao')) $('#cancelar-edicao').onclick = limparFormularioPlano;
  if ($('#btnTentarDeNovoPublica')) $('#btnTentarDeNovoPublica').onclick = carregarTudo;

  $('#criar-plano').onclick = async () => {
    const dados = {
      nome: $('#nome').value,
      descricao: $('#descricao').value,
      coberturas: $('#coberturas').value,
      beneficios: $('#beneficios').value,
      condicoes: $('#condicoes') ? $('#condicoes').value : '',
      valorMensal: Number($('#valor').value)
    };
    try {
      if (planoEmEdicao) {
        await api('/api/admin/seguros/' + planoEmEdicao, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ...dados, token })
        });
        aviso('Plano atualizado. A alteração já vale para os clientes.', 'success');
      } else {
        await post('/api/admin/seguros', dados);
        aviso('Plano salvo com sucesso.', 'success');
      }
      limparFormularioPlano();
      await carregarTudo();
    } catch (erro) { aviso(erro.message, 'error'); }
  };

  /* Busca e filtros de cada módulo: redesenha a partir do cache local, sem
     nova chamada à API a cada tecla. */
  ['buscaSeguro', 'filtroSeguroStatus'].forEach(id => {
    const el = $('#' + id);
    if (el) el.addEventListener(id === 'buscaSeguro' ? 'input' : 'change', renderSeguro);
  });
  if ($('#limparFiltrosSeguro')) $('#limparFiltrosSeguro').onclick = () => {
    if ($('#buscaSeguro')) $('#buscaSeguro').value = '';
    if ($('#filtroSeguroStatus')) $('#filtroSeguroStatus').value = 'todos';
    renderSeguro();
  };

  ['buscaSolicitacoes', 'filtroSolicitacaoStatus'].forEach(id => {
    const el = $('#' + id);
    if (el) el.addEventListener(id === 'buscaSolicitacoes' ? 'input' : 'change', renderSolicitacoes);
  });
  if ($('#limparFiltrosSolicitacoes')) $('#limparFiltrosSolicitacoes').onclick = () => {
    if ($('#buscaSolicitacoes')) $('#buscaSolicitacoes').value = '';
    if ($('#filtroSolicitacaoStatus')) $('#filtroSolicitacaoStatus').value = 'todos';
    renderSolicitacoes();
  };

  ['buscaAvaliacoes', 'filtroAvaliacaoStatus'].forEach(id => {
    const el = $('#' + id);
    if (el) el.addEventListener(id === 'buscaAvaliacoes' ? 'input' : 'change', renderAvaliacoes);
  });
  if ($('#limparFiltrosAvaliacoes')) $('#limparFiltrosAvaliacoes').onclick = () => {
    if ($('#buscaAvaliacoes')) $('#buscaAvaliacoes').value = '';
    if ($('#filtroAvaliacaoStatus')) $('#filtroAvaliacaoStatus').value = 'todos';
    renderAvaliacoes();
  };
})();
