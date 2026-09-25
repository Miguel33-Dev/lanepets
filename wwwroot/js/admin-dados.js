/* =============================================================================
   LanePets — Camada de dados do painel administrativo.

   Antes, cada tela do painel lia `localStorage`. Isso mantinha o admin cego
   para tudo que o cliente fazia, porque a area do cliente grava no SQLite pela
   API. Este arquivo passa a ser a UNICA fonte de dados do painel:

       lanepets.db  ->  API  ->  LaneAdmin  ->  telas do painel

   Tambem abre o canal SignalR: quando o cliente faz qualquer coisa, o servidor
   avisa e a tela se atualiza sozinha, sem F5.
   ============================================================================= */
(function () {
  const BASE = '/api';
  const CHAVE_TOKEN = 'lanePetsAuthToken';

  const token = () => { try { return sessionStorage.getItem(CHAVE_TOKEN) || ''; } catch (_) { return ''; } };

  function semSessao() {
    const destino = encodeURIComponent(location.pathname.replace(/^\//, '') || 'index.html');
    location.href = `admin-login.html?retorno=%2F${destino}`;
  }

  /* --------------------------------------------------------------------- */
  /* Leitura                                                                */
  /* --------------------------------------------------------------------- */
  async function get(caminho, parametros = {}) {
    const url = new URL(`${BASE}/${String(caminho).replace(/^\//, '')}`, location.origin);
    url.searchParams.set('token', token());
    Object.entries(parametros).forEach(([chave, valor]) => {
      if (valor !== undefined && valor !== null && valor !== '') url.searchParams.set(chave, valor);
    });

    let resposta;
    try { resposta = await fetch(url.toString()); }
    catch (_) {
      const erro = new Error('Não foi possível falar com o servidor. Verifique se o LanePets está rodando.');
      erro.semConexao = true;
      throw erro;
    }

    const texto = await resposta.text();
    let corpo;
    try { corpo = JSON.parse(texto); }
    catch (_) {
      console.error(`[LanePets] GET ${url.pathname} -> HTTP ${resposta.status}`, texto.slice(0, 400));
      throw new Error(`O servidor retornou um erro inesperado (HTTP ${resposta.status}).`);
    }

    if (resposta.status === 401) { semSessao(); throw new Error('Sessão administrativa expirada.'); }
    if (!resposta.ok || !corpo.ok) {
      console.error(`[LanePets] GET ${url.pathname} -> HTTP ${resposta.status}`, corpo);
      throw new Error(corpo.error || 'Não foi possível carregar os dados.');
    }
    return corpo.data;
  }

  async function post(caminho, dados = {}) {
    const resposta = await fetch(`${BASE}/${String(caminho).replace(/^\//, '')}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ...dados, token: token() })
    });
    const texto = await resposta.text();
    let corpo;
    try { corpo = JSON.parse(texto); }
    catch (_) {
      console.error(`[LanePets] POST ${caminho} -> HTTP ${resposta.status}`, texto.slice(0, 400));
      throw new Error(`O servidor retornou um erro inesperado (HTTP ${resposta.status}).`);
    }
    if (resposta.status === 401) { semSessao(); throw new Error('Sessão administrativa expirada.'); }
    if (!resposta.ok || !corpo.ok) throw new Error(corpo.error || 'Não foi possível concluir a operação.');
    return corpo.data;
  }

  /* --------------------------------------------------------------------- */
  /* Atalhos por entidade — todos leem o mesmo lanepets.db                  */
  /* --------------------------------------------------------------------- */
  const listaDe = resposta => Array.isArray(resposta) ? resposta : (resposta && resposta.items) || [];

  const api = {
    get,
    post,
    token,
    resumo: filtros => get('admin/resumo', filtros),
    agendamentos: filtros => get('agendamentos', { limite: 2000, ...filtros }).then(r => r.agendamentos || []),
    clientes: () => get('clientes', { limite: 2000 }).then(listaDe),
    pets: () => get('pets', { limite: 2000 }).then(listaDe),
    servicos: () => get('servicos', { limite: 2000 }).then(listaDe),
    produtos: () => get('admin/produtos').then(listaDe),
    lancamentos: filtros => get('admin/entradas-saidas', filtros).then(listaDe),
    pacotes: () => get('admin/pacotes').then(listaDe),
    pedidos: () => get('admin/pedidos').then(listaDe),
    avaliacoes: () => get('admin/depoimentos').then(listaDe),
    seguros: () => get('admin/seguros/solicitacoes').then(listaDe),
    importar: (dados, simular) => post('admin/importar', { ...dados, simular: !!simular })
  };

  /* --------------------------------------------------------------------- */
  /* Tempo real: o servidor avisa, a tela recarrega                         */
  /* --------------------------------------------------------------------- */
  const ouvintes = [];
  let conexao = null;
  let agendado = null;

  function avisar(evento) {
    /* Varios avisos podem chegar juntos (um cadastro mexe em cliente + pet).
       Agrupa em uma unica recarga para nao martelar a API. */
    clearTimeout(agendado);
    agendado = setTimeout(() => ouvintes.forEach(fn => { try { fn(evento); } catch (e) { console.error('[LanePets] ouvinte falhou:', e); } }), 250);
  }

  function conectar() {
    if (conexao || !window.signalR) return;
    conexao = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/lanepets')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
      .build();

    conexao.on('dadosAlterados', evento => {
      console.info('[LanePets] tempo real:', evento);
      pintarStatus('ao-vivo', 'Ao vivo');
      avisar(evento);
    });
    conexao.onreconnecting(() => pintarStatus('reconectando', 'Reconectando…'));
    conexao.onreconnected(() => { pintarStatus('ao-vivo', 'Ao vivo'); avisar({ entidade: 'reconexao' }); });
    conexao.onclose(() => pintarStatus('offline', 'Sem tempo real'));

    conexao.start()
      .then(() => pintarStatus('ao-vivo', 'Ao vivo'))
      .catch(erro => {
        console.warn('[LanePets] SignalR indisponível, seguindo sem tempo real:', erro);
        pintarStatus('offline', 'Sem tempo real');
      });
  }

  /* Selo discreto no topo da pagina mostrando o estado da conexao. */
  function pintarStatus(estado, texto) {
    let selo = document.getElementById('lp-tempo-real');
    if (!selo) {
      const destino = document.querySelector('.topbar-right');
      if (!destino) return;
      selo = document.createElement('span');
      selo.id = 'lp-tempo-real';
      selo.className = 'lp-live';
      destino.prepend(selo);
    }
    selo.dataset.estado = estado;
    selo.innerHTML = `<i></i><span class="hide-mobile">${texto}</span>`;
    selo.title = estado === 'ao-vivo'
      ? 'Conectado ao servidor: a tela se atualiza sozinha quando o cliente faz algo.'
      : 'Sem canal de tempo real. Use o botão de atualizar para recarregar.';
  }

  /** Registra uma função para rodar sempre que algo mudar no banco. */
  function aoMudar(callback) {
    ouvintes.push(callback);
    conectar();
  }

  /* Uma tela que ficou escondida pode ter perdido avisos: ao voltar, recarrega. */
  document.addEventListener('visibilitychange', () => { if (!document.hidden) avisar({ entidade: 'retomada' }); });

  window.LaneAdmin = { ...api, aoMudar, conectar };
})();
