/**
 * LanePets — unidades no painel (item 5 do roadmap, 24/09).
 *
 * Antes cada tela tinha "Franco da Rocha" e "Caieiras" escritos no HTML/JS.
 * Agora a lista vem do banco (GET /api/admin/unidades/lista) e este arquivo e
 * o unico lugar que sabe transformar o que foi gravado ("franco",
 * "Franco da Rocha", "FRANCO") no id da unidade e no nome por extenso.
 *
 *   await LaneUnidades.carregar()          -> [{ id, nome, ativa, capacidade, servicos }]
 *   LaneUnidades.idDe(valorGravado)         -> "franco" | "" (sem unidade)
 *   LaneUnidades.nome(id)                   -> "Franco da Rocha" | "Sem unidade / antigo"
 *   LaneUnidades.capacidade(id)             -> 1..20
 *   LaneUnidades.ativas()                   -> so as ativas
 *   LaneUnidades.preencherSelect(el, { todas, semUnidade, rotuloTodas, somenteAtivas })
 *
 * Sem rede ou antes de carregar, cai nas duas unidades originais — nenhuma
 * tela fica sem opcao de unidade.
 */
(function () {
  const CHAVE_TOKEN = 'lanePetsAuthToken';
  const PADRAO = [
    { id: 'franco', nome: 'Franco da Rocha', ativa: true, capacidade: 1, servicos: [] },
    { id: 'caieiras', nome: 'Caieiras', ativa: true, capacidade: 1, servicos: [] }
  ];
  let lista = PADRAO.slice();
  let minhaUnidade = '';
  let promessa = null;

  const texto = v => String(v == null ? '' : v).normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
  const token = () => { try { return sessionStorage.getItem(CHAVE_TOKEN) || ''; } catch (_) { return ''; } };

  function carregar(forcar) {
    if (promessa && !forcar) return promessa;
    promessa = fetch('/api/admin/unidades/lista?token=' + encodeURIComponent(token()))
      .then(r => r.json())
      .then(corpo => {
        if (corpo && corpo.ok && corpo.data && Array.isArray(corpo.data.unidades) && corpo.data.unidades.length) {
          lista = corpo.data.unidades.map(u => ({
            id: u.id || u.Id, nome: u.nome || u.Nome, ativa: u.ativa !== false && u.Ativa !== false,
            capacidade: Number(u.capacidade || 1), servicos: u.servicos || [],
            /* item 4: funcionarios ativos da unidade (responsavel do agendamento) */
            funcionarios: (u.funcionarios || []).map(f => ({ id: f.id || f.Id, nome: f.nome || f.Nome }))
          }));
          minhaUnidade = corpo.data.minhaUnidade || '';
          /* Funcionario (preso a uma unidade): abas, filtros e selects so
             oferecem a unidade dele. O backend ja recusa as outras com 403;
             aqui so deixa de oferecer o que nao vai funcionar. */
          if (minhaUnidade && lista.some(u => u.id === minhaUnidade)) lista = lista.filter(u => u.id === minhaUnidade);
        }
        return lista;
      })
      .catch(erro => { console.error('[LanePets] Não foi possível carregar as unidades; usando a lista padrão.', erro); return lista; });
    return promessa;
  }

  function idDe(valor) {
    const t = texto(valor);
    if (!t) return '';
    const achada = lista.find(u => texto(u.id) === t || texto(u.nome) === t);
    if (achada) return achada.id;
    // Grafias antigas gravadas antes do cadastro de unidades.
    if (['franco-da-rocha', 'franco_da_rocha'].includes(t)) return 'franco';
    if (t === 'caieira') return 'caieiras';
    return '';
  }

  const achar = id => lista.find(u => u.id === id);
  const nome = id => (achar(id) || {}).nome || (id ? id : 'Sem unidade / antigo');
  const capacidade = id => Math.max(1, Number((achar(id) || {}).capacidade || 1));
  const ativas = () => lista.filter(u => u.ativa);

  function preencherSelect(el, opcoes) {
    if (!el) return;
    const o = Object.assign({ todas: false, semUnidade: false, rotuloTodas: 'Todas as unidades', somenteAtivas: false }, opcoes || {});
    const atual = el.value;
    const base = o.somenteAtivas ? ativas() : lista;
    const esc = v => { const d = document.createElement('div'); d.textContent = v; return d.innerHTML; };
    el.innerHTML =
      (o.todas ? `<option value="todas">${esc(o.rotuloTodas)}</option>` : '') +
      base.map(u => `<option value="${esc(u.id)}">${esc(u.nome)}${u.ativa ? '' : ' (inativa)'}</option>`).join('') +
      (o.semUnidade ? '<option value="sem-unidade">Sem unidade / antigos</option>' : '');
    if ([...el.options].some(op => op.value === atual)) el.value = atual;
  }

  window.LaneUnidades = {
    carregar, idDe, nome, capacidade, ativas, preencherSelect,
    lista: () => lista.slice(),
    funcionarios: id => ((achar(id) || {}).funcionarios || []).slice(),
    minhaUnidade: () => minhaUnidade
  };
})();
