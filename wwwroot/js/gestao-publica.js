/* =========================================================
   LanePets — Gestão da área pública
   Mesmas rotas e mesmas regras de negócio de antes.
   Mudou apenas a apresentação (classes do design system) e
   o login passou a enviar e-mail + senha, como a rota
   POST /api/login exige.
   ========================================================= */
(function () {
  let token = '';
  const $ = seletor => document.querySelector(seletor);

  const api = async (url, opcoes = {}) => {
    const resposta = await fetch(url, opcoes);
    const corpo = await resposta.json();
    if (!resposta.ok || !corpo.ok) throw new Error(corpo.error || 'Erro ao processar.');
    return corpo.data;
  };
  const post = (url, corpo) => api(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ...corpo, token })
  });
  const esc = valor => { const d = document.createElement('div'); d.textContent = valor ?? ''; return d.innerHTML; };
  const brl = valor => Number(valor || 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  const aviso = (mensagem, tipo) => (window.toast ? toast(mensagem, tipo) : console.log(mensagem));

  const vazio = (titulo, texto) =>
    `<div class="empty-state"><h3>${titulo}</h3><p>${texto}</p></div>`;
  const tabela = (cabecalhos, linhas) =>
    `<div class="table-wrap"><table><thead><tr>${cabecalhos.map(c => `<th>${c}</th>`).join('')}</tr></thead><tbody>${linhas}</tbody></table></div>`;

  const BADGE = {
    'Pendente': 'badge-warning',
    'Aprovado': 'badge-success',
    'Concluída': 'badge-success',
    'Em contato': 'badge-info',
    'Recusado': 'badge-danger',
    'Cancelada': 'badge-danger',
    'Cancelado': 'badge-danger',
    'Pago': 'badge-success'
  };
  const badge = status => `<span class="badge ${BADGE[status] || 'badge-neutral'}">${esc(status)}</span>`;

  async function carregar() {
    const [planos, depoimentos, solicitacoes] = await Promise.all([
      api('/api/admin/seguros?token=' + encodeURIComponent(token)),
      api('/api/admin/depoimentos?token=' + encodeURIComponent(token)),
      api('/api/admin/seguros/solicitacoes?token=' + encodeURIComponent(token))
    ]);

    $('#planos').innerHTML = planos.length
      ? tabela(['Plano', 'Valor mensal', 'Situação', 'Ações'], planos.map(p => `
          <tr>
            <td><strong>${esc(p.nome)}</strong><br><span class="text-muted">${esc(p.descricao || '')}</span>
              ${p.condicoes ? `<br><span class="text-muted"><strong>Condições:</strong> ${esc(p.condicoes)}</span>` : ''}</td>
            <td class="num">${brl(p.valorMensal)}</td>
            <td>${p.ativo ? '<span class="badge badge-success">Visível no site</span>' : '<span class="badge badge-neutral">Oculto</span>'}</td>
            <td><div class="row-actions">
              <button class="btn btn-outline btn-sm editar-plano" type="button" data-id="${esc(p.id)}">Editar</button>
              <button class="btn btn-outline btn-sm alterar-plano" type="button" data-id="${esc(p.id)}" data-ativo="${!p.ativo}">${p.ativo ? 'Desativar' : 'Ativar'}</button>
              <button class="btn btn-danger btn-sm excluir-plano" type="button" data-id="${esc(p.id)}" data-nome="${esc(p.nome)}">Excluir</button>
            </div></td>
          </tr>`).join(''))
      : vazio('Nenhum plano cadastrado', 'Crie o primeiro plano no formulário ao lado.');

    $('#depoimentos').innerHTML = depoimentos.length
      ? tabela(['Cliente', 'Avaliação', 'Comentário', 'Status', 'Ação'], depoimentos.map(d => `
          <tr>
            <td><strong>${esc(d.nomeCliente)}</strong><br><span class="text-muted">tutor(a) de ${esc(d.nomePet)}</span></td>
            <td style="color:var(--accent-600);white-space:nowrap">${'★'.repeat(d.avaliacao)}${'☆'.repeat(5 - d.avaliacao)}</td>
            <td>${esc(d.comentario)}</td>
            <td>${badge(d.status)}</td>
            <td>${d.status === 'Pendente'
              ? `<div class="row-actions"><button class="btn btn-secondary btn-sm moderar" type="button" data-id="${esc(d.id)}" data-status="Aprovado">Aprovar</button><button class="btn btn-danger btn-sm moderar" type="button" data-id="${esc(d.id)}" data-status="Recusado">Recusar</button></div>`
              : '<span class="text-muted">—</span>'}</td>
          </tr>`).join(''))
      : vazio('Nenhum depoimento enviado', 'Assim que uma família avaliar pelo site, ele aparece aqui para moderação.');

    $('#solicitacoes').innerHTML = solicitacoes.length
      ? tabela(['Plano', 'Cliente / pet', 'Telefone', 'Valor', 'Pagamento', 'Status', 'Atualizar'], solicitacoes.map(s => `
          <tr>
            <td><strong>${esc(s.nomePlano)}</strong><br><span class="text-muted">${brl(s.valorMensal)}/mês · ${new Date(s.criadoEm).toLocaleDateString('pt-BR')}</span></td>
            <td><strong>${esc(s.clienteAtual || s.nomeCliente)}</strong>
              <br><span class="text-muted">${esc(s.petAtual || s.nomePet)}</span>
              <br>${s.temCadastro
                  ? '<span class="badge badge-success">Conta vinculada</span>'
                  : '<span class="badge badge-neutral">Pedido pelo site</span>'}</td>
            <td>${esc(s.telefoneAtual || s.telefone)}</td>
            <td class="num">${brl(s.valor || s.valorMensal)}<br><span class="text-muted">contratado</span></td>
            <td>${esc(s.metodoPagamento || 'A combinar')}${s.cartaoFinal ? `<br><span class="text-muted">**** ${esc(s.cartaoFinal)}</span>` : ''}<br>${badge(s.pagamentoStatus || 'Pendente')}</td>
            <td>${badge(s.status)}${s.dataCancelamento ? `<br><span class="text-muted">Cancelado em ${new Date(s.dataCancelamento).toLocaleDateString('pt-BR')}</span>` : ''}</td>
            <td><select class="input sol-status" data-id="${esc(s.id)}" style="min-width:150px">
              ${['Pendente', 'Em contato', 'Concluída', 'Cancelada'].map(op => `<option ${s.status === op ? 'selected' : ''}>${op}</option>`).join('')}
            </select></td>
          </tr>`).join(''))
      : vazio('Nenhuma solicitação recebida', 'As contratações pedidas pelo site aparecem nesta lista.');

    document.querySelectorAll('.alterar-plano').forEach(botao => botao.onclick = async () => {
      try { await post('/api/admin/seguros/' + botao.dataset.id + '/ativo', { ativo: botao.dataset.ativo === 'true' }); aviso('Plano atualizado.', 'success'); carregar(); }
      catch (erro) { aviso(erro.message, 'error'); }
    });
    document.querySelectorAll('.editar-plano').forEach(botao => botao.onclick = () => {
      const plano = planos.find(p => p.id === botao.dataset.id);
      if (!plano) return;
      /* Reaproveita o formulario de cadastro: preenche os campos e troca o
         botao para "Salvar alteracoes". Nenhuma tela nova. */
      $('#nome').value = plano.nome || '';
      $('#descricao').value = plano.descricao || '';
      $('#coberturas').value = plano.coberturas || '';
      $('#beneficios').value = plano.beneficios || '';
      if ($('#condicoes')) $('#condicoes').value = plano.condicoes || '';
      $('#valor').value = plano.valorMensal || '';
      planoEmEdicao = plano.id;
      $('#criar-plano').textContent = 'Salvar alterações';
      if ($('#cancelar-edicao')) $('#cancelar-edicao').style.display = '';
      $('#nome').focus();
    });

    document.querySelectorAll('.excluir-plano').forEach(botao => botao.onclick = async () => {
      if (!window.confirm(`Excluir o plano "${botao.dataset.nome}"? Planos com contrato não podem ser excluídos — nesse caso, desative.`)) return;
      try {
        await api('/api/admin/seguros/' + botao.dataset.id + '?token=' + encodeURIComponent(token), { method: 'DELETE' });
        aviso('Plano excluído.', 'success');
        carregar();
      } catch (erro) { aviso(erro.message, 'error'); }
    });

    document.querySelectorAll('.moderar').forEach(botao => botao.onclick = async () => {
      try { await post('/api/admin/depoimentos/' + botao.dataset.id + '/status', { status: botao.dataset.status }); aviso('Depoimento ' + botao.dataset.status.toLowerCase() + '.', 'success'); carregar(); }
      catch (erro) { aviso(erro.message, 'error'); }
    });
    document.querySelectorAll('.sol-status').forEach(campo => campo.onchange = async () => {
      try { await post('/api/admin/solicitacoes/' + campo.dataset.id + '/status', { status: campo.value }); aviso('Status atualizado.', 'success'); carregar(); }
      catch (erro) { aviso(erro.message, 'error'); }
    });
  }

  async function entrar() {
    const botao = $('#entrar');
    const mensagem = $('#mensagem');
    mensagem.textContent = '';
    const email = $('#email').value.trim();
    const senha = $('#senha').value;
    if (!email || !senha) { mensagem.textContent = 'Informe e-mail e senha.'; return; }

    const textoOriginal = botao.innerHTML;
    botao.disabled = true;
    botao.innerHTML = '<span class="spinner"></span> Entrando…';
    try {
      const dados = await api('/api/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, senha })
      });
      token = dados.token;
      $('#login').style.display = 'none';
      $('#gestao').style.display = 'block';
      await carregar();
    } catch (erro) {
      mensagem.textContent = erro.message;
      botao.disabled = false;
      botao.innerHTML = textoOriginal;
    }
  }

  $('#entrar').onclick = entrar;
  $('#senha').addEventListener('keydown', e => { if (e.key === 'Enter') entrar(); });

  function limparFormularioPlano() {
    ['nome', 'descricao', 'coberturas', 'beneficios', 'condicoes', 'valor']
      .forEach(id => { if ($('#' + id)) $('#' + id).value = ''; });
    planoEmEdicao = null;
    $('#criar-plano').textContent = 'Salvar plano';
    if ($('#cancelar-edicao')) $('#cancelar-edicao').style.display = 'none';
  }

  if ($('#cancelar-edicao')) $('#cancelar-edicao').onclick = limparFormularioPlano;

  $('#criar-plano').onclick = async () => {
    const dados = {
      nome: $('#nome').value,
      descricao: $('#descricao').value,
      coberturas: $('#coberturas').value,
      beneficios: $('#beneficios').value,
      condicoes: $('#condicoes') ? $('#condicoes').value : '',
      valorMensal: Number($('#valor').value)
    };
    try {
      if (planoEmEdicao) {
        await api('/api/admin/seguros/' + planoEmEdicao, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ...dados, token })
        });
        aviso('Plano atualizado. A alteração já vale para os clientes.', 'success');
      } else {
        await post('/api/admin/seguros', dados);
        aviso('Plano salvo com sucesso.', 'success');
      }
      limparFormularioPlano();
      carregar();
    } catch (erro) { aviso(erro.message, 'error'); }
  };
})();
