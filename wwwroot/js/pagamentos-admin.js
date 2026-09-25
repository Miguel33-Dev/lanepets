/**
 * LanePets — Financeiro > Pagamentos (item 8 do roadmap, 25/09).
 * O servidor decide as transicoes (PagamentosService) e recusa com a mensagem
 * certa; a tela so oferece os botoes que fazem sentido para cada status.
 */
(function () {
  const CHAVE_TOKEN = 'lanePetsAuthToken';
  const $ = id => document.getElementById(id);
  const esc = v => { const d = document.createElement('div'); d.textContent = v == null ? '' : String(v); return d.innerHTML; };
  const brl = v => Number(v || 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  const token = () => { try { return sessionStorage.getItem(CHAVE_TOKEN) || ''; } catch (_) { return ''; } };
  const podeEditar = () => !window.LanePermissoes || !window.LanePermissoes.pode || window.LanePermissoes.pode('pagamentos', 'editar');

  const ORIGEM = { agendamento: 'Serviço', pedido: 'Pedido da loja', seguro: 'Seguro Pet' };
  const TOM = { Pendente: 'badge-warning', Aprovado: 'badge-success', Recusado: 'badge-danger', Cancelado: 'badge-neutral', Reembolsado: 'badge-info' };
  /* Mesmas transicoes do servidor (PagamentosService.PodeIr). */
  const ACOES = {
    Pendente: [['Aprovado', 'Aprovar', 'btn-primary'], ['Recusado', 'Recusar', 'btn-outline'], ['Cancelado', 'Cancelar', 'btn-outline']],
    Recusado: [['Aprovado', 'Aprovar', 'btn-primary'], ['Pendente', 'Voltar a pendente', 'btn-outline'], ['Cancelado', 'Cancelar', 'btn-outline']],
    Aprovado: [['Reembolsado', 'Reembolsar', 'btn-outline'], ['Pendente', 'Desfazer aprovação', 'btn-outline']],
    Cancelado: [], Reembolsado: []
  };

  let itens = [];
  let alvo = null, alvoStatus = '';

  async function api(caminho, opcoes) {
    const r = await fetch('/api' + caminho, opcoes);
    const corpo = await r.json().catch(() => null);
    if (r.status === 401) { location.replace('admin-login.html?retorno=' + encodeURIComponent('/pagamentos.html')); throw new Error('Sessão expirada.'); }
    if (!r.ok || !corpo || !corpo.ok) throw new Error((corpo && corpo.error) || `O servidor retornou um erro inesperado (HTTP ${r.status}).`);
    return corpo.data;
  }

  function erro(id, texto) { $(id).textContent = texto || ''; $(id).style.display = texto ? 'block' : 'none'; }
  const dataCurta = v => { const d = new Date(v); return isNaN(d) ? (v || '—') : d.toLocaleDateString('pt-BR'); };

  /* Item 16: filtros na URL (o Dashboard abre ?status=Pendente ou ?reembolso=1). */
  const FILTROS = { busca: 'fBusca', status: 'fStatus', origem: 'fOrigem', unidade: 'fUnidade', de: 'fDe', ate: 'fAte', reembolso: 'fReembolso' };
  const temFiltro = () => window.LaneBusca ? LaneBusca.ativos(Object.values(FILTROS)) > 0 : false;

  function filtros() {
    if (window.LaneBusca) LaneBusca.gravarUrl(FILTROS);
    const q = new URLSearchParams({ token: token() });
    const add = (k, v) => { if (v) q.set(k, v); };
    add('busca', $('fBusca').value.trim()); add('status', $('fStatus').value); add('origem', $('fOrigem').value);
    add('unidade', $('fUnidade').value); add('de', $('fDe').value); add('ate', $('fAte').value);
    if ($('fReembolso').checked) q.set('reembolso', 'true');
    return q.toString();
  }

  async function carregar() {
    erro('erro', '');
    try {
      const d = await api('/admin/pagamentos?' + filtros());
      itens = d.itens || [];
      const r = d.resumo || {};
      $('kpiPendentes').textContent = r.pendentes ?? 0;
      $('kpiPendentesValor').textContent = brl(r.valorPendente);
      $('kpiAprovados').textContent = r.aprovados ?? 0;
      $('kpiAprovadosValor').textContent = brl(r.valorAprovado);
      $('kpiRecusados').textContent = (r.recusados || 0) + (r.cancelados || 0);
      $('kpiReembolsos').textContent = r.reembolsosPendentes ?? 0;
      const alerta = $('alertaReembolso');
      alerta.style.display = r.reembolsosPendentes ? 'block' : 'none';
      alerta.innerHTML = r.reembolsosPendentes
        ? `${r.reembolsosPendentes} pagamento(s) aprovado(s) de agendamento/pedido/seguro que foi cancelado. Decida o reembolso. <button class="btn btn-outline btn-sm" type="button" id="verReembolsos">Ver</button>` : '';
      desenhar();
    } catch (e) {
      console.error('[LanePets] pagamentos:', e);
      erro('erro', /404/.test(e.message) ? 'O servidor ainda não conhece os pagamentos. Pare o LanePets (Ctrl+C) e rode dotnet run de novo.' : e.message);
      $('corpo').innerHTML = '<tr><td colspan="8">Não foi possível carregar.</td></tr>';
    }
  }

  function desenhar() {
    const filtrado = temFiltro();
    $('contagem').textContent = `${itens.length} pagamento(s)${filtrado ? ' no filtro atual' : ''}`;
    $('btnLimparPag').style.display = filtrado ? '' : 'none';
    $('corpo').innerHTML = itens.length ? itens.map(p => `
      <tr>
        <td>${dataCurta(p.dataReferencia)}</td>
        <td><span class="pg-origem">${esc(ORIGEM[p.origem] || p.origem)}</span><br>${esc(p.descricao)}<br><code style="font-size:.72rem">${esc(p.origemId)}</code></td>
        <td>${esc(p.cliente || '—')}</td>
        <td>${esc(p.unidadeNome)}</td>
        <td>${esc(p.forma || 'A combinar')}</td>
        <td class="pg-valor">${brl(p.valor)}</td>
        <td><span class="badge ${TOM[p.status] || 'badge-neutral'}">${esc(p.status)}</span>
          ${p.reembolsoPendente ? '<br><span class="pg-reembolso">Reembolso pendente</span>' : ''}
          ${p.observacao ? `<br><span class="small-note">${esc(p.observacao)}</span>` : ''}</td>
        <td><div class="pg-acoes">${podeEditar() ? (ACOES[p.status] || [])
            .filter(([s]) => !p.reembolsoPendente || s === 'Reembolsado')
            .map(([s, rotulo, cls]) => `<button class="btn ${cls} btn-sm" type="button" data-pag="${esc(p.id)}" data-status="${s}">${rotulo}</button>`).join('') : ''}</div></td>
      </tr>`).join('') : (window.LaneBusca
        ? LaneBusca.linhaVazia(8, filtrado ? 1 : 0, { nenhum: 'Nenhum pagamento registrado ainda.', filtro: 'Nenhum pagamento encontrado com esses filtros.' })
        : '<tr><td colspan="8">Nenhum pagamento encontrado.</td></tr>');
  }

  function abrir(id, status) {
    alvo = itens.find(p => p.id === id); alvoStatus = status;
    if (!alvo) return;
    $('msTitulo').textContent = { Aprovado: 'Aprovar pagamento', Recusado: 'Recusar pagamento', Cancelado: 'Cancelar pagamento', Reembolsado: 'Registrar reembolso', Pendente: 'Voltar para pendente' }[status] || 'Alterar pagamento';
    $('msSub').textContent = `${ORIGEM[alvo.origem] || alvo.origem} · ${alvo.cliente || ''}`;
    $('msTexto').innerHTML = `${esc(alvo.descricao)} — <strong>${brl(alvo.valor)}</strong><br>${esc(alvo.status)} → <strong>${esc(status)}</strong>`
      + (status === 'Cancelado' || status === 'Reembolsado' ? '<br><span class="small-note">Esta ação é definitiva.</span>' : '');
    $('msObs').value = '';
    $('msConfirmar').className = 'btn ' + (status === 'Cancelado' || status === 'Recusado' ? 'btn-danger' : 'btn-primary');
    erro('msErro', '');
    $('modalStatus').classList.add('ativo');
  }
  const fechar = () => $('modalStatus').classList.remove('ativo');

  $('formStatus').addEventListener('submit', async e => {
    e.preventDefault();
    if (!alvo) return;
    const botao = $('msConfirmar'); botao.disabled = true;
    try {
      const r = await api(`/admin/pagamentos/${encodeURIComponent(alvo.id)}/status`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token: token(), status: alvoStatus, observacao: $('msObs').value.trim() })
      });
      fechar();
      if (window.toast) toast(r.message || 'Pagamento atualizado.', 'success');
      await carregar();
    } catch (err) { erro('msErro', err.message); }
    finally { botao.disabled = false; }
  });

  document.addEventListener('click', e => {
    const b = e.target.closest('[data-pag]');
    if (b) { abrir(b.dataset.pag, b.dataset.status); return; }
    if (e.target.closest('#modalStatus [data-fechar]') || e.target === $('modalStatus')) fechar();
    if (e.target.closest('#verReembolsos')) { $('fReembolso').checked = true; carregar(); }
    if (e.target.closest('[data-limpar-filtros]') && window.LaneBusca) {
      LaneBusca.limpar(Object.values(FILTROS));
      $('fUnidade').value = 'todas';
      carregar();
      $('fBusca').focus();
    }
  });
  document.addEventListener('keydown', e => { if (e.key === 'Escape') fechar(); });

  let espera = null;
  $('fBusca').addEventListener('input', () => { clearTimeout(espera); espera = setTimeout(carregar, 300); });
  ['fStatus', 'fOrigem', 'fUnidade', 'fDe', 'fAte', 'fReembolso'].forEach(id => $(id).addEventListener('change', carregar));
  $('btnAtualizar').addEventListener('click', carregar);

  if (window.LaneBusca) LaneBusca.atalhos('fBusca', carregar);

  window.addEventListener('load', async () => {
    LaneUnidades.preencherSelect($('fUnidade'), { todas: true, semUnidade: true });
    if (window.LaneBusca) LaneBusca.lerUrl(FILTROS);
    LaneUnidades.carregar().then(() => {
      LaneUnidades.preencherSelect($('fUnidade'), { todas: true, semUnidade: true });
      if (window.LaneBusca && LaneBusca.lerUrl({ unidade: 'fUnidade' })) carregar();
    });
    carregar();
  });
})();
