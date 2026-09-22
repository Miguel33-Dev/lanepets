/* =========================================================
   LANE PETS — UI KIT
   Toasts, menu mobile e modal de confirmação reutilizáveis.
   Não mexe em nenhuma lógica de dados: apenas UI.
   ========================================================= */

/* ---------- Toasts ---------- */
function garantirToastStack() {
  let stack = document.querySelector(".toast-stack");
  if (!stack) {
    stack = document.createElement("div");
    stack.className = "toast-stack";
    document.body.appendChild(stack);
  }
  return stack;
}

const TOAST_ICONS = {
  success: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
  error: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 8v5M12 16h.01"/></svg>',
  info: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/></svg>'
};

function toast(mensagem, tipo = "success", duracao = 3200) {
  const stack = garantirToastStack();
  const el = document.createElement("div");
  el.className = `toast toast-${tipo}`;
  el.innerHTML = `${TOAST_ICONS[tipo] || TOAST_ICONS.info}<span>${mensagem}</span>`;
  stack.appendChild(el);
  setTimeout(() => {
    el.classList.add("leaving");
    setTimeout(() => el.remove(), 200);
  }, duracao);
}
window.toast = toast;

/* ---------- Confirmação (substitui confirm()/alert() nativos) ---------- */
function confirmarAcao({ titulo = "Tem certeza?", texto = "Esta ação não poderá ser desfeita.", corBotao = "btn-danger", textoBotao = "Excluir" } = {}) {
  return new Promise((resolve) => {
    const overlay = document.createElement("div");
    overlay.className = "modal-overlay ativo";
    overlay.innerHTML = `
      <div class="modal-box" role="alertdialog" aria-modal="true" aria-labelledby="confirmTitle">
        <div class="modal-confirm-icon">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 9v4M12 17h.01M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/></svg>
        </div>
        <h2 id="confirmTitle">${titulo}</h2>
        <p class="text-muted" style="margin:6px 0 0;font-size:.9rem;">${texto}</p>
        <div class="modal-actions">
          <button type="button" class="btn btn-outline" data-acao="cancelar">Cancelar</button>
          <button type="button" class="btn ${corBotao}" data-acao="confirmar">${textoBotao}</button>
        </div>
      </div>`;
    document.body.appendChild(overlay);
    const fechar = (resultado) => { overlay.remove(); resolve(resultado); };
    overlay.addEventListener("mousedown", (e) => { if (e.target === overlay) fechar(false); });
    overlay.querySelector('[data-acao="cancelar"]').addEventListener("click", () => fechar(false));
    overlay.querySelector('[data-acao="confirmar"]').addEventListener("click", () => fechar(true));
    overlay.querySelector('[data-acao="confirmar"]').focus();
  });
}
window.confirmarAcao = confirmarAcao;

/* ---------- Menu mobile ---------- */
function iniciarMenuMobile() {
  const sidebar = document.querySelector(".sidebar");
  const toggle = document.querySelector(".menu-toggle");
  const close = document.querySelector(".sidebar-close");
  let backdrop = document.querySelector(".sidebar-backdrop");
  if (!backdrop) {
    backdrop = document.createElement("div");
    backdrop.className = "sidebar-backdrop";
    document.body.appendChild(backdrop);
  }
  const abrir = () => { sidebar.classList.add("open"); backdrop.classList.add("show"); };
  const fechar = () => { sidebar.classList.remove("open"); backdrop.classList.remove("show"); };
  if (toggle) toggle.addEventListener("click", abrir);
  if (close) close.addEventListener("click", fechar);
  backdrop.addEventListener("click", fechar);
}
document.addEventListener("DOMContentLoaded", iniciarMenuMobile);

/* ---------- Botão em estado "salvando..." ---------- */
function comLoading(botao, textoCarregando, fn) {
  const textoOriginal = botao.innerHTML;
  botao.disabled = true;
  botao.innerHTML = `<span class="spinner"></span> ${textoCarregando}`;
  Promise.resolve()
    .then(fn)
    .finally(() => {
      setTimeout(() => { botao.disabled = false; botao.innerHTML = textoOriginal; }, 260);
    });
}
window.comLoading = comLoading;
