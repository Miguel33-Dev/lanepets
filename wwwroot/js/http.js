(function () {
  const config = window.LANE_PETS_CONFIG || {};
  const baseUrl = String(config.API_BASE_URL || config.API_URL || '/api').replace(/\/$/, '');

  function unavailable() {
    return new Error('Não foi possível conectar ao servidor. Inicie o LanePets com "dotnet run" e abra a URL http://localhost:5180 no navegador.');
  }

  function url(path) {
    if (window.location.protocol === 'file:') throw unavailable();
    return baseUrl + '/' + String(path).replace(/^\//, '');
  }

  async function request(path, options) {
    let response;
    try { response = await fetch(url(path), options); }
    catch (_) { throw unavailable(); }
    const text = await response.text();
    let payload;
    try { payload = JSON.parse(text); }
    catch (_) { throw new Error(response.ok ? 'Resposta inválida do servidor.' : 'O servidor retornou um erro inesperado.'); }
    if (!response.ok || !payload.ok) throw new Error(payload.error || 'Não foi possível concluir a operação.');
    return payload.data;
  }

  window.LanePetsHttp = { request, url };
})();
