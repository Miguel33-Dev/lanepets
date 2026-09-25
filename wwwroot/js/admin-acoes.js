/**
 * LanePets — motor das ações de tabela (compartilhado).
 *
 * Duas coisas que qualquer tabela do painel pode usar:
 *
 *   1. DICA  — um balão para botões que são só ícone. Um único elemento com
 *              position:fixed, reaproveitado por todos os botões da página.
 *              Balão absoluto dentro da tabela seria recortado, porque
 *              .table-wrap tem overflow:auto.
 *
 *   2. MENU  — o "⋮" que substitui a linha de ícones no celular. Também
 *              fixed, pelo mesmo motivo.
 *
 * Basta marcar o botão:
 *
 *     <button class="acao" data-dica="Editar produto" aria-label="Editar produto">
 *
 * Os estilos correspondentes estão em css/admin-acoes.css.
 *
 * Isto é aparência. Nenhuma decisão de permissão passa por aqui: quem autoriza
 * é o backend, que responde 403 por conta própria.
 */
(function () {
  if (window.LaneAcoes) return;   // uma instância por página

  /* ------------------------------------------------------------------ DICA */
  const dica = document.createElement('div');
  dica.id = 'dicaFlutuante';
  dica.setAttribute('role', 'tooltip');

  function garantirNoDocumento() {
    if (!dica.isConnected) document.body.appendChild(dica);
  }

  function mostrarDica(alvo) {
    const texto = alvo.getAttribute('data-dica');
    if (!texto) return;
    garantirNoDocumento();
    dica.textContent = texto;
    dica.classList.add('visivel');
    const r = alvo.getBoundingClientRect();
    const largura = dica.offsetWidth;
    const esquerda = Math.min(Math.max(8, r.left + r.width / 2 - largura / 2), window.innerWidth - largura - 8);
    dica.style.left = esquerda + 'px';
    dica.style.top = (r.top - dica.offsetHeight - 9) + 'px';
  }

  const esconderDica = () => dica.classList.remove('visivel');

  /* ------------------------------------------------------------------ MENU */
  let menuAberto = null;

  function fecharMenu() {
    if (!menuAberto) return;
    const { lista, botao } = menuAberto;
    lista.classList.remove('aberto');
    botao.setAttribute('aria-expanded', 'false');
    setTimeout(() => { if (!lista.classList.contains('aberto')) lista.hidden = true; }, 150);
    menuAberto = null;
  }

  function abrirMenu(botao) {
    const lista = botao.parentElement.querySelector('.acoes-lista');
    if (!lista) return;
    if (menuAberto && menuAberto.lista === lista) return fecharMenu();
    fecharMenu();

    lista.hidden = false;
    const r = botao.getBoundingClientRect();
    const largura = lista.offsetWidth;
    const altura = lista.offsetHeight;
    // Abre para baixo; se não couber, sobe. O menu nunca sai da tela.
    const abaixo = r.bottom + 6 + altura < window.innerHeight;
    lista.style.left = Math.min(Math.max(8, r.right - largura), window.innerWidth - largura - 8) + 'px';
    lista.style.top = (abaixo ? r.bottom + 6 : r.top - altura - 6) + 'px';

    requestAnimationFrame(() => lista.classList.add('aberto'));
    botao.setAttribute('aria-expanded', 'true');
    menuAberto = { lista, botao };
  }

  /* ------------------------------------------------------------- LIGAÇÕES */
  document.addEventListener('mouseover', e => {
    const alvo = e.target.closest('[data-dica]');
    if (alvo) mostrarDica(alvo); else esconderDica();
  });
  document.addEventListener('focusin', e => {
    const alvo = e.target.closest('[data-dica]');
    if (alvo) mostrarDica(alvo); else esconderDica();
  });
  document.addEventListener('focusout', esconderDica);
  window.addEventListener('scroll', esconderDica, true);

  document.addEventListener('click', e => {
    const gatilho = e.target.closest('button[data-menu]');
    if (gatilho) { e.stopPropagation(); return abrirMenu(gatilho); }
    if (!e.target.closest('.acoes-lista')) fecharMenu();
  });
  document.addEventListener('keydown', e => { if (e.key === 'Escape') { fecharMenu(); esconderDica(); } });
  window.addEventListener('resize', fecharMenu);

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', garantirNoDocumento);
  else garantirNoDocumento();

  window.LaneAcoes = { esconderDica, fecharMenu };
})();
