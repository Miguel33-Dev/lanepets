/**
 * LanePets — Administração > Log de eventos (item 15 do roadmap, 24/09).
 *
 * Somente leitura: lista GET /api/admin/eventos com filtros e paginação no
 * servidor. Quem decide se pode ver é a API (só Administrador Geral → senão 403).
 */
(function () {
  const CHAVE_TOKEN = 'lanePetsAuthToken';
  const POR_PAGINA = 50;
  const $ = id => document.getElementById(id);
  const esc = v => { const d = document.createElement('div'); d.textContent = v == null ? '' : String(v); return d.innerHTML; };
  const token = () => { try { return sessionStorage.getItem(CHAVE_TOKEN) || ''; } catch (_) { return ''; } };

  let offset = 0;
  let total = 0;
  let categoriasCarregadas = false;

  const NIVEL = {
    info: '<span class="badge badge-neutral">Info</span>',
    aviso: '<span class="badge badge-warning">Aviso</span>',
    erro: '<span class="badge badge-danger">Erro</span>'
  };
  const ROTULO_CATEGORIA = {
    autenticacao: 'Autenticação', seguranca: 'Segurança', administracao: 'Administração',
    cliente: 'Cliente', pet: 'Pet', agendamento: 'Agendamento', pedido: 'Pedido', produto: 'Produto',
    servico: 'Serviço', financeiro: 'Financeiro', seguro: 'Seguro', sistema: 'Sistema'
  };
  const ORIGEM = { cliente: 'Área do cliente', admin: 'Painel', publico: 'Site público', sistema: 'Sistema' };

  function quando(iso) {
    const d = new Date(iso);
    return isNaN(d) ? '—' : d.toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }

  function parametros() {
    const p = new URLSearchParams({ token: token(), limite: String(POR_PAGINA), offset: String(offset) });
    const campos = { busca: $('fBusca').value.trim(), categoria: $('fCategoria').value, nivel: $('fNivel').value, de: $('fDe').value, ate: $('fAte').value };
    Object.entries(campos).forEach(([k, v]) => { if (v) p.set(k, v); });
    return p.toString();
  }

  function mostrarErro(texto) {
    $('erro').textContent = texto || '';
    $('erro').style.display = texto ? 'block' : 'none';
  }

  async function carregar() {
    mostrarErro('');
    let corpo;
    try {
      const r = await fetch('/api/admin/eventos?' + parametros());
      corpo = await r.json().catch(() => null);
      if (r.status === 401) { location.replace('admin-login.html?retorno=' + encodeURIComponent('/eventos.html')); return; }
      if (!r.ok || !corpo || !corpo.ok) throw new Error((corpo && corpo.error) || 'Não foi possível carregar o log.');
    } catch (e) {
      console.error('[LanePets] Falha ao carregar o log de eventos:', e);
      mostrarErro(e.message);
      return;
    }

    const d = corpo.data;
    total = d.total || 0;
    $('kpiTotal').textContent = d.resumo.total24h;
    $('kpiLogins').textContent = d.resumo.loginsRecusados24h;
    $('kpiAvisos').textContent = d.resumo.avisos24h;
    $('kpiErros').textContent = d.resumo.erros24h;

    if (!categoriasCarregadas) {
      $('fCategoria').innerHTML = '<option value="">Todas</option>' +
        (d.categorias || []).filter(Boolean).map(c => `<option value="${esc(c)}">${esc(ROTULO_CATEGORIA[c] || c)}</option>`).join('');
      categoriasCarregadas = true;
    }

    const itens = d.itens || [];
    $('corpo').innerHTML = itens.map(e => `
      <tr>
        <td class="ev-quando">${esc(quando(e.dataHora))}</td>
        <td>${NIVEL[e.nivel] || esc(e.nivel)}</td>
        <td class="ev-acao"><strong>${esc(e.acao)}</strong><span class="ev-cat">${esc(ROTULO_CATEGORIA[e.categoria] || e.categoria)} · ${esc(ORIGEM[e.origem] || e.origem)}</span></td>
        <td>${esc(e.autor || '—')}${e.ip ? `<span class="ev-meta">IP ${esc(e.ip)}</span>` : ''}</td>
        <td class="ev-detalhes">${esc(e.detalhes || '')}
          ${e.alvoId ? `<span class="ev-meta">Registro: ${esc(e.alvoId)}</span>` : ''}
          ${e.referencia ? `<span class="ev-meta">Ref. ${esc(e.referencia)} — ver logs/erros-AAAA-MM-DD.log</span>` : ''}</td>
      </tr>`).join('');

    $('vazio').style.display = itens.length ? 'none' : 'block';
    const ate = Math.min(offset + itens.length, total);
    $('contagem').textContent = total ? `Mostrando ${offset + 1}–${ate} de ${total} evento(s)` : '';
    $('btnAnterior').disabled = offset === 0;
    $('btnProxima').disabled = offset + POR_PAGINA >= total;
  }

  function recomecar() { offset = 0; carregar(); }

  let timer = null;
  $('fBusca').addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(recomecar, 350); });
  ['fCategoria', 'fNivel', 'fDe', 'fAte'].forEach(id => $(id).addEventListener('change', recomecar));
  $('btnAtualizar').addEventListener('click', carregar);
  $('btnAnterior').addEventListener('click', () => { offset = Math.max(0, offset - POR_PAGINA); carregar(); });
  $('btnProxima').addEventListener('click', () => { if (offset + POR_PAGINA < total) { offset += POR_PAGINA; carregar(); } });

  window.LanePermissoes ? window.LanePermissoes.pronto(carregar) : carregar();
})();
