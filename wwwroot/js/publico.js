(function () {
  const $ = selector => document.querySelector(selector);
  async function api(url, options) { const response = await fetch(url, options); const payload = await response.json(); if (!response.ok || !payload.ok) throw new Error(payload.error || 'Não foi possível concluir a solicitação.'); return payload.data; }
  const money = value => Number(value).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  const icon = ['✂','✦','♧','♥','◌','✚'];
  async function carregar() {
    try { const servicos = await api('/api/public/servicos'); $('#lista-servicos').innerHTML = servicos.slice(0, 6).map((s, i) => `<article class="card"><span class="servico-icone">${icon[i % icon.length]}</span><h3>${esc(s.nome)}</h3><p>${s.porte ? `Para pets de porte ${esc(s.porte)}.` : 'Cuidado personalizado para o seu pet.'}</p><span class="preco">A partir de ${money(s.preco)}</span></article>`).join('') || '<article class="card">Em breve, novos serviços.</article>';
      const planos = await api('/api/public/seguros'); $('#lista-seguros').innerHTML = planos.map((p, i) => `<article class="plano ${i === 1 ? 'destaque' : ''}"><h3>${esc(p.nome)}</h3><p>${esc(p.descricao)}</p><ul>${esc(p.coberturas).split(';').map(x => `<li>${x.trim()}</li>`).join('')}</ul><strong>${money(p.valorMensal)}<small>/mês</small></strong><button class="botao ${i === 1 ? 'primario' : 'claro'} contratar" data-id="${esc(p.id)}" data-nome="${esc(p.nome)}">Quero este plano</button></article>`).join('');
      const produtos = await api('/api/public/produtos'); $('#lista-produtos').innerHTML = produtos.slice(0, 6).map(p => `<article class="card"><span class="servico-icone">◌</span><h3>${esc(p.nome)}</h3><p>${esc(p.categoria || 'Produto LanePets')}</p><span class="preco">${money(p.valorVenda)}</span></article>`).join('') || '<article class="card">Novos produtos em breve.</article>';
      document.querySelectorAll('.contratar').forEach(button => button.addEventListener('click', () => { $('#plano-id').value = button.dataset.id; $('#seguro-titulo').textContent = `Plano ${button.dataset.nome}`; $('#modal-seguro').showModal(); }));
      const depoimentos = await api('/api/public/depoimentos'); $('#lista-depoimentos').innerHTML = depoimentos.length ? depoimentos.slice(0, 6).map(d => `<article class="card depoimento"><span class="estrelas">${'★'.repeat(d.avaliacao)}${'☆'.repeat(5 - d.avaliacao)}</span><blockquote>“${esc(d.comentario)}”</blockquote><cite>${esc(d.nomeCliente)} · tutor(a) de ${esc(d.nomePet)}</cite></article>`).join('') : '<article class="card"><h3>Seja a primeira família a avaliar</h3><p>Clientes LanePets podem enviar um depoimento para a nossa moderação.</p></article>';
    } catch (error) { $('#lista-servicos').innerHTML = `<article class="card"><h3>Serviços LanePets</h3><p>Consulte nossa equipe para conhecer todas as opções.</p></article>`; console.error(error); }
  }
  function esc(value) { const div = document.createElement('div'); div.textContent = value ?? ''; return div.innerHTML; }
  $('#abrir-depoimento').addEventListener('click', () => $('#modal-depoimento').showModal());
  $('#enviar-depoimento').addEventListener('click', async event => { event.preventDefault(); const feedback = $('#dep-feedback'); feedback.textContent = 'Enviando…'; try { const data = await api('/api/public/depoimentos', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ nome: $('#dep-nome').value, telefone: $('#dep-telefone').value, pet: $('#dep-pet').value, avaliacao: Number($('#dep-avaliacao').value), comentario: $('#dep-comentario').value }) }); feedback.textContent = data.message; event.target.form.reset(); } catch (error) { feedback.textContent = error.message; } });
  $('#enviar-seguro').addEventListener('click', async event => { event.preventDefault(); const feedback = $('#seguro-feedback'); feedback.textContent = 'Enviando…'; try { const data = await api('/api/public/seguros/solicitacoes', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ planoId: $('#plano-id').value, nome: $('#seguro-nome').value, telefone: $('#seguro-telefone').value, pet: $('#seguro-pet').value, observacao: $('#seguro-observacao').value }) }); feedback.textContent = data.message; event.target.form.reset(); } catch (error) { feedback.textContent = error.message; } });
  carregar();
})();

/* ==========================================================================
   Navegação do topo — menu responsivo e estado de sessão do cliente
   ========================================================================== */
(function () {
  const botao = document.querySelector('#abrir-menu');
  const nav = document.querySelector('#menu-principal');
  if (!nav) return;

  /* Menu responsivo -------------------------------------------------- */
  if (botao) {
    const alternar = aberto => {
      nav.classList.toggle('aberto', aberto);
      botao.setAttribute('aria-expanded', String(aberto));
      botao.textContent = aberto ? '✕' : '☰';
      botao.setAttribute('aria-label', aberto ? 'Fechar menu' : 'Abrir menu');
    };
    botao.addEventListener('click', () => alternar(!nav.classList.contains('aberto')));
    nav.addEventListener('click', event => { if (event.target.tagName === 'A') alternar(false); });
    document.addEventListener('keydown', event => { if (event.key === 'Escape') alternar(false); });
    window.addEventListener('resize', () => { if (window.innerWidth > 1080) alternar(false); });
  }

  /* Estado da sessão do cliente --------------------------------------- */
  const conta = document.querySelector('#nav-conta');
  const acao = document.querySelector('#nav-acao');
  const logado = () => Boolean(localStorage.getItem('lanePetsClienteToken'));

  function aplicarEstado() {
    if (!conta || !acao) return;
    if (logado()) {
      conta.textContent = 'Minha conta';
      conta.href = 'minha-conta.html';
      acao.textContent = 'Sair';
      acao.href = '#sair';
      acao.dataset.sair = '1';
    } else {
      conta.textContent = 'Entrar';
      conta.href = 'minha-conta.html';
      acao.textContent = 'Criar conta';
      acao.href = 'minha-conta.html#cadastro';
      delete acao.dataset.sair;
    }
  }

  if (acao) {
    acao.addEventListener('click', async event => {
      if (!acao.dataset.sair) return;
      event.preventDefault();
      const token = localStorage.getItem('lanePetsClienteToken') || '';
      try {
        await fetch('/api/cliente/logout', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-LanePets-Client': token } });
      } catch (_) { /* sessão local é encerrada de qualquer forma */ }
      localStorage.removeItem('lanePetsClienteToken');
      aplicarEstado();
    });
  }

  aplicarEstado();
  window.addEventListener('storage', aplicarEstado);
  window.addEventListener('pageshow', aplicarEstado);
})();

/* ==========================================================================
   Fechamento dos modais (<dialog>)
   O botão X é type="button": não submete o formulário, apenas fecha o modal.
   Também fecha ao clicar fora do cartão. O Esc já é nativo do <dialog>.
   ========================================================================== */
(function () {
  document.querySelectorAll('dialog .fechar').forEach(botao => {
    botao.addEventListener('click', event => {
      event.preventDefault();
      const modal = botao.closest('dialog');
      if (modal && modal.open) modal.close();
    });
  });

  document.querySelectorAll('dialog').forEach(modal => {
    modal.addEventListener('click', event => {
      if (event.target === modal) modal.close();
    });
  });
})();
