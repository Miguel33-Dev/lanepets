/* =============================================================================
   LanePets — Ponte entre as telas antigas do painel e o banco de dados.

   PROBLEMA QUE ESTE ARQUIVO RESOLVE
   ---------------------------------
   As telas administrativas mais antigas (agendamentos, clientes, produtos,
   entradas e saidas, pacotes) foram escritas lendo e gravando no localStorage
   do navegador. Isso criava duas verdades: o cliente gravava no lanepets.db
   pela API e o admin lia outra coisa, guardada no navegador de quem estava
   logado. Nada do que o cliente fazia aparecia no painel.

   COMO A PONTE FUNCIONA
   ---------------------
   Este script troca o objeto `localStorage` por um adaptador para as chaves de
   dados do sistema. Quem le, le o banco; quem grava, grava no banco:

       lanepets.db  ->  GET  /api/admin/estado       ->  telas do painel
       telas        ->  POST /api/admin/sync/<lista> ->  lanepets.db

   As chaves que sao so preferencia de tela (filtros, unidade selecionada,
   modo de visualizacao) continuam indo para o localStorage de verdade.

   A carga inicial e sincrona de proposito: o codigo das telas roda durante o
   parse do HTML e ja espera encontrar os dados prontos. Depois dessa primeira
   carga, tudo e assincrono.
   ============================================================================= */
