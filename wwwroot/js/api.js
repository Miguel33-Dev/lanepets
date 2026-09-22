/**
 * Lane Pets — Cliente da API / DataService base.
 *
 * ETAPA M2:
 * - contrato de resposta: { ok, data, timestamp }
 * - token administrativo mantido em sessionStorage
 * - login por POST (senha nunca vai na URL)
 * - rotas GET de dados enviam token na query string porque o
 *   Google Apps Script Web App não expõe headers HTTP ao doGet().
 * - CRUD de produção ainda permanece bloqueado nesta etapa.
 */
(function () {
  const cfg = window.LANE_PETS_CONFIG || {};
  const API_URL = String(cfg.API_URL || '').trim();
  const TOKEN_KEY = 'lanePetsAuthToken';
  const FIN_TOKEN_KEY = 'lanePetsFinanceiroToken';

  function assertConfigured() {
    if (!API_URL || API_URL.includes('COLE_AQUI')) {
      throw new Error('API do Google Apps Script ainda não configurada em js/config.js.');
    }
  }

  function getToken() {
    try { return sessionStorage.getItem(TOKEN_KEY) || ''; }
    catch (_) { return ''; }
  }

  function setToken(token) {
    if (!token) throw new Error('Token de sessão não informado.');
    sessionStorage.setItem(TOKEN_KEY, String(token));
  }

  function clearToken() {
    try { sessionStorage.removeItem(TOKEN_KEY); } catch (_) {}
  }

  function getFinanceiroToken() {
    try { return sessionStorage.getItem(FIN_TOKEN_KEY) || ''; }
    catch (_) { return ''; }
  }

  function setFinanceiroToken(token) {
    if (!token) throw new Error('Token financeiro não informado.');
    sessionStorage.setItem(FIN_TOKEN_KEY, String(token));
  }

  function clearFinanceiroToken() {
    try { sessionStorage.removeItem(FIN_TOKEN_KEY); } catch (_) {}
  }

  async function parseResponse(response) {
    const text = await response.text();
    let envelope;
    try { envelope = JSON.parse(text); }
    catch (_) { throw new Error('Resposta inválida da API: ' + text.slice(0, 300)); }

    if (!envelope || envelope.ok !== true) {
      throw new Error(envelope && envelope.error ? envelope.error : 'Erro desconhecido na API.');
    }

    return envelope.data;
  }

  async function get(action, params = {}) {
    assertConfigured();
    const url = new URL(API_URL + '/' + encodeURIComponent(action), window.location.origin);
    const token = getToken();
    if (token) url.searchParams.set('token', token);
    Object.entries(params).forEach(([k, v]) => { if (v !== undefined && v !== null && v !== '') url.searchParams.set(k, v); });
    return parseResponse(await fetch(url.toString(), { method: 'GET' }));
  }

  async function post(action, data = {}) {
    assertConfigured();
    const body = { action, ...data };
    const token = getToken();
    if (token) body.token = token;
    const response = await fetch(API_URL + '/' + encodeURIComponent(action), { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    return parseResponse(response);
  }

  async function login(senha) {
    if (!senha) throw new Error('Senha obrigatória.');
    const data = await post('login', { senha: String(senha) });
    setToken(data.token);
    return data;
  }

  async function authStatus() {
    const token = getToken();
    if (!token) return { autenticado: false };
    try {
      return await get('auth_status');
    } catch (err) {
      clearToken();
      throw err;
    }
  }

  async function logout() {
    const token = getToken();
    try {
      if (token) await post('logout');
    } finally {
      clearToken();
    }
    return { encerrado: true };
  }

  async function financeiroLogin(senha) {
    if (!getToken()) throw new Error('Faça o login administrativo antes da autorização financeira.');
    if (!senha) throw new Error('Senha financeira obrigatória.');
    const data = await post('financeiro_login', { senha: String(senha) });
    setFinanceiroToken(data.token);
    return data;
  }

  async function financeiroStatus() {
    const token = getFinanceiroToken();
    if (!token) return { autorizado: false };
    const url = new URL(API_URL + '/financeiro_status', window.location.origin);
    url.searchParams.set('financeiro_token', token);
    try {
      return parseResponse(await fetch(url.toString(), { method: 'GET' }));
    } catch (err) {
      clearFinanceiroToken();
      throw err;
    }
  }

  async function financeiroLogout() {
    const token = getFinanceiroToken();
    try {
      if (token) {
        await post('financeiro_logout', { financeiro_token: token });
      }
    } finally {
      clearFinanceiroToken();
    }
    return { encerrado: true };
  }

  window.LaneAPI = {
    // Sessão
    login,
    authStatus,
    logout,
    getToken,
    setToken,
    clearToken,
    getFinanceiroToken,
    setFinanceiroToken,
    clearFinanceiroToken,
    financeiroLogin,
    financeiroStatus,
    financeiroLogout,

    // Leitura contratada
    health: () => get('health'),
    dashboard: params => get('dashboard', params || {}),
    metricas: params => get('metricas', params || {}),
    agendamentos: params => get('agendamentos', params || {}),
    appointments: params => get('appointments', params || {}),
    pets: params => get('pets', params || {}),
    clientes: params => get('clientes', params || {}),
    servicos: params => get('servicos', params || {}),
    unidades: () => get('unidades'),

    // CRUD será liberado somente após as regras de negócio de cada módulo.
    getAll: entity => get(entity),
    getById: (entity, id) => get(entity, { id }),

    // Mantidos apenas como interface futura; não executam CRUD de produção ainda.
    create: () => Promise.reject(new Error('CRUD de produção ainda não liberado.')),
    update: () => Promise.reject(new Error('CRUD de produção ainda não liberado.')),
    remove: () => Promise.reject(new Error('CRUD de produção ainda não liberado.')),
    movement: () => Promise.reject(new Error('Movimentação de estoque ainda não liberada.')),
    report: () => Promise.reject(new Error('Relatórios API ainda não liberados.'))
  };
})();
