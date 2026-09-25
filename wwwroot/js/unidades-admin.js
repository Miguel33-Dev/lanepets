/**
 * LanePets — Administração > Unidades (item 5 do roadmap, 24/09).
 * A API decide (módulo "unidades", 403 sem permissão); esta tela só mostra
 * e envia. Não existe excluir: só ativar/desativar.
 */
(function () {
  const CHAVE_TOKEN = 'lanePetsAuthToken';
  const $ = id => document.getElementById(id);
  const esc = v => { const d = document.createElement('div'); d.textContent = v == null ? '' : String(v); return d.innerHTML; };
  const token = () => { try { return sessionStorage.getItem(CHAVE_TOKEN) || ''; } catch (_) { return ''; } };
  const pode = acao => !window.LanePermissoes || !window.LanePermissoes.pode || window.LanePermissoes.pode('unidades', acao);

  let unidades = [];
  let servicos = [];
  let editando = '';

  async function api(caminho, opcoes) {
    const r = await fetch('/api' + caminho, opcoes);
    const corpo = await r.json().catch(() => null);
    if (r.status === 401) { location.replace('admin-login.html?retorno=' + encodeURIComponent('/unidades.html')); throw new Error('Sessão expirada.'); }
    if (!r.ok || !corpo || !corpo.ok) throw new Error((corpo && corpo.error) || 'Não foi possível concluir a operação.');
    return corpo.data;
  }
  const enviar = (metodo, caminho, dados) => api(caminho, {
    method: metodo, headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(Object.assign({ token: token() }, dados))
  });

  function erro(id, texto) { $(id).textContent = texto || ''; $(id).style.display = texto ? 'block' : 'none'; }

  /* Mesmo servico existe por porte ("Banho" Pequeno/Medio/Grande): o rotulo
     leva o porte e o preco, senao a lista mostra tres "Banho" iguais. */
  const rotuloServico = s => {
    const nome = s.nome || s.Nome || '';
    const porte = s.porte || s.Porte || '';
    const preco = s.preco ?? s.Preco;
    return nome + (porte ? ` · ${porte === 'Gato' ? 'Gato' : 'Porte ' + porte}` : '')
      + (preco != null ? ` · ${Number(preco).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}` : '');
  };
  /* Cidades sugeridas no campo Nome (datalist: escolhe na lista ou digita). A
     regiao das unidades atuais primeiro. Nome ja usado por outra unidade nao
     aparece. O servidor continua validando nome unico. */
  const CIDADES = [
    'Franco da Rocha', 'Caieiras', 'Francisco Morato', 'Mairiporã', 'Cajamar', 'Jundiaí',
    'Campo Limpo Paulista', 'Várzea Paulista', 'Santana de Parnaíba', 'Pirapora do Bom Jesus',
    'Cabreúva', 'Atibaia', 'Bragança Paulista', 'Guarulhos', 'Osasco', 'Barueri',
    'São Paulo — Zona Norte', 'São Paulo — Zona Oeste', 'São Paulo — Centro', 'São Paulo — Zona Leste', 'São Paulo — Zona Sul'
  ];
  const semAcento = t => String(t || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();
  function preencherCidades(atual) {
    const usados = new Set(unidades.filter(u => u.id !== editando).map(u => semAcento(u.nome)));
    $('uCidades').innerHTML = CIDADES.filter(c => !usados.has(semAcento(c)) || semAcento(c) === semAcento(atual))
      .map(c => `<option value="${esc(c)}"></option>`).join('');
  }

  const nomeServico = id => { const s = servicos.find(x => (x.id || x.Id) === id); return s ? rotuloServico(s) : id; };

  function desenhar() {
    const ativas = unidades.filter(u => u.ativa);
    $('kpiAtivas').textContent = ativas.length;
    $('kpiAtivasMeta').textContent = unidades.length > ativas.length ? `${unidades.length - ativas.length} inativa(s)` : 'Todas em operação';
    $('kpiCapacidade').textContent = ativas.reduce((s, u) => s + Number(u.capacidade || 1), 0);
    $('kpiFuncionarios').textContent = unidades.reduce((s, u) => s + (u.funcionarios || []).length, 0);
    $('kpiFuturos').textContent = unidades.reduce((s, u) => s + Number(u.agendamentosFuturos || 0), 0);

    $('vazio').style.display = unidades.length ? 'none' : 'block';
    $('grade').innerHTML = unidades.map(u => {
      const lista = u.servicos || [];
      const func = u.funcionarios || [];
      return `
      <article class="card un-card${u.ativa ? '' : ' inativa'}">
        <div class="un-topo">
          <div><h2>${esc(u.nome)}</h2><span class="small-note">${esc(u.endereco || 'Endereço não informado')}</span></div>
          ${u.ativa ? '<span class="badge badge-success">Ativa</span>' : '<span class="badge badge-neutral">Inativa</span>'}
        </div>
        <dl class="un-linhas">
          <div><dt>Telefone</dt><dd>${esc(u.telefone || '—')}</dd></div>
          <div><dt>Por horário</dt><dd>${u.capacidade} atendimento(s)</dd></div>
          <div><dt>Horário</dt><dd>${esc(u.horarioFuncionamento || '—')}</dd></div>
          <div><dt>Agenda futura</dt><dd>${u.agendamentosFuturos} agendamento(s)</dd></div>
        </dl>
        <div><span class="small-note">Serviços</span>
          <div class="un-chips">${lista.length ? lista.map(id => `<span class="chip">${esc(nomeServico(id))}</span>`).join('') : '<span class="chip">Todos os serviços</span>'}</div></div>
        <div><span class="small-note">Funcionários</span>
          <div class="un-chips">${func.length ? func.map(f => `<span class="chip">${esc(f.nome)}${f.ativo ? '' : ' (inativo)'}</span>`).join('') : '<span class="small-note">Nenhum — vincule em Usuários Administrativos (perfil Funcionário).</span>'}</div></div>
        <div class="un-acoes">
          ${pode('editar') ? `<button class="btn btn-outline btn-sm" type="button" data-editar="${esc(u.id)}">Editar</button>
          <button class="btn btn-outline btn-sm" type="button" data-ativa="${esc(u.id)}">${u.ativa ? 'Desativar' : 'Ativar'}</button>` : ''}
        </div>
      </article>`;
    }).join('');
  }

  async function carregar() {
    erro('erro', '');
    try {
      const d = await api('/admin/unidades?token=' + encodeURIComponent(token()));
      unidades = d.unidades || [];
      servicos = d.servicos || [];
      desenhar();
    } catch (e) {
      console.error('[LanePets] Falha ao carregar unidades:', e);
      erro('erro', e.message);
    }
  }

  function abrir(u) {
    editando = u ? u.id : '';
    $('tituloModal').textContent = u ? `Editar ${u.nome}` : 'Nova unidade';
    $('uNome').value = u ? u.nome : '';
    $('uEndereco').value = u ? (u.endereco || '') : '';
    $('uTelefone').value = u ? (u.telefone || '') : '';
    $('uHorario').value = u ? (u.horarioFuncionamento || '') : '';
    $('uCapacidade').value = u ? u.capacidade : 1;
    preencherCidades(u ? u.nome : '');
    const marcados = new Set(u ? (u.servicos || []) : []);
    $('uServicos').innerHTML = servicos.length ? servicos.map(s => {
      const id = s.id || s.Id;
      return `<label><input type="checkbox" value="${esc(id)}" ${marcados.has(id) ? 'checked' : ''}> ${esc(rotuloServico(s))}</label>`;
    }).join('') : '<span class="small-note">Nenhum serviço cadastrado.</span>';
    erro('erroForm', '');
    $('modalUnidade').classList.add('ativo');
    $('uNome').focus();
  }
  const fechar = () => $('modalUnidade').classList.remove('ativo');

  $('formUnidade').addEventListener('submit', async e => {
    e.preventDefault();
    erro('erroForm', '');
    const dados = {
      nome: $('uNome').value.trim(),
      endereco: $('uEndereco').value.trim(),
      telefone: $('uTelefone').value.trim(),
      horarioFuncionamento: $('uHorario').value.trim(),
      capacidade: Number($('uCapacidade').value),
      servicos: [...$('uServicos').querySelectorAll('input:checked')].map(c => c.value)
    };
    if (dados.nome.length < 3) return erro('erroForm', 'Informe o nome da unidade (mínimo 3 letras).');
    if (!Number.isInteger(dados.capacidade) || dados.capacidade < 1 || dados.capacidade > 20) return erro('erroForm', 'A capacidade deve ficar entre 1 e 20 atendimentos por horário.');
    const botao = $('btnSalvar');
    botao.disabled = true;
    try {
      const r = editando
        ? await enviar('PUT', '/admin/unidades/' + encodeURIComponent(editando), dados)
        : await enviar('POST', '/admin/unidades', dados);
      fechar();
      window.toast && toast(r.message || 'Unidade salva.');
      await carregar();
    } catch (ex) { erro('erroForm', ex.message); }
    finally { botao.disabled = false; }
  });

  document.addEventListener('click', async e => {
    if (e.target.closest('[data-fechar]') || e.target === $('modalUnidade')) { fechar(); return; }
    const ed = e.target.closest('[data-editar]');
    if (ed) { abrir(unidades.find(u => u.id === ed.dataset.editar)); return; }
    const at = e.target.closest('[data-ativa]');
    if (at) {
      const u = unidades.find(x => x.id === at.dataset.ativa);
      if (!u) return;
      const desativando = u.ativa;
      const ok = await confirmarAcao({
        titulo: desativando ? `Desativar ${u.nome}?` : `Ativar ${u.nome}?`,
        texto: desativando
          ? `A unidade some do site e do agendamento, mas todo o histórico é mantido.${u.agendamentosFuturos ? ` Ela tem ${u.agendamentosFuturos} agendamento(s) futuro(s), que continuam registrados.` : ''}`
          : 'A unidade volta a aparecer no site e no agendamento.',
        corBotao: desativando ? 'btn-danger' : 'btn-primary',
        textoBotao: desativando ? 'Desativar' : 'Ativar'
      });
      if (!ok) return;
      try {
        const r = await enviar('POST', '/admin/unidades/' + encodeURIComponent(u.id) + '/ativa', { ativa: !u.ativa });
        window.toast && toast(r.message, desativando && r.agendamentosFuturos ? 'warning' : 'success', 6000);
        await carregar();
      } catch (ex) { window.toast ? toast(ex.message, 'error', 6000) : alert(ex.message); }
    }
  });
  document.addEventListener('keydown', e => { if (e.key === 'Escape') fechar(); });

  $('btnNova') && $('btnNova').addEventListener('click', () => abrir(null));
  $('btnAtualizar').addEventListener('click', carregar);

  window.LanePermissoes ? window.LanePermissoes.pronto(carregar) : carregar();
})();
