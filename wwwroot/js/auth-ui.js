/* =============================================================================
   LanePets — utilitários das telas de autenticação
   Comportamento visual compartilhado: mostrar/ocultar senha, estados de campo,
   alertas, carregamento do botão e máscara de telefone.
   Não faz nenhuma chamada de API — apenas interface.
   ============================================================================= */
window.LanePetsAuthUI = (function () {
  const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[a-zA-Z]{2,}$/;

  /* Alterna a visibilidade dos campos de senha marcados com data-alvo ------- */
  function ligarOlhos(escopo) {
    (escopo || document).querySelectorAll('.lp-input__acao[data-alvo]').forEach(botao => {
      botao.addEventListener('click', event => {
        event.preventDefault();
        const campo = document.querySelector(botao.dataset.alvo);
        if (!campo) return;
        const oculto = campo.type === 'password';
        campo.type = oculto ? 'text' : 'password';
        botao.classList.toggle('is-visivel', oculto);
        botao.setAttribute('aria-pressed', String(oculto));
        botao.setAttribute('aria-label', oculto ? 'Ocultar senha' : 'Mostrar senha');
        campo.focus();
      });
    });
  }

  /* Estados do campo --------------------------------------------------------- */
  const bloco = campo => campo.closest('.lp-field');

  function erro(campo, mensagem) {
    const caixa = bloco(campo);
    if (!caixa) return false;
    caixa.classList.add('is-erro');
    caixa.classList.remove('is-ok');
    const msg = caixa.querySelector('.lp-field__msg');
    if (msg) { msg.textContent = mensagem; msg.dataset.dica = msg.dataset.dica || ''; }
    campo.setAttribute('aria-invalid', 'true');
    return false;
  }

  function ok(campo) {
    const caixa = bloco(campo);
    if (!caixa) return true;
    caixa.classList.remove('is-erro');
    caixa.classList.add('is-ok');
    restaurarDica(caixa);
    campo.removeAttribute('aria-invalid');
    return true;
  }

  function neutro(campo) {
    const caixa = bloco(campo);
    if (!caixa) return;
    caixa.classList.remove('is-erro', 'is-ok');
    restaurarDica(caixa);
    campo.removeAttribute('aria-invalid');
  }

  function restaurarDica(caixa) {
    const msg = caixa.querySelector('.lp-field__msg');
    if (msg) msg.textContent = msg.dataset.dica || '';
  }

  function limpar(escopo) {
    escopo.querySelectorAll('.lp-field').forEach(caixa => {
      caixa.classList.remove('is-erro', 'is-ok');
      restaurarDica(caixa);
      const campo = caixa.querySelector('input');
      if (campo) campo.removeAttribute('aria-invalid');
    });
  }

  /* Guarda a dica original de cada campo e limpa o erro ao digitar ---------- */
  function ligarCampos(escopo) {
    escopo.querySelectorAll('.lp-field').forEach(caixa => {
      const msg = caixa.querySelector('.lp-field__msg');
      if (msg && msg.dataset.dica === undefined) msg.dataset.dica = msg.textContent.trim();
      const campo = caixa.querySelector('input');
      if (campo) campo.addEventListener('input', () => {
        if (caixa.classList.contains('is-erro')) {
          caixa.classList.remove('is-erro');
          restaurarDica(caixa);
          campo.removeAttribute('aria-invalid');
        }
      });
    });
  }

  /* Alertas ------------------------------------------------------------------ */
  function alerta(seletor, mensagem, cartaoSeletor) {
    const caixa = document.querySelector(seletor);
    if (!caixa) return;
    const texto = caixa.querySelector('span');
    if (!mensagem) { caixa.classList.remove('is-visivel'); return; }
    if (texto) texto.textContent = mensagem;
    caixa.classList.add('is-visivel');
    const cartao = document.querySelector(cartaoSeletor || '.lp-card');
    if (cartao && caixa.classList.contains('lp-alert--erro')) {
      cartao.classList.remove('is-tremendo');
      void cartao.offsetWidth;
      cartao.classList.add('is-tremendo');
    }
  }

  /* Botão em carregamento ---------------------------------------------------- */
  function carregando(botao, ativo, rotuloOcupado) {
    const alvo = typeof botao === 'string' ? document.querySelector(botao) : botao;
    if (!alvo) return;
    const rotulo = alvo.querySelector('.lp-btn__texto');
    if (!alvo.dataset.rotulo && rotulo) alvo.dataset.rotulo = rotulo.textContent;
    alvo.classList.toggle('is-carregando', ativo);
    alvo.disabled = ativo;
    alvo.setAttribute('aria-busy', String(ativo));
    if (rotulo) rotulo.textContent = ativo ? rotuloOcupado : alvo.dataset.rotulo;
  }

  /* Máscara de telefone brasileiro ------------------------------------------ */
  function mascaraTelefone(campo) {
    if (!campo) return;
    campo.addEventListener('input', () => {
      const d = campo.value.replace(/\D/g, '').slice(0, 11);
      let saida = d;
      if (d.length > 2) saida = `(${d.slice(0, 2)}) ${d.slice(2)}`;
      if (d.length > 6 && d.length <= 10) saida = `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`;
      if (d.length > 10) saida = `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`;
      campo.value = saida;
    });
  }

  /* Validação de conveniência ao sair do campo ------------------------------ */
  function validarNoBlur(campo, teste) {
    if (!campo) return;
    campo.addEventListener('blur', () => {
      if (!campo.value.trim()) { neutro(campo); return; }
      teste(campo.value.trim()) ? ok(campo) : neutro(campo);
    });
  }

  return { EMAIL_RE, ligarOlhos, ligarCampos, limpar, erro, ok, neutro, alerta, carregando, mascaraTelefone, validarNoBlur };
})();
