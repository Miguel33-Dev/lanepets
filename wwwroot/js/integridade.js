/**
 * LanePets — Administração > Integridade do banco (item 19 do roadmap, 25/09).
 * Somente leitura. A API exige Administrador Geral.
 */
(function () {
  const $ = id => document.getElementById(id);
  const esc = v => { const d = document.createElement('div'); d.textContent = v == null ? '' : String(v); return d.innerHTML; };
  const token = () => { try { return sessionStorage.getItem('lanePetsAuthToken') || ''; } catch (_) { return ''; } };
  const ORDEM = { erro: 0, aviso: 1, info: 2, ok: 3 };
  const ROTULO = { erro: 'Erro', aviso: 'Aviso', info: 'Informativo', ok: 'Sem problema' };
  let dados = null, filtro = '';

  const tamanho = b => b > 1048576 ? (b / 1048576).toFixed(1) + ' MB' : Math.max(1, Math.round(b / 1024)) + ' KB';
  const quando = v => { const d = new Date(v); return isNaN(d) ? '—' : d.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }); };

  async function carregar() {
    const botao = $('btnVerificar'); botao.disabled = true;
    $('erro').style.display = 'none';
    try {
      const r = await fetch('/api/admin/integridade?token=' + encodeURIComponent(token()));
      const corpo = await r.json().catch(() => null);
      if (r.status === 401) { location.replace('admin-login.html?retorno=%2Fintegridade.html'); return; }
      if (!r.ok || !corpo || !corpo.ok) {
        throw new Error(r.status === 404
          ? 'O servidor ainda não conhece esta verificação. Pare o LanePets (Ctrl+C) e rode dotnet run de novo.'
          : (corpo && corpo.error) || `O servidor retornou um erro inesperado (HTTP ${r.status}).`);
      }
      dados = corpo.data;
      desenhar();
    } catch (e) {
      console.error('[LanePets] integridade:', e);
      $('erro').textContent = e.message; $('erro').style.display = 'block';
      $('lista').innerHTML = '';
    } finally { botao.disabled = false; }
  }

  function desenhar() {
    const r = dados.resumo;
    $('kpiErros').textContent = r.erros; $('kpiAvisos').textContent = r.avisos;
    $('kpiRegistros').textContent = r.registrosComProblema; $('kpiOk').textContent = r.ok;
    $('kpiGerado').textContent = 'Verificado ' + quando(dados.geradoEm);

    const itens = dados.verificacoes.slice().sort((a, b) => ORDEM[a.nivel] - ORDEM[b.nivel] || b.total - a.total)
      .filter(v => !filtro || v.nivel === filtro);
    $('lista').innerHTML = itens.length ? itens.map(v => `
      <details class="ig-item" data-nivel="${esc(v.nivel)}" ${v.nivel === 'erro' ? 'open' : ''}>
        <summary><span class="ig-bola" aria-hidden="true"></span><span class="ig-titulo">${esc(v.titulo)}</span>
          <span class="badge ${v.nivel === 'erro' ? 'badge-danger' : v.nivel === 'aviso' ? 'badge-warning' : v.nivel === 'ok' ? 'badge-success' : 'badge-info'}">${ROTULO[v.nivel]}</span>
          <span class="ig-total">${v.total}</span></summary>
        ${v.total ? `<div class="ig-corpo"><p class="ig-dica">${esc(v.dica)}</p>
          <ul>${v.exemplos.map(e => `<li><code>${esc(e.id)}</code> — ${esc(e.descricao)}</li>`).join('')}</ul>
          ${v.total > v.exemplos.length ? `<p class="ig-dica">Mostrando ${v.exemplos.length} de ${v.total}.</p>` : ''}</div>` : ''}
      </details>`).join('') : '<p class="small-note">Nenhuma verificação neste filtro.</p>';

    const b = dados.banco;
    $('bkInfo').innerHTML = `Pasta: <code>${esc(b.pastaBackups)}</code><br>`
      + (b.erroBackup ? `<span style="color:var(--danger-500)">Último backup falhou: ${esc(b.erroBackup)}</span>`
        : `Guarda os 10 mais recentes. Feito a cada subida do servidor.`);
    $('bkLista').innerHTML = b.backups.length ? b.backups.map(x => `<tr><td>${esc(x.arquivo)}</td><td>${tamanho(x.bytes)}</td><td>${quando(x.criadoEm)}</td></tr>`).join('')
      : '<tr><td colspan="3">Nenhum backup ainda (é feito na próxima subida).</td></tr>';
    $('ixLista').innerHTML = b.indices.length ? b.indices.map(i => `<tr><td>${esc(i.tabela)}</td><td>${esc(i.colunas)}</td><td>${esc(i.situacao)}</td></tr>`).join('')
      : '<tr><td colspan="3">Reinicie o servidor para criar os índices.</td></tr>';
  }

  document.querySelectorAll('[data-nivel]').forEach(bt => bt.tagName === 'BUTTON' && bt.addEventListener('click', () => {
    filtro = bt.dataset.nivel;
    document.querySelectorAll('.ig-filtros [data-nivel]').forEach(x => x.classList.toggle('is-ativo', x === bt));
    if (dados) desenhar();
  }));
  $('btnVerificar').addEventListener('click', carregar);
  window.addEventListener('load', carregar);
})();
