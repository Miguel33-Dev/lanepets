/**
 * LanePets — nomes das unidades nos textos de marca (pendência do roadmap, 25/09).
 *
 * "Franco + Caieiras" estava escrito à mão no menu lateral do painel, no login e
 * no site. Agora qualquer elemento com data-unidades é preenchido a partir das
 * unidades ATIVAS do banco (GET /api/public/unidades — público, sem sessão):
 *
 *   <span data-unidades="curto">…</span>    -> "Caieiras + Franco"  (texto longo demais: "3 unidades")
 *   <span data-unidades="extenso">…</span>  -> "Caieiras e Franco da Rocha"  (ordem alfabética, como a API)
 *   <span data-unidades="qtd">…</span>      -> "2 unidades"
 *
 * O texto que já está no HTML fica como está se a API falhar ou vier vazia —
 * ele descreve as unidades semeadas, então nunca aparece algo inventado.
 * Para JS: await LaneMarcaUnidades.carregar() -> [{ id, nome }].
 */
(function () {
  let promessa = null;
  const CONECTORES = ['da', 'de', 'do', 'das', 'dos', 'e'];

  /* "Franco da Rocha" -> "Franco"; "Vila Mariana" e "São Paulo" ficam inteiros. */
  function nomeCurto(nome) {
    const palavras = String(nome || '').trim().split(/\s+/);
    const i = palavras.findIndex((p, n) => n > 0 && CONECTORES.includes(p.toLowerCase()));
    const curto = (i > 0 ? palavras.slice(0, i) : palavras).join(' ');
    return curto.length > 14 ? palavras[0] : curto;
  }

  function juntar(lista, ultimo) {
    return lista.length <= 1 ? lista.join('') : lista.slice(0, -1).join(', ') + ' ' + ultimo + ' ' + lista[lista.length - 1];
  }

  function textos(unidades) {
    const nomes = unidades.map(u => u.nome);
    const qtd = nomes.length + (nomes.length === 1 ? ' unidade' : ' unidades');
    const curto = nomes.map(nomeCurto).join(' + ');
    return {
      curto: curto.length > 24 ? qtd : curto, /* cabe no menu lateral a 330 px */
      extenso: juntar(nomes, 'e'),
      qtd
    };
  }

  function carregar() {
    if (promessa) return promessa;
    promessa = fetch('/api/public/unidades')
      .then(r => r.json())
      .then(corpo => (corpo && corpo.ok && Array.isArray(corpo.data) ? corpo.data : [])
        .map(u => ({ id: u.id || u.Id, nome: u.nome || u.Nome }))
        .filter(u => u.nome))
      .catch(erro => { console.error('[LanePets] Não foi possível carregar as unidades para o texto da marca.', erro); return []; });
    return promessa;
  }

  function aplicar() {
    const alvos = document.querySelectorAll('[data-unidades]');
    if (!alvos.length) return;
    carregar().then(unidades => {
      if (!unidades.length) return; /* mantém o texto do HTML */
      const t = textos(unidades);
      alvos.forEach(el => { const v = t[el.dataset.unidades]; if (v) el.textContent = v; });
    });
  }

  window.LaneMarcaUnidades = { carregar, nomeCurto, textos };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', aplicar);
  else aplicar();
})();