(function () {
  'use strict';

  var CHAVE_TOKEN = 'lanePetsAuthToken';

  /* Chaves cujo conteudo mora no banco. Qualquer outra chave e preferencia
     de tela e continua no navegador. */
  var COLECOES = {
    agendamentos: { rota: 'agendamentos', identidade: ['dono', 'pet', 'dataHora'] },
    pets: { rota: 'pets', identidade: ['dono', 'pet'] },
    servicos: { rota: 'servicos', identidade: ['nome', 'porte'] },
    produtos: { rota: 'produtos', identidade: ['codigo'] },
    entradasESaidas: { rota: 'entradasESaidas', identidade: ['data', 'descricao', 'tipo', 'valor'] },
    clientes: { rota: 'clientes', identidade: ['nome', 'telefone'] }
  };

  var real = window.localStorage;
  var cache = {};      /* o que as telas enxergam agora            */
  var espelho = {};    /* ultima versao conhecida do banco (p/ diff) */
  var carregado = false;
  var ouvintes = [];

  function token() {
    try { return sessionStorage.getItem(CHAVE_TOKEN) || ''; } catch (e) { return ''; }
  }

  function texto(valor) {
    return String(valor === null || valor === undefined ? '' : valor)
      .normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
  }

  /* Chave de identidade: serve para reconhecer um registro que a tela criou
     sem id (as telas antigas criam objetos sem id) quando ele volta do banco
     ja com id. Sem isso, o mesmo registro seria gravado duas vezes. */
  function identidade(colecao, item) {
    var campos = COLECOES[colecao].identidade;
    return campos.map(function (campo) { return texto(item && item[campo]); }).join('|');
  }

  /* --------------------------------------------------------------------- */
  /* Carga a partir do banco                                               */
  /* --------------------------------------------------------------------- */

  function aplicarEstado(dados) {
    Object.keys(COLECOES).forEach(function (chave) {
      var lista = Array.isArray(dados[chave]) ? dados[chave] : [];
      cache[chave] = lista;
      espelho[chave] = JSON.parse(JSON.stringify(lista));
    });
    window.LaneStore.totais = dados.totais || {};
    window.LaneStore.atualizadoEm = dados.atualizadoEm || '';
    carregado = true;
  }

  function vazio() {
    Object.keys(COLECOES).forEach(function (chave) { cache[chave] = []; espelho[chave] = []; });
  }

  /* Primeira carga: sincrona, porque as telas leem os dados durante o parse. */
  function carregarSincrono() {
    var t = token();
    if (!t) { vazio(); return; }
    try {
      var xhr = new XMLHttpRequest();
      xhr.open('GET', '/api/admin/estado?token=' + encodeURIComponent(t), false);
      xhr.send(null);
      if (xhr.status !== 200) { vazio(); return; }
      var corpo = JSON.parse(xhr.responseText);
      if (!corpo || corpo.ok !== true) { vazio(); return; }
      aplicarEstado(corpo.data);
    } catch (erro) {
      console.error('[LanePets] nao foi possivel carregar o estado do painel:', erro);
      vazio();
    }
  }

  /* Recargas seguintes: assincronas. */
  function recarregar() {
    var t = token();
    if (!t) return Promise.resolve(false);
    return fetch('/api/admin/estado?token=' + encodeURIComponent(t))
      .then(function (r) { return r.json(); })
      .then(function (corpo) {
        if (!corpo || corpo.ok !== true) return false;
        aplicarEstado(corpo.data);
        avisar();
        return true;
      })
      .catch(function (erro) {
        console.warn('[LanePets] falha ao recarregar o estado do painel:', erro);
        return false;
      });
  }

  function avisar() {
    ouvintes.forEach(function (fn) {
      try { fn(); } catch (e) { console.error('[LanePets] ouvinte do painel falhou:', e); }
    });
    try { window.dispatchEvent(new CustomEvent('lanepets:dados')); } catch (e) { /* navegador antigo */ }
  }

  /* --------------------------------------------------------------------- */
  /* Gravacao: manda para o banco apenas o que mudou                       */
  /* --------------------------------------------------------------------- */

  function calcularDelta(colecao, novos) {
    var antigos = espelho[colecao] || [];
    var porId = {};
    var porIdentidade = {};

    antigos.forEach(function (item) {
      if (item && item.id) porId[item.id] = item;
      var chave = identidade(colecao, item);
      if (!porIdentidade[chave]) porIdentidade[chave] = [];
      porIdentidade[chave].push(item);
    });

    var criados = [];
    var atualizados = [];
    var vistos = {};

    novos.forEach(function (item) {
      if (!item || typeof item !== 'object') return;
      var original = null;

      if (item.id && porId[item.id]) {
        original = porId[item.id];
      } else if (!item.id) {
        /* Registro criado pela tela sem id: tenta casar com um registro do
           banco pela identidade antes de considerar que e novo. */
        var fila = porIdentidade[identidade(colecao, item)];
        while (fila && fila.length) {
          var candidato = fila.shift();
          if (candidato && candidato.id && !vistos[candidato.id]) { original = candidato; break; }
        }
      }

      if (original && original.id) {
        vistos[original.id] = true;
        item.id = original.id;
        if (JSON.stringify(item) !== JSON.stringify(original)) atualizados.push(item);
      } else {
        criados.push(item);
      }
    });

    var removidos = antigos
      .filter(function (item) { return item && item.id && !vistos[item.id]; })
      .map(function (item) { return item.id; });

    return { criados: criados, atualizados: atualizados, removidos: removidos };
  }

  function gravar(colecao, novos) {
    var delta = calcularDelta(colecao, novos);
    if (!delta.criados.length && !delta.atualizados.length && !delta.removidos.length) {
      cache[colecao] = novos;
      return Promise.resolve();
    }

    /* A tela ja mostrou o resultado; o cache acompanha na hora e o banco
       confirma logo em seguida. */
    cache[colecao] = novos;

    return fetch('/api/admin/sync/' + encodeURIComponent(COLECOES[colecao].rota), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        token: token(),
        criados: delta.criados,
        atualizados: delta.atualizados,
        removidos: delta.removidos
      })
    })
      .then(function (r) { return r.json(); })
      .then(function (corpo) {
        if (!corpo || corpo.ok !== true) throw new Error((corpo && corpo.error) || 'Falha ao gravar no banco.');
        /* Os ids gerados pelo servidor voltam na ordem dos criados. */
        (corpo.data.novosIds || []).forEach(function (id, i) {
          if (delta.criados[i]) delta.criados[i].id = id;
        });
        espelho[colecao] = JSON.parse(JSON.stringify(cache[colecao]));
        return recarregar();
      })
      .catch(function (erro) {
        console.error('[LanePets] nao foi possivel gravar "' + colecao + '" no banco:', erro);
        if (typeof window.toast === 'function') {
          window.toast('Não foi possível salvar no servidor: ' + erro.message, 'error');
        }
        /* Volta a mostrar o que o banco realmente tem, para a tela nunca
           exibir algo que nao foi gravado. */
        return recarregar();
      });
  }

  /* --------------------------------------------------------------------- */
  /* Adaptador que substitui o localStorage                                */
  /* --------------------------------------------------------------------- */

  function gerenciada(chave) {
    return Object.prototype.hasOwnProperty.call(COLECOES, String(chave));
  }

  var adaptador = {
    getItem: function (chave) {
      if (gerenciada(chave)) return JSON.stringify(cache[chave] || []);
      return real.getItem(chave);
    },
    setItem: function (chave, valor) {
      if (!gerenciada(chave)) return real.setItem(chave, valor);
      var lista;
      try { lista = JSON.parse(valor); } catch (e) { lista = []; }
      if (!Array.isArray(lista)) lista = [];
      gravar(String(chave), lista);
    },
    removeItem: function (chave) {
      if (gerenciada(chave)) return gravar(String(chave), []);
      return real.removeItem(chave);
    },
    clear: function () {
      /* Nunca apaga o banco: limpa apenas as preferencias de tela. */
      real.clear();
    },
    key: function (indice) { return this._chaves()[indice] || null; },
    _chaves: function () {
      var lista = Object.keys(COLECOES);
      for (var i = 0; i < real.length; i++) {
        var k = real.key(i);
        if (k && lista.indexOf(k) === -1) lista.push(k);
      }
      return lista;
    }
  };

  Object.defineProperty(adaptador, 'length', {
    get: function () { return this._chaves().length; }
  });

  try {
    Object.defineProperty(window, 'localStorage', {
      configurable: true,
      get: function () { return adaptador; }
    });
  } catch (erro) {
    console.error('[LanePets] nao foi possivel instalar a ponte de dados:', erro);
  }

  /* --------------------------------------------------------------------- */
  /* API publica da ponte                                                  */
  /* --------------------------------------------------------------------- */

  window.LaneStore = {
    recarregar: recarregar,
    aoAtualizar: function (fn) { if (typeof fn === 'function') ouvintes.push(fn); },
    lista: function (colecao) { return cache[colecao] || []; },
    pronto: function () { return carregado; },
    totais: {},
    atualizadoEm: ''
  };

  carregarSincrono();

  /* --------------------------------------------------------------------- */
  /* Atualizacao automatica                                                */
  /* Preferencia: SignalR (o servidor avisa na hora). O intervalo de 20s e  */
  /* a rede de seguranca para quando o tempo real estiver indisponivel.     */
  /* --------------------------------------------------------------------- */
  document.addEventListener('DOMContentLoaded', function () {
    if (window.LaneAdmin && typeof window.LaneAdmin.aoMudar === 'function') {
      window.LaneAdmin.aoMudar(function () { recarregar(); });
    }
    setInterval(function () { if (!document.hidden) recarregar(); }, 20000);
    document.addEventListener('visibilitychange', function () { if (!document.hidden) recarregar(); });
  });
})();
