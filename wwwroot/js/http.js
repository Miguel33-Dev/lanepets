(function () {
  const config = window.LANE_PETS_CONFIG || {};
  const baseUrl = String(config.API_BASE_URL || config.API_URL || '/api').replace(/\/$/, '');

  function unavailable() {
    const erro = new Error('Não foi possível conectar ao servidor. Inicie o LanePets com "dotnet run" e abra a URL http://localhost:5180 no navegador.');
    erro.status = 0;            /* 0 = nem chegou ao servidor */
    erro.semConexao = true;
    return erro;
  }

  /* Mantém o console útil durante o desenvolvimento: a tela mostra um texto
     curto, mas aqui ficam o método, a URL, o status e a resposta crua. */
  function diagnostico(metodo, destino, status, corpo) {
    console.error(`[LanePets] ${metodo} ${destino} -> HTTP ${status}`, corpo ? corpo.slice(0, 500) : '(resposta vazia)');
  }

  function url(path) {
    if (window.location.protocol === 'file:') throw unavailable();
    return baseUrl + '/' + String(path).replace(/^\//, '');
  }

  async function request(path, options) {
    const metodo = String((options && options.method) || 'GET').toUpperCase();
    const destino = url(path);
    let response;
    try { response = await fetch(destino, options); }
    catch (_) { throw unavailable(); }
    const text = await response.text();
    let payload;
    try { payload = JSON.parse(text); }
    catch (_) {
      /* O servidor respondeu, mas não com o envelope { ok, data }.
         Preserva o status para quem chamou poder diferenciar 401 de 500. */
      const erro = new Error(response.ok ? 'Resposta inválida do servidor.' : `O servidor retornou um erro inesperado (HTTP ${response.status}).`);
      erro.status = response.status;
      /* 405 = a rota existe, mas não aceita este método HTTP. Na prática isso
         acontece quando o processo em execução é uma build antiga, anterior ao
         endpoint que o frontend passou a chamar: recompile a API. */
      if (response.status === 405) {
        erro.message = `O servidor não aceita ${metodo} em ${destino} (HTTP 405). Pare o LanePets e rode "dotnet run" novamente para recompilar a API.`;
      }
      diagnostico(metodo, destino, response.status, text);
      throw erro;
    }
    if (!response.ok || !payload.ok) {
      const erro = new Error(payload.error || 'Não foi possível concluir a operação.');
      erro.status = response.status;   /* 401 = sessão inválida/expirada */
      diagnostico(metodo, destino, response.status, text);
      throw erro;
    }
    return payload.data;
  }

  window.LanePetsHttp = { request, url };
})();
