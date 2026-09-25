/* =============================================================================
   LaneBusca — padrão único de busca e filtros do painel (item 16 do roadmap).

   Usado por Clientes, Pets, Produtos, Pedidos e Pagamentos. Regras:
   - sem diferença de acento e maiúscula ("racao" acha "Ração");
   - várias palavras = todas precisam aparecer ("rex banho");
   - número é comparado só pelos dígitos ("11 9999" acha "(11) 99999-0000");
   - filtros ficam na URL (?busca=...&status=...), então dá para voltar,
     recarregar ou abrir a tela já filtrada a partir de um link (ex.: Dashboard);
   - contador "Mostrando X de Y" e estado vazio com "Limpar filtros";
   - Esc limpa a busca que está em foco; "/" leva o cursor para a busca.
   ============================================================================= */
(function () {
  'use strict';

  function normal(v) {
    return String(v == null ? '' : v)
      .normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
  }

  function digitos(v) { return String(v == null ? '' : v).replace(/\D/g, ''); }

  /** true quando TODAS as palavras do termo aparecem em algum dos campos. */
  function combina(termo, campos) {
    var palavras = normal(termo).split(/\s+/).filter(Boolean);
    if (!palavras.length) return true;
    var texto = (campos || []).map(normal).join(' \u0001 ');
    var soDigitos = (campos || []).map(digitos).join(' ');
    return palavras.every(function (p) {
      if (texto.indexOf(p) !== -1) return true;
      var d = digitos(p);
      return d.length >= 3 && d.length === p.replace(/[\s().-]/g, '').length && soDigitos.indexOf(d) !== -1;
    });
  }

  /* ---- URL ------------------------------------------------------------- */
  /** Preenche os campos a partir da URL. mapa = { parametro: idDoCampo }. */
  function lerUrl(mapa) {
    var q = new URLSearchParams(location.search);
    var algum = false;
    Object.keys(mapa).forEach(function (chave) {
      var campo = document.getElementById(mapa[chave]);
      if (!campo || !q.has(chave)) return;
      var v = q.get(chave);
      if (campo.type === 'checkbox') campo.checked = v === '1' || v === 'true';
      else if (campo.tagName === 'SELECT') {
        var opcao = Array.prototype.find.call(campo.options, function (o) { return normal(o.value) === normal(v); });
        if (!opcao) return;
        campo.value = opcao.value;
      } else campo.value = v;
      algum = true;
    });
    return algum;
  }

  /** Grava os campos na URL sem recarregar (valor vazio/"todas" sai da URL). */
  function gravarUrl(mapa) {
    var q = new URLSearchParams(location.search);
    Object.keys(mapa).forEach(function (chave) {
      var campo = document.getElementById(mapa[chave]);
      if (!campo) return;
      var v = campo.type === 'checkbox' ? (campo.checked ? '1' : '') : String(campo.value || '').trim();
      if (!v || v === 'todas') q.delete(chave); else q.set(chave, v);
    });
    var s = q.toString();
    try { history.replaceState(null, '', location.pathname + (s ? '?' + s : '') + location.hash); } catch (_) {}
  }

  /** Volta os campos ao padrão (texto vazio, select na 1ª opção, checkbox desmarcado). */
  function limpar(ids) {
    ids.forEach(function (id) {
      var campo = document.getElementById(id);
      if (!campo) return;
      if (campo.type === 'checkbox') campo.checked = false;
      else if (campo.tagName === 'SELECT') campo.selectedIndex = 0;
      else campo.value = '';
    });
  }

  /** Quantos filtros estão diferentes do padrão. */
  function ativos(ids) {
    return ids.filter(function (id) {
      var campo = document.getElementById(id);
      if (!campo) return false;
      if (campo.type === 'checkbox') return campo.checked;
      if (campo.tagName === 'SELECT') return campo.selectedIndex > 0;
      return String(campo.value || '').trim() !== '';
    }).length;
  }

  /* ---- Contador e vazio ------------------------------------------------ */
  function contador(el, visiveis, total, singular, plural) {
    if (typeof el === 'string') el = document.getElementById(el);
    if (!el) return;
    var nome = function (n) { return n === 1 ? singular : plural; };
    el.textContent = visiveis === total
      ? total + ' ' + nome(total)
      : 'Mostrando ' + visiveis + ' de ' + total + ' ' + nome(total);
  }

  function esc(v) { var d = document.createElement('div'); d.textContent = v == null ? '' : v; return d.innerHTML; }

  /** Linha de tabela para lista vazia: sem cadastro × filtro sem resultado. */
  function linhaVazia(colunas, total, textos) {
    textos = textos || {};
    var semNada = total === 0;
    var titulo = semNada ? (textos.nenhum || 'Nada cadastrado ainda.') : (textos.filtro || 'Nada encontrado com esses filtros.');
    var botao = semNada ? '' : '<button type="button" class="btn btn-outline btn-sm" data-limpar-filtros style="margin-top:var(--sp-3)">Limpar filtros</button>';
    return '<tr><td colspan="' + colunas + '" style="text-align:center;padding:var(--sp-6) var(--sp-4)">'
      + '<strong>' + esc(titulo) + '</strong>'
      + (semNada ? '' : '<br><span class="small-note">Confira a grafia ou tire algum filtro.</span><br>')
      + botao + '</td></tr>';
  }

  /* ---- Atalhos --------------------------------------------------------- */
  var atalhoLigado = false;
  function atalhos(idBusca, aoLimpar) {
    var campo = document.getElementById(idBusca);
    if (!campo) return;
    campo.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && campo.value) { e.stopPropagation(); campo.value = ''; if (aoLimpar) aoLimpar(); }
    });
    if (atalhoLigado) return;
    atalhoLigado = true;
    document.addEventListener('keydown', function (e) {
      if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey) return;
      var alvo = e.target;
      if (alvo && (alvo.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(alvo.tagName))) return;
      if (document.querySelector('.modal-overlay.open, .modal-overlay.aberto, .modal-overlay[style*="flex"]')) return;
      e.preventDefault();
      campo.focus();
      campo.select();
    });
  }

  /** Atraso para busca que vai ao servidor. */
  function atrasar(fn, ms) {
    var t = null;
    return function () { var a = arguments, self = this; clearTimeout(t); t = setTimeout(function () { fn.apply(self, a); }, ms || 300); };
  }

  window.LaneBusca = {
    normal: normal, digitos: digitos, combina: combina,
    lerUrl: lerUrl, gravarUrl: gravarUrl, limpar: limpar, ativos: ativos,
    contador: contador, linhaVazia: linhaVazia, atalhos: atalhos, atrasar: atrasar
  };
})();
