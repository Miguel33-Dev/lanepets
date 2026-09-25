/**
 * LanePets — tela Administração > Usuários Administrativos.
 *
 * Toda decisao de seguranca desta tela ja foi tomada no servidor: os botoes
 * daqui apenas evitam que a pessoa tente o que ela sabidamente nao pode. Se
 * alguem forjar a chamada, a API responde 403 e a tela mostra o mesmo recado.
 */
(function () {
  const CHAVE_TOKEN = 'lanePetsAuthToken';
  const $ = id => document.getElementById(id);
  const esc = v => { const d = document.createElement('div'); d.textContent = v == null ? '' : v; return d.innerHTML; };

  const token = () => { try { return sessionStorage.getItem(CHAVE_TOKEN) || ''; } catch (_) { return ''; } };

  let usuarios = [];
  let souAdminGeral = false;
  let meuId = '';
  let editandoId = '';      /* vazio = criando */
  let permissoesDe = null;  /* usuario aberto no modal de permissoes */

  /* ------------------------------------------------------------------
     ICONES
     Inline, no mesmo traco do resto do painel (stroke 1.8, sem preenchimento).
     O projeto nao usa biblioteca de icones e nao ha motivo para adicionar uma:
     sao seis desenhos e eles herdam a cor do botao automaticamente.
     ------------------------------------------------------------------ */
  const svg = miolo => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">${miolo}</svg>`;

  const ICONES = {
    editar:     svg('<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/>'),
    senha:      svg('<rect x="4.5" y="10.3" width="15" height="10.2" rx="2.2"/><path d="M8 10.3V7.4a4 4 0 0 1 8 0v2.9"/><path d="M12 14.2v2.5"/>'),
    permissoes: svg('<circle cx="12" cy="12" r="3"/><path d="M19.4 14.5a1.6 1.6 0 0 0 .32 1.77l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.6 1.6 0 0 0-1.77-.32 1.6 1.6 0 0 0-1 1.47V21a2 2 0 1 1-4 0v-.1A1.6 1.6 0 0 0 9.1 19.4a1.6 1.6 0 0 0-1.77.32l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.6 1.6 0 0 0 .32-1.77 1.6 1.6 0 0 0-1.47-1H3a2 2 0 1 1 0-4h.1A1.6 1.6 0 0 0 4.6 9.1a1.6 1.6 0 0 0-.32-1.77l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.6 1.6 0 0 0 1.77.32H9a1.6 1.6 0 0 0 1-1.47V3a2 2 0 1 1 4 0v.1a1.6 1.6 0 0 0 1 1.47 1.6 1.6 0 0 0 1.77-.32l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.6 1.6 0 0 0-.32 1.77V9a1.6 1.6 0 0 0 1.47 1H21a2 2 0 1 1 0 4h-.1a1.6 1.6 0 0 0-1.5 1Z"/>'),
    desativar:  svg('<circle cx="12" cy="12" r="9"/><path d="M10 9.5v5M14 9.5v5"/>'),
    ativar:     svg('<circle cx="12" cy="12" r="9"/><path d="M10.2 8.6 15.5 12l-5.3 3.4Z"/>'),
    excluir:    svg('<path d="M3.5 6h17"/><path d="M8.5 6V4.5A1.5 1.5 0 0 1 10 3h4a1.5 1.5 0 0 1 1.5 1.5V6"/><path d="M6.5 6.5 7.4 19a2 2 0 0 0 2 1.9h5.2a2 2 0 0 0 2-1.9l.9-12.5"/><path d="M10.5 10.5v6M13.5 10.5v6"/>'),
    menu:       svg('<circle cx="12" cy="5" r="1.4"/><circle cx="12" cy="12" r="1.4"/><circle cx="12" cy="19" r="1.4"/>')
  };

  /* ------------------------------------------------------------------ API */
  async function api(caminho, opcoes) {
    const resposta = await fetch('/api' + caminho, opcoes);
    const corpo = await resposta.json().catch(() => null);

    if (resposta.status === 401) {
      try { sessionStorage.removeItem(CHAVE_TOKEN); } catch (_) {}
      location.replace('admin-login.html');
      throw new Error('Sessão expirada.');
    }
    // 403: autenticado, mas sem permissao. A mensagem vem pronta do servidor.
    if (!resposta.ok || !corpo || !corpo.ok) {
      throw new Error((corpo && corpo.error) || 'Não foi possível concluir a operação.');
    }
    return corpo.data;
  }

  const enviar = (metodo, caminho, dados) => api(caminho, {
    method: metodo,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(Object.assign({ token: token() }, dados || {}))
  });

  /* -------------------------------------------------------------- LISTAGEM */
  function dataCurta(iso) {
    if (!iso) return '—';
    const d = new Date(iso);
    return isNaN(d) ? '—' : d.toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  /**
   * As acoes disponiveis para UM usuario, ja filtradas pelas regras da tela.
   * Uma lista so, consumida pelos dois formatos: a linha de icones no
   * computador e o menu "⋮" no celular. Assim nao existe a chance de o menu
   * oferecer algo que a linha esconde.
   *
   * Isto e aparencia. O backend recusa cada uma destas acoes por conta
   * propria — apagar um botao daqui pelo DevTools nao libera nada.
   */
  function acoesDe(u) {
    const itens = [];

    // Administrador comum nao mexe em Administrador Geral: o backend recusa,
    // e aqui o botao nem aparece.
    if (u.editavel) {
      itens.push({ acao: 'editar', icone: 'editar', rotulo: 'Editar usuário', curto: 'Editar' });
      itens.push({ acao: 'senha', icone: 'senha', rotulo: 'Alterar senha', curto: 'Alterar senha' });
    }

    // Conceder permissao e privilegio do Administrador Geral, e ninguem
    // altera as proprias permissoes.
    if (souAdminGeral && !u.adminGeral && !u.ehVoce) {
      itens.push({ acao: 'permissoes', icone: 'permissoes', rotulo: 'Gerenciar permissões', curto: 'Permissões' });
    }

    if (u.editavel && !u.ehVoce) {
      itens.push(u.ativo
        ? { acao: 'status', icone: 'desativar', rotulo: 'Desativar usuário', curto: 'Desativar' }
        : { acao: 'status', icone: 'ativar', rotulo: 'Ativar usuário', curto: 'Ativar' });
      itens.push({ acao: 'excluir', icone: 'excluir', rotulo: 'Excluir usuário', curto: 'Excluir', perigo: true });
    }

    return itens;
  }

  function celulaAcoes(u) {
    const itens = acoesDe(u);
    if (!itens.length) return '<span class="small-note">&mdash;</span>';

    const linha = itens.map(i => `
      <button type="button" class="acao acao--${i.acao}" data-acao="${i.acao}" data-id="${u.id}"
              data-dica="${esc(i.rotulo)}" aria-label="${esc(i.rotulo)}">${ICONES[i.icone]}</button>`).join('');

    const lista = itens.map(i => `
      <button type="button" role="menuitem" class="acao-item${i.perigo ? ' acao-item--perigo' : ''}"
              data-acao="${i.acao}" data-id="${u.id}">${ICONES[i.icone]}<span>${esc(i.curto)}</span></button>`).join('');

    return `
      <div class="acoes">
        <div class="acoes-linha">${linha}</div>
        <div class="acoes-compacta">
          <button type="button" class="acao acao--menu" data-menu="${u.id}"
                  aria-label="Ações para ${esc(u.nome)}" aria-haspopup="menu" aria-expanded="false">${ICONES.menu}</button>
          <div class="acoes-lista" role="menu" hidden>${lista}</div>
        </div>
      </div>`;
  }

  /* A dica dos botoes e o menu "⋮" vivem em js/admin-acoes.js, compartilhados
     com as outras tabelas do painel. Aqui so usamos os atalhos. */
  const esconderDica = () => window.LaneAcoes && window.LaneAcoes.esconderDica();
  const fecharMenu = () => window.LaneAcoes && window.LaneAcoes.fecharMenu();

  /* ------------------------------------------------------------------
     CONFIRMACAO
     Substitui o confirm() do navegador pelo modal do proprio painel: diz
     exatamente QUEM sera afetado, e o botao que destroi nao fica colado no
     Cancelar. Devolve uma Promise<boolean>.
     ------------------------------------------------------------------ */
  function confirmar({ titulo, texto, nome, email, aviso, rotuloOk, perigo }) {
    return new Promise(resolve => {
      $('confirmarTitulo').textContent = titulo;
      $('confirmarTexto').textContent = texto || '';
      $('confirmarNome').textContent = nome || '';
      $('confirmarEmail').textContent = email || '';
      $('confirmarAviso').textContent = aviso || '';
      $('confirmarAviso').style.display = aviso ? 'block' : 'none';

      const ok = $('confirmarOk');
      ok.textContent = rotuloOk;
      ok.className = 'btn ' + (perigo ? 'btn-perigo' : 'btn-primary');

      const overlay = $('modalConfirmar');

      function encerrar(resposta) {
        overlay.classList.remove('ativo');
        ok.removeEventListener('click', aoConfirmar);
        overlay.removeEventListener('click', aoCancelar);
        document.removeEventListener('keydown', aoTeclar);
        resolve(resposta);
      }
      const aoConfirmar = () => encerrar(true);
      const aoCancelar = ev => { if (ev.target === overlay || ev.target.closest('[data-fechar]')) encerrar(false); };
      const aoTeclar = ev => { if (ev.key === 'Escape') encerrar(false); };

      ok.addEventListener('click', aoConfirmar);
      overlay.addEventListener('click', aoCancelar);
      document.addEventListener('keydown', aoTeclar);

      overlay.classList.add('ativo');
      ok.focus();
    });
  }

  /** Marca o botao como ocupado enquanto a chamada esta no ar. */
  async function comCarregamento(botao, tarefa) {
    botao.classList.add('carregando');
    botao.setAttribute('aria-busy', 'true');
    try { return await tarefa(); }
    finally {
      botao.classList.remove('carregando');
      botao.removeAttribute('aria-busy');
    }
  }

  function desenhar() {
    const busca = ($('busca').value || '').trim().toLowerCase();
    const status = $('filtroStatus').value;

    const linhas = usuarios.filter(u => {
      if (busca && !(u.nome + ' ' + u.email).toLowerCase().includes(busca)) return false;
      if (status === 'ativo' && !u.ativo) return false;
      if (status === 'inativo' && u.ativo) return false;
      return true;
    });

    $('corpoTabela').innerHTML = linhas.map(u => `
      <tr>
        <td><strong>${esc(u.nome)}</strong>${u.ehVoce ? ' <span class="chip">você</span>' : ''}</td>
        <td>${esc(u.email)}</td>
        <td>${u.adminGeral ? '<span class="badge badge-info">Administrador Geral</span>' : '<span class="badge badge-neutral">Administrador</span>'}</td>
        <td>${u.ativo ? '<span class="badge badge-success">Ativo</span>' : '<span class="badge badge-danger">Inativo</span>'}</td>
        <td>${u.acessoTotal ? '<span class="badge badge-warning">Acesso total</span>' : esc(u.acessoResumo)}</td>
        <td class="small-note">${dataCurta(u.ultimoAcesso)}</td>
        <td>${celulaAcoes(u)}</td>
      </tr>`).join('');

    $('vazio').style.display = linhas.length ? 'none' : 'block';
    $('kpiTotal').textContent = usuarios.length;
    $('kpiAtivos').textContent = usuarios.filter(u => u.ativo).length;
    $('kpiTotalAcesso').textContent = usuarios.filter(u => u.acessoTotal).length;
    $('kpiLimitados').textContent = usuarios.filter(u => !u.acessoTotal).length;
  }

  async function carregar() {
    try {
      const dados = await api('/admin/usuarios?token=' + encodeURIComponent(token()));
      usuarios = dados.usuarios || [];
      souAdminGeral = !!dados.souAdminGeral;
      meuId = dados.meuId || '';
      desenhar();
      carregarAuditoria();
    } catch (e) { alert(e.message); }
  }

  async function carregarAuditoria() {
    try {
      const dados = await api('/admin/usuarios/auditoria?token=' + encodeURIComponent(token()));
      $('corpoAuditoria').innerHTML = (dados.registros || []).map(r => `
        <tr>
          <td class="small-note">${dataCurta(r.dataHora)}</td>
          <td>${esc(r.autorEmail)}</td>
          <td>${esc(r.acao)}</td>
          <td>${esc(r.alvoEmail || '—')}</td>
          <td class="small-note">${esc(r.detalhes)}</td>
        </tr>`).join('') || '<tr><td colspan="5" class="small-note">Nenhum registro ainda.</td></tr>';
    } catch (_) { /* auditoria e complementar: falhar aqui nao quebra a tela */ }
  }

  /* ----------------------------------------------------------- MODAIS base */
  const abrir = id => $(id).classList.add('ativo');
  const fechar = id => $(id).classList.remove('ativo');
  document.querySelectorAll('[data-fechar]').forEach(b =>
    b.addEventListener('click', () => b.closest('.modal-overlay').classList.remove('ativo')));
  document.querySelectorAll('.modal-overlay').forEach(o =>
    o.addEventListener('click', e => { if (e.target === o) o.classList.remove('ativo'); }));

  function erro(id, mensagem) {
    const el = $(id);
    el.textContent = mensagem || '';
    el.style.display = mensagem ? 'block' : 'none';
  }

  /* -------------------------------------------------- NOVO / EDITAR USUARIO */
  function abrirUsuario(u) {
    editandoId = u ? u.id : '';
    $('tituloUsuario').textContent = u ? 'Editar usuário administrativo' : 'Novo usuário administrativo';
    $('campoNome').value = u ? u.nome : '';
    $('campoEmail').value = u ? u.email : '';
    $('campoTelefone').value = u ? (u.telefone || '') : '';
    $('campoStatus').value = u ? (u.ativo ? 'ativo' : 'inativo') : 'ativo';
    $('campoSenha').value = '';
    $('campoSenha2').value = '';
    // Na edicao a senha nao aparece: trocar senha e outra acao, com registro proprio.
    $('blocoSenha').style.display = u ? 'none' : 'block';
    $('campoStatus').closest('.field').style.display = u ? 'none' : 'block';
    $('avisoSemPermissao').style.display = u ? 'none' : 'block';
    $('btnSalvarUsuario').textContent = u ? 'Salvar alterações' : 'Criar usuário';
    erro('erroUsuario', '');
    abrir('modalUsuario');
    $('campoNome').focus();
  }

  $('formUsuario').addEventListener('submit', async e => {
    e.preventDefault();
    erro('erroUsuario', '');
    const dados = {
      nome: $('campoNome').value.trim(),
      email: $('campoEmail').value.trim(),
      telefone: $('campoTelefone').value.trim()
    };
    try {
      if (editandoId) {
        await enviar('PUT', '/admin/usuarios/' + encodeURIComponent(editandoId), dados);
      } else {
        if ($('campoSenha').value !== $('campoSenha2').value) throw new Error('A confirmação de senha não confere.');
        dados.senha = $('campoSenha').value;
        dados.confirmarSenha = $('campoSenha2').value;
        dados.ativo = $('campoStatus').value === 'ativo';
        await enviar('POST', '/admin/usuarios', dados);
      }
      fechar('modalUsuario');
      await carregar();
    } catch (ex) { erro('erroUsuario', ex.message); }
  });

  /* ------------------------------------------------------------ PERMISSOES */
  /* ------------------------------------------------------------------
     Icone e descricao de cada modulo. Vive no frontend de proposito: e
     apresentacao, nao regra. O backend continua mandando apenas chave,
     rotulo, grupo e as quatro acoes — a lista de modulos permitida segue
     sendo a dele, e um modulo novo que apareca por la aparece aqui com o
     icone generico, sem quebrar nada.
     ------------------------------------------------------------------ */
  const MODULOS_UI = {
    dashboard:     { d: 'Indicadores e visão geral do negócio',   i: '<path d="M3 13h8V3H3v10Zm0 8h8v-6H3v6Zm10 0h8V11h-8v10Zm0-18v6h8V3h-8Z"/>' },
    agendamentos:  { d: 'Agenda de banho, tosa e serviços',       i: '<rect x="3" y="4.5" width="18" height="16.5" rx="2.5"/><path d="M16 2.5v4M8 2.5v4M3 10h18"/>' },
    pedidos:       { d: 'Pedidos feitos pelos clientes na loja',  i: '<circle cx="9" cy="20" r="1.4"/><circle cx="18" cy="20" r="1.4"/><path d="M2.5 3h2.2l2.3 11.2a1.6 1.6 0 0 0 1.6 1.3h8.7a1.6 1.6 0 0 0 1.6-1.25L21 7H6"/>' },
    clientes:      { d: 'Cadastro, contato e histórico',          i: '<path d="M16 20v-1.8a3.6 3.6 0 0 0-3.6-3.6H6.6A3.6 3.6 0 0 0 3 18.2V20"/><circle cx="9.5" cy="7.5" r="3.4"/><path d="M21 20v-1.8a3.6 3.6 0 0 0-2.7-3.48"/>' },
    pets:          { d: 'Pets cadastrados e pacotes contratados', i: '<circle cx="6.2" cy="9" r="1.9"/><circle cx="11" cy="6.4" r="1.9"/><circle cx="15.8" cy="9" r="1.9"/><path d="M11 13.4c-3.1 0-5.6 2-5.6 4.4 0 1.5 1.4 2.2 2.8 1.7.85-.3 1.8-.45 2.8-.45s1.95.15 2.8.45c1.4.5 2.8-.2 2.8-1.7 0-2.4-2.5-4.4-5.6-4.4Z"/>' },
    produtos:      { d: 'Catálogo, preços e estoque',             i: '<path d="m3 8 9-5 9 5-9 5-9-5Z"/><path d="M3 8v8l9 5 9-5V8"/><path d="M12 13v8"/>' },
    servicos:      { d: 'Serviços oferecidos e valores',          i: '<path d="M8.5 3.5h7l-1 4.2a5.5 5.5 0 0 1 3 4.9v6.4a1.5 1.5 0 0 1-1.5 1.5h-8A1.5 1.5 0 0 1 6.5 19v-6.4a5.5 5.5 0 0 1 3-4.9Z"/><path d="M6.5 14h11"/>' },
    seguros:       { d: 'Planos e contratos do Seguro Pet',       i: '<path d="M12 3 4.5 6v6c0 4.5 3.1 8.2 7.5 9.3 4.4-1.1 7.5-4.8 7.5-9.3V6Z"/><path d="m9 12 2.2 2.2L15.5 10"/>' },
    avaliacoes:    { d: 'Depoimentos publicados no site',         i: '<path d="m12 3.6 2.6 5.3 5.9.85-4.25 4.15 1 5.85L12 17l-5.25 2.75 1-5.85L3.5 9.75l5.9-.85Z"/>' },
    pagamentos:    { d: 'Entradas, saídas e recebimentos',        i: '<rect x="2.5" y="5.5" width="19" height="13" rx="2.5"/><path d="M2.5 10h19"/><path d="M6.5 14.5h4"/>' },
    relatorios:    { d: 'Relatórios e fechamentos do período',    i: '<path d="M3.5 3.5v17h17"/><path d="M18 16.5V9M13 16.5V5.5M8 16.5v-4"/>' },
    usuarios:      { d: 'Contas administrativas e permissões',    i: '<path d="M16 20v-1.8a3.6 3.6 0 0 0-3.6-3.6H6.6A3.6 3.6 0 0 0 3 18.2V20"/><circle cx="9.5" cy="7.5" r="3.4"/><path d="M19 11.5v3M20.5 13h-3"/>' },
    configuracoes: { d: 'Ajustes gerais e importação de dados',   i: '<circle cx="12" cy="12" r="3"/><path d="M12 2.5v3M12 18.5v3M21.5 12h-3M5.5 12h-3M18.7 5.3l-2.1 2.1M7.4 16.6l-2.1 2.1M18.7 18.7l-2.1-2.1M7.4 7.4 5.3 5.3"/>' }
  };
  const ICONE_GENERICO = '<rect x="4" y="4" width="16" height="16" rx="3"/>';

  const ACOES = ['visualizar', 'criar', 'editar', 'excluir'];
  const ROTULO_ACAO = { visualizar: 'Visualizar', criar: 'Criar', editar: 'Editar', excluir: 'Excluir' };

  /**
   * Um card por modulo. O interruptor continua sendo
   * <input type="checkbox" data-modulo data-acao> — a funcao que salva le
   * exatamente o mesmo seletor de antes, entao a troca e so de casca.
   */
  function linhaModulo(m) {
    const ui = MODULOS_UI[m.chave] || {};
    const icone = `<span class="perm-ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ui.i || ICONE_GENERICO}</svg></span>`;

    // "Usuários Administrativos" aparece, mas travado: e a area do
    // Administrador Geral e nao se delega. Mostrar travado explica a regra
    // melhor do que esconder a linha — e o backend descarta esse modulo de
    // qualquer jeito, mesmo se a requisicao vier forjada.
    if (m.somenteGeral) {
      return `
        <article class="perm-card travado">
          <div class="perm-card-topo">
            ${icone}
            <div class="perm-card-txt"><strong>${esc(m.rotulo)}</strong><span>${esc(ui.d || '')}</span></div>
          </div>
          <p class="perm-travado-nota">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="4.5" y="10.3" width="15" height="10.2" rx="2.2"/><path d="M8 10.3V7.4a4 4 0 0 1 8 0"/></svg>
            Exclusivo do Administrador Geral
          </p>
        </article>`;
    }

    const linhas = ACOES.map(acao => `
      <label class="perm-linha">
        <span>${ROTULO_ACAO[acao]}</span>
        <span class="chave">
          <input type="checkbox" data-modulo="${m.chave}" data-acao="${acao}" ${m[acao] ? 'checked' : ''}
                 aria-label="${ROTULO_ACAO[acao]} em ${esc(m.rotulo)}">
          <span class="chave-trilho"></span>
        </span>
      </label>`).join('');

    return `
      <article class="perm-card" data-card="${m.chave}">
        <div class="perm-card-topo">
          ${icone}
          <div class="perm-card-txt"><strong>${esc(m.rotulo)}</strong><span>${esc(ui.d || '')}</span></div>
          <span class="perm-conta" data-conta="${m.chave}">0</span>
        </div>
        <div>${linhas}</div>
      </article>`;
  }

  /** Caixas que o usuario realmente pode mexer (exclui as travadas). */
  const caixasPermissao = () => $('listaModulos').querySelectorAll('input[type=checkbox]');

  /**
   * Recalcula tudo que e derivado do estado das caixas: o contador de cada
   * card, o realce do card e o resumo do topo. Uma funcao so, chamada em
   * toda mudanca — assim a tela nunca mostra numero velho.
   */
  function atualizarResumo() {
    const cards = $('listaModulos').querySelectorAll('.perm-card[data-card]');
    let concedidas = 0, modulosAtivos = 0;

    cards.forEach(card => {
      const marcadas = card.querySelectorAll('input[type=checkbox]:checked').length;
      concedidas += marcadas;
      if (marcadas) modulosAtivos++;
      card.classList.toggle('ativo', marcadas > 0);
      card.querySelector('[data-conta]').textContent = marcadas;
    });

    const total = $('permAcessoTotal').checked;
    $('permResumo').textContent = total
      ? 'Acesso total concedido'
      : concedidas === 0 ? 'Nenhuma permissão concedida'
      : `${concedidas} permiss${concedidas === 1 ? 'ão concedida' : 'ões concedidas'}`;
    $('permResumoModulos').textContent = total
      ? `Todos os ${cards.length} módulos liberados`
      : `${modulosAtivos} de ${cards.length} módulos`;
  }

  /* Acesso total ligado deixa a grade em segundo plano: o que vale naquele
     momento e o acesso total. Desligar devolve exatamente as marcacoes
     anteriores — elas nunca foram perdidas, so ficaram suspensas. */
  function aplicarAcessoTotal() {
    const total = $('permAcessoTotal').checked;
    $('listaModulos').classList.toggle('suspensa', total);
    $('permTotalCartao').classList.toggle('ligado', total);
    caixasPermissao().forEach(c => { c.disabled = total; });
    $('btnMarcarTodas').disabled = total;
    $('btnLimparTodas').disabled = total;
    atualizarResumo();
  }

  function iniciais(nome) {
    const partes = String(nome || '').trim().split(/\s+/);
    return ((partes[0] || 'L')[0] + (partes[1] || partes[0] || 'P')[0]).toUpperCase();
  }

  async function abrirPermissoes(id) {
    try {
      const dados = await api('/admin/usuarios/' + encodeURIComponent(id) + '/permissoes?token=' + encodeURIComponent(token()));
      permissoesDe = dados.usuario;

      const daLista = usuarios.find(u => u.id === id) || {};
      $('permIniciais').textContent = iniciais(dados.usuario.nome);
      $('permNome').textContent = dados.usuario.nome;
      $('permEmail').textContent = dados.usuario.email;
      $('permSelos').innerHTML =
        `<span class="badge ${dados.usuario.adminGeral ? 'badge-info' : 'badge-neutral'}">${esc(dados.usuario.adminGeral ? 'Administrador Geral' : 'Administrador')}</span>` +
        (daLista.ativo === undefined ? ''
          : daLista.ativo ? '<span class="badge badge-success">Ativo</span>'
                          : '<span class="badge badge-danger">Inativo</span>');

      $('permAcessoTotal').checked = !!dados.acessoTotal;

      const grupos = {};
      (dados.modulos || []).forEach(m => { (grupos[m.grupo] = grupos[m.grupo] || []).push(m); });
      $('listaModulos').innerHTML = Object.entries(grupos).map(([grupo, itens]) =>
        `<p class="perm-grupo">${esc(grupo)}</p>` + itens.map(linhaModulo).join('')
      ).join('');

      aplicarAcessoTotal();
      erro('erroPermissoes', '');
      abrir('modalPermissoes');
    } catch (e) { alert(e.message); }
  }

  $('permAcessoTotal').addEventListener('change', aplicarAcessoTotal);
  $('listaModulos').addEventListener('change', atualizarResumo);

  /* Selecionar / limpar todas mexem SO nos controles da tela. Nada vai para o
     banco antes de "Salvar permissões" — dá para experimentar à vontade e
     fechar no Cancelar sem ter mudado nada. */
  $('btnMarcarTodas').addEventListener('click', () => {
    caixasPermissao().forEach(c => { c.checked = true; });
    atualizarResumo();
  });
  $('btnLimparTodas').addEventListener('click', () => {
    caixasPermissao().forEach(c => { c.checked = false; });
    atualizarResumo();
  });

  $('btnSalvarPermissoes').addEventListener('click', async () => {
    if (!permissoesDe) return;
    erro('erroPermissoes', '');

    const mapa = {};
    caixasPermissao().forEach(c => {
      const chave = c.dataset.modulo;
      mapa[chave] = mapa[chave] || { chave, visualizar: false, criar: false, editar: false, excluir: false };
      mapa[chave][c.dataset.acao] = c.checked;
    });

    const acessoTotal = $('permAcessoTotal').checked;
    const concedidas = Object.values(mapa).reduce((soma, m) => soma + ACOES.filter(a => m[a]).length, 0);

    const confirmado = await confirmar({
      titulo: 'Salvar alterações?',
      texto: 'Você está alterando as permissões de:',
      nome: permissoesDe.nome,
      email: permissoesDe.email,
      aviso: acessoTotal
        ? 'Acesso total: este administrador passa a enxergar todos os módulos do painel.'
        : concedidas === 0
          ? 'Nenhuma permissão será concedida — este administrador entra no painel sem acesso a módulo nenhum.'
          : `${concedidas} permiss${concedidas === 1 ? 'ão será concedida' : 'ões serão concedidas'}.`,
      rotuloOk: 'Salvar alterações'
    });
    if (!confirmado) return;

    const botao = $('btnSalvarPermissoes');
    botao.classList.add('carregando');
    botao.disabled = true;
    botao.textContent = 'Salvando';
    try {
      await enviar('PUT', '/admin/usuarios/' + encodeURIComponent(permissoesDe.id) + '/permissoes', {
        acessoTotal,
        modulos: Object.values(mapa)
      });
      fechar('modalPermissoes');
      await carregar();
    } catch (e) {
      erro('erroPermissoes', e.message);
    } finally {
      botao.classList.remove('carregando');
      botao.disabled = false;
      botao.textContent = 'Salvar permissões';
    }
  });

  /* ----------------------------------------------------------------- SENHA */
  let senhaDe = '';
  function abrirSenha(u) {
    senhaDe = u.id;
    $('senhaUsuario').textContent = u.nome + ' · ' + u.email;
    $('novaSenha').value = '';
    $('novaSenha2').value = '';
    erro('erroSenha', '');
    abrir('modalSenha');
  }

  $('formSenha').addEventListener('submit', async e => {
    e.preventDefault();
    erro('erroSenha', '');
    try {
      if ($('novaSenha').value !== $('novaSenha2').value) throw new Error('A confirmação de senha não confere.');
      await enviar('POST', '/admin/usuarios/' + encodeURIComponent(senhaDe) + '/senha', {
        senha: $('novaSenha').value, confirmarSenha: $('novaSenha2').value
      });
      fechar('modalSenha');
      await carregar();
    } catch (ex) { erro('erroSenha', ex.message); }
  });

  /* ----------------------------------------------------------------- ACOES */
  document.addEventListener('click', async e => {
    const botao = e.target.closest('button[data-acao]');
    if (!botao || !botao.dataset.id) return;

    const u = usuarios.find(x => x.id === botao.dataset.id);
    if (!u) return;

    fecharMenu();
    esconderDica();

    try {
      if (botao.dataset.acao === 'editar') return abrirUsuario(u);
      if (botao.dataset.acao === 'senha') return abrirSenha(u);
      if (botao.dataset.acao === 'permissoes') return abrirPermissoes(u.id);

      if (botao.dataset.acao === 'status') {
        const desativando = u.ativo;
        const confirmado = await confirmar({
          titulo: desativando ? 'Desativar usuário?' : 'Ativar usuário?',
          texto: desativando
            ? 'Esta conta deixa de acessar o painel imediatamente e as sessões abertas dela são encerradas.'
            : 'Esta conta volta a acessar o painel com as permissões que já possui.',
          nome: u.nome, email: u.email,
          rotuloOk: desativando ? 'Desativar usuário' : 'Ativar usuário',
          perigo: desativando
        });
        if (!confirmado) return;
        await comCarregamento(botao, () =>
          enviar('POST', '/admin/usuarios/' + encodeURIComponent(u.id) + '/status', { ativo: !u.ativo }));
        return carregar();
      }

      if (botao.dataset.acao === 'excluir') {
        const confirmado = await confirmar({
          titulo: 'Excluir usuário?',
          texto: 'Você está prestes a excluir:',
          nome: u.nome, email: u.email,
          aviso: 'As permissões dele serão removidas junto. Essa ação não poderá ser desfeita.',
          rotuloOk: 'Excluir usuário',
          perigo: true
        });
        if (!confirmado) return;
        await comCarregamento(botao, () =>
          api('/admin/usuarios/' + encodeURIComponent(u.id) + '?token=' + encodeURIComponent(token()), { method: 'DELETE' }));
        return carregar();
      }
    } catch (ex) { alert(ex.message); }
  });

  $('btnNovo') && $('btnNovo').addEventListener('click', () => abrirUsuario(null));
  $('btnAtualizar').addEventListener('click', carregar);
  $('busca').addEventListener('input', desenhar);
  $('filtroStatus').addEventListener('change', desenhar);
  /* O botao "Sair" ja e tratado pelo ui-kit.js, que e carregado em todas as
     telas do painel. Nao duplicamos o handler aqui. */

  // Espera as permissoes chegarem: se este usuario nao pode ver o modulo,
  // admin-permissoes.js ja tera redirecionado antes daqui rodar.
  window.LanePermissoes ? window.LanePermissoes.pronto(carregar) : carregar();
})();
