/**
 * LanePets — permissoes no painel administrativo.
 *
 * Este arquivo faz UMA coisa: adaptar a tela ao que o usuario pode.
 * Ele esconde item de menu, esconde botao e bloqueia a pagina.
 *
 * O QUE ELE NAO FAZ: proteger dado. Quem protege dado e a API — cada endpoint
 * confere a permissao no servidor e responde 403 por conta propria. Se alguem
 * apagar este script pelo DevTools, os botoes reaparecem e continuam sem
 * funcionar, porque o backend recusa a chamada do mesmo jeito.
 *
 * Como usar em qualquer tela:
 *
 *   <script src="js/admin-permissoes.js"></script>
 *
 *   <button data-permissao="produtos:excluir">Excluir</button>
 *      -> some quando o usuario nao pode excluir produtos
 *
 *   LanePermissoes.pronto(function (p) { if (p.pode('clientes','criar')) ... });
 */
(function () {
  const CHAVE_TOKEN = 'lanePetsAuthToken';

  /* Cada pagina do painel e o modulo que da direito de abri-la. Espelha o
     mapa do AdminAreaGuardMiddleware no backend: se divergirem, quem manda e
     o backend — a tela apenas ficaria mais restritiva ou mais frouxa na
     aparencia, nunca no acesso ao dado. */
  const MODULOS_DA_PAGINA = {
    'index.html': ['pets', 'clientes'],
    'clientes.html': ['clientes'],
    'clientes_pacote.html': ['pets'],
    'agendamentos.html': ['agendamentos'],
    'produtos.html': ['produtos'],
    'entradas_e_saidas.html': ['pagamentos'],
    'dashboard_financeiro.html': ['dashboard'],
    'relatorio.html': ['relatorios'],
    'gestao-publica.html': ['seguros', 'avaliacoes'],
    'migrar-dados.html': ['configuracoes'],
    'pedidos.html': ['pedidos'],
    'usuarios-admin.html': ['usuarios']
  };

  let estado = null;
  const espera = [];

  function token() {
    try { return sessionStorage.getItem(CHAVE_TOKEN) || ''; } catch (_) { return ''; }
  }

  function arquivoDe(href) {
    if (!href) return '';
    const limpo = String(href).split('?')[0].split('#')[0];
    return limpo.substring(limpo.lastIndexOf('/') + 1).toLowerCase();
  }

  function pode(modulo, acao) {
    if (!estado) return false;
    const m = estado.modulos && estado.modulos[modulo];
    // Modulo exclusivo do Administrador Geral: acesso total nao abre.
    if (m && m.somenteGeral) return !!m[acao || 'visualizar'];
    if (estado.acessoIrrestrito) return true;
    return !!(m && m[acao || 'visualizar']);
  }

  function podeVerAlgum(lista) {
    return (lista || []).some(m => pode(m, 'visualizar'));
  }

  /** Perfil AdminGeral, conforme gravado no banco — nunca deduzido do e-mail. */
  function souAdminGeral() {
    return !!(estado && estado.usuario && estado.usuario.adminGeral);
  }

  /* ------------------------------------------------------------------
     MENU
     Itens sem permissao saem do DOM. Um grupo ("Operação", "Catálogo") que
     ficou sem nenhum item sai junto, senao sobraria um titulo solto.
     ------------------------------------------------------------------ */
  function ajustarMenu() {
    const nav = document.querySelector('.sidebar-nav');
    if (!nav) return;

    nav.querySelectorAll('a.nav-item').forEach(link => {
      const modulos = MODULOS_DA_PAGINA[arquivoDe(link.getAttribute('href'))];
      if (modulos && !podeVerAlgum(modulos)) link.remove();
    });

    inserirItemUsuarios(nav);

    nav.querySelectorAll('p.nav-group').forEach(titulo => {
      let irmao = titulo.nextElementSibling;
      let temItem = false;
      while (irmao && !irmao.classList.contains('nav-group')) {
        if (irmao.classList.contains('nav-item')) { temItem = true; break; }
        irmao = irmao.nextElementSibling;
      }
      if (!temItem) titulo.remove();
    });
  }

  /* A area "Administração > Usuários Administrativos" nao existe no HTML das
     telas antigas. Ela e acrescentada aqui, e SO para o Administrador Geral —
     nem um administrador com acesso total ve este item, porque administrar
     administradores nao e uma ferramenta do petshop, e a autoridade sobre os
     outros administradores. O backend recusa a rota do mesmo jeito. */
  function inserirItemUsuarios(nav) {
    if (!souAdminGeral()) return;
    if (nav.querySelector('a[href="usuarios-admin.html"]')) return;

    const grupo = document.createElement('p');
    grupo.className = 'nav-group';
    grupo.textContent = 'Administração';

    const item = document.createElement('a');
    item.className = 'nav-item';
    item.href = 'usuarios-admin.html';
    item.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M16 20v-1.8a3.6 3.6 0 0 0-3.6-3.6H6.6A3.6 3.6 0 0 0 3 18.2V20"/><circle cx="9.5" cy="7.5" r="3.4"/><path d="M19 11.5v3M20.5 13h-3"/></svg><span>Usuários Administrativos</span>';
    if (arquivoDe(location.pathname) === 'usuarios-admin.html') {
      item.classList.add('active');
      item.setAttribute('aria-current', 'page');
    }

    nav.appendChild(grupo);
    nav.appendChild(item);
  }

  /* ------------------------------------------------------------------
     BOTOES E BLOCOS  —  data-permissao="modulo:acao"
     ------------------------------------------------------------------ */
  function ajustarElementos(raiz) {
    (raiz || document).querySelectorAll('[data-permissao]').forEach(el => {
      const [modulo, acao] = String(el.getAttribute('data-permissao')).split(':');
      if (!pode(modulo, acao || 'visualizar')) el.remove();
    });
  }

  function identificarUsuario() {
    const u = estado && estado.usuario;
    if (!u) return;
    document.querySelectorAll('.topbar-user-info strong').forEach(el => { el.textContent = u.nome; });
    document.querySelectorAll('.topbar-user-info span').forEach(el => { el.textContent = u.perfilRotulo; });
    document.querySelectorAll('.topbar-user .avatar').forEach(el => {
      const partes = String(u.nome || 'LP').trim().split(/\s+/);
      el.textContent = ((partes[0] || 'L')[0] + (partes[1] || partes[0] || 'P')[0]).toUpperCase();
    });
  }

  function bloquearPagina() {
    const modulos = MODULOS_DA_PAGINA[arquivoDe(location.pathname)];
    if (!modulos || podeVerAlgum(modulos)) return false;
    location.replace('acesso-negado.html?modulo=' + encodeURIComponent(modulos[0]));
    return true;
  }

  async function carregar() {
    const t = token();
    if (!t) { location.replace('admin-login.html?retorno=' + encodeURIComponent(location.pathname)); return; }

    let resposta;
    try {
      resposta = await fetch('/api/admin/me?token=' + encodeURIComponent(t));
    } catch (_) {
      console.error('[LanePets] não foi possível carregar as permissões.');
      return;
    }

    if (resposta.status === 401) {
      try { sessionStorage.removeItem(CHAVE_TOKEN); } catch (_) {}
      location.replace('admin-login.html?retorno=' + encodeURIComponent(location.pathname));
      return;
    }

    const corpo = await resposta.json().catch(() => null);
    if (!corpo || !corpo.ok) { console.error('[LanePets] permissões indisponíveis.'); return; }

    estado = corpo.data;
    window.LanePermissoes.estado = estado;
    window.LanePermissoes.usuario = estado.usuario;

    if (bloquearPagina()) return;

    ajustarMenu();
    ajustarElementos();
    identificarUsuario();
    espera.splice(0).forEach(fn => { try { fn(window.LanePermissoes); } catch (e) { console.error(e); } });
  }

  window.LanePermissoes = {
    pode,
    podeVerAlgum,
    souAdminGeral,
    ajustarElementos,
    get estadoAtual() { return estado; },
    /** Executa quando as permissoes ja chegaram (ou na hora, se ja chegaram). */
    pronto(fn) { if (estado) fn(window.LanePermissoes); else espera.push(fn); }
  };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', carregar);
  else carregar();
})();
