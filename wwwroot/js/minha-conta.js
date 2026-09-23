/* =============================================================================
   LanePets — Área do cliente
   Autenticação (login / criar conta) + portal do cliente.
   As rotas, o backend e as chamadas de API continuam exatamente as mesmas;
   aqui mudaram apenas a validação visual, os estados e a troca de painéis.
   ============================================================================= */
(function () {
  const UI = window.LanePetsAuthUI;
  const $ = s => document.querySelector(s);

  let token = localStorage.getItem('lanePetsClienteToken') || '';
  if (token) $('#auth').style.display = 'none';

  const api = (path, options = {}) => LanePetsHttp.request(`cliente/${path}`, {
    ...options,
    headers: { ...(options.headers || {}), 'Content-Type': 'application/json', 'X-LanePets-Client': token }
  });

  const esc = v => { const d = document.createElement('div'); d.textContent = v || ''; return d.innerHTML; };
  const options = (el, rows, label) => el.innerHTML = rows.map(r => `<option value="${r.id}">${esc(label(r))}</option>`).join('');
  const message = (id, e) => $(id).textContent = e.message;

  const formLogin = $('#form-login');
  const formCadastro = $('#form-cadastro');

  /* ---------------------------------------------------------------------
     Interface das telas de autenticação
     --------------------------------------------------------------------- */
  UI.ligarOlhos(document);
  UI.ligarCampos(formLogin);
  UI.ligarCampos(formCadastro);
  UI.mascaraTelefone($('#cad-tel'));
  UI.validarNoBlur($('#login-email'), v => UI.EMAIL_RE.test(v));
  UI.validarNoBlur($('#cad-email'), v => UI.EMAIL_RE.test(v));
  UI.validarNoBlur($('#cad-nome'), v => v.length >= 3);
  UI.validarNoBlur($('#cad-tel'), v => v.replace(/\D/g, '').length >= 10);
  UI.validarNoBlur($('#cad-pet'), v => v.length >= 2);

  function mostrarPainel(nome) {
    document.querySelectorAll('.lp-painel').forEach(p => p.classList.remove('is-ativo'));
    $(`#painel-${nome}`).classList.add('is-ativo');
    $('#cartao-cliente').classList.toggle('lp-card--largo', nome === 'cadastro');
    UI.alerta('#login-aviso', '');
    UI.alerta('#login-info', '');
    UI.alerta('#cad-aviso', '');
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  $('#ir-cadastro').addEventListener('click', e => { e.preventDefault(); mostrarPainel('cadastro'); $('#cad-nome').focus(); });
  $('#ir-login').addEventListener('click', e => { e.preventDefault(); mostrarPainel('login'); $('#login-email').focus(); });
  $('#sucesso-continuar').addEventListener('click', () => { mostrarPainel('login'); $('#login-senha').focus(); });
  $('#esqueci').addEventListener('click', e => {
    e.preventDefault();
    UI.alerta('#login-info', 'Para redefinir sua senha, fale com a equipe LanePets em uma das unidades — Franco ou Caieiras.');
  });
  if (location.hash === '#cadastro') mostrarPainel('cadastro');

  /* Medidor de força da senha ------------------------------------------- */
  const NIVEIS = ['Mínimo 8', 'Fraca', 'Boa', 'Forte'];
  $('#cad-senha').addEventListener('input', event => {
    const v = event.target.value;
    let nivel = 0;
    if (v.length >= 8) nivel = 1;
    if (v.length >= 8 && /[A-Za-z]/.test(v) && /\d/.test(v)) nivel = 2;
    if (v.length >= 10 && /[A-Za-z]/.test(v) && /\d/.test(v) && /[^A-Za-z0-9]/.test(v)) nivel = 3;
    $('#forca-senha').dataset.nivel = String(nivel);
    $('#forca-texto').textContent = NIVEIS[nivel];
  });

  /* ---------------------------------------------------------------------
     Portal do cliente (comportamento original preservado)
     --------------------------------------------------------------------- */
  function exibirPortal(ativo) {
    $('#auth').style.display = ativo ? 'none' : 'block';
    $('#portal').style.display = ativo ? 'block' : 'none';
    $('#topo-site').hidden = !ativo;
    document.body.classList.toggle('lp-auth', !ativo);
    document.body.classList.toggle('lp-auth--cliente', !ativo);
  }

  async function abrir() {
    try {
      const conta = await api('conta');
      exibirPortal(true);
      $('#boas-vindas').textContent = `Olá, ${conta.nome}`;
      $('#pets').innerHTML = conta.pets.map(p => `<div class="item"><strong>${esc(p.petNome)}</strong>${esc(p.tipo)} · ${esc(p.raca)}</div>`).join('');
      $('#agendamentos').innerHTML = conta.agendamentos.length ? conta.agendamentos.map(a => `<div class="item"><strong>${esc(a.pet)} · ${esc(a.status)}</strong>${new Date(a.dataHora).toLocaleString('pt-BR')} · ${esc(a.unidade)}</div>`).join('') : '<p>Nenhum agendamento ainda.</p>';
      $('#lista-pedidos').innerHTML = conta.pedidos.length ? conta.pedidos.map(p => `<div class="item"><strong>${esc(p.produtoNome)} · ${esc(p.status)}</strong>${p.quantidade} item(ns) · R$ ${Number(p.total).toFixed(2)} · ${esc(p.formaPagamento)}</div>`).join('') : '<p>Nenhum pedido ainda.</p>';
      const c = await api('catalogo');
      options($('#pet'), conta.pets, p => p.petNome);
      options($('#servico'), c.servicos, s => `${s.nome} — R$ ${Number(s.preco).toFixed(2)}`);
      options($('#unidade'), c.unidades, u => u.nome);
      options($('#produto'), c.produtos, p => `${p.nome} — R$ ${Number(p.valorVenda).toFixed(2)}`);
      await horarios();
    } catch (e) {
      localStorage.removeItem('lanePetsClienteToken');
      token = '';
      exibirPortal(false);
    }
  }

  async function horarios() {
    if (!token || !$('#data').value || !$('#unidade').value) return;
    const rows = await api(`horarios?unidade=${encodeURIComponent($('#unidade').value)}&data=${$('#data').value}`);
    $('#horario').innerHTML = rows.length ? rows.map(x => `<option>${x}</option>`).join('') : '<option>Nenhum horário livre</option>';
  }

  /* ---------------------------------------------------------------------
     Entrar
     --------------------------------------------------------------------- */
  formLogin.addEventListener('submit', async event => {
    event.preventDefault();
    UI.limpar(formLogin);
    UI.alerta('#login-aviso', '');
    UI.alerta('#login-info', '');

    const campoEmail = $('#login-email'), campoSenha = $('#login-senha');
    const email = campoEmail.value.trim();
    const senha = campoSenha.value;

    let valido = true;
    if (!email) valido = UI.erro(campoEmail, 'Informe seu e-mail.');
    else if (!UI.EMAIL_RE.test(email)) valido = UI.erro(campoEmail, 'Digite um e-mail válido, como nome@email.com.');
    if (!senha) valido = UI.erro(campoSenha, 'Informe sua senha.');
    if (!valido) { UI.alerta('#login-aviso', 'Revise os campos destacados para continuar.'); return; }

    UI.carregando('#login', true, 'Entrando…');
    try {
      const d = await api('login', { method: 'POST', body: JSON.stringify({ email, senha }) });
      token = d.token;
      localStorage.setItem('lanePetsClienteToken', token);
      await abrir();
    } catch (e) {
      UI.alerta('#login-aviso', e.message || 'Não foi possível entrar.');
      UI.erro(campoSenha, 'Verifique seus dados e tente novamente.');
      campoSenha.value = '';
      campoSenha.focus();
    } finally {
      UI.carregando('#login', false);
    }
  });

  /* ---------------------------------------------------------------------
     Criar conta
     --------------------------------------------------------------------- */
  formCadastro.addEventListener('submit', async event => {
    event.preventDefault();
    UI.limpar(formCadastro);
    UI.alerta('#cad-aviso', '');

    const cNome = $('#cad-nome'), cEmail = $('#cad-email'), cTel = $('#cad-tel');
    const cSenha = $('#cad-senha'), cSenha2 = $('#cad-senha2'), cPet = $('#cad-pet'), cTipo = $('#cad-tipo');

    const nome = cNome.value.trim(), email = cEmail.value.trim(), telefone = cTel.value.trim();
    const senha = cSenha.value, senha2 = cSenha2.value;
    const pet = cPet.value.trim(), tipo = cTipo.value.trim();

    let valido = true;
    if (nome.length < 3) valido = UI.erro(cNome, 'Informe seu nome completo (mínimo 3 letras).');
    if (!email) valido = UI.erro(cEmail, 'Informe seu e-mail.');
    else if (!UI.EMAIL_RE.test(email)) valido = UI.erro(cEmail, 'Digite um e-mail válido, como nome@email.com.');
    if (telefone.replace(/\D/g, '').length < 10) valido = UI.erro(cTel, 'Informe um telefone com DDD.');
    if (senha.length < 8) valido = UI.erro(cSenha, 'A senha precisa ter ao menos 8 caracteres.');
    if (!senha2) valido = UI.erro(cSenha2, 'Repita a senha para confirmar.');
    else if (senha !== senha2) valido = UI.erro(cSenha2, 'As senhas não são iguais.');
    else if (senha.length >= 8) UI.ok(cSenha2);
    if (pet.length < 2) valido = UI.erro(cPet, 'Informe o nome do seu pet.');
    if (!valido) { UI.alerta('#cad-aviso', 'Revise os campos destacados para criar sua conta.'); return; }

    UI.carregando('#cadastro', true, 'Criando conta…');
    try {
      await api('cadastro', { method: 'POST', body: JSON.stringify({ nome, email, senha, telefone, pet, tipo }) });
      /* Conta criada — o cliente segue para o login, conforme o fluxo definido. */
      formCadastro.reset();
      UI.limpar(formCadastro);
      $('#forca-senha').dataset.nivel = '0';
      $('#forca-texto').textContent = NIVEIS[0];
      mostrarPainel('sucesso');
      $('#login-email').value = email;
    } catch (e) {
      UI.alerta('#cad-aviso', e.message || 'Não foi possível criar a conta.');
    } finally {
      UI.carregando('#cadastro', false);
    }
  });

  /* ---------------------------------------------------------------------
     Ações do portal (inalteradas)
     --------------------------------------------------------------------- */
  $('#data').onchange = horarios;
  $('#unidade').onchange = horarios;

  $('#confirmar').onclick = async () => {
    try {
      await api('agendamentos', { method: 'POST', body: JSON.stringify({ petId: $('#pet').value, servicoId: $('#servico').value, unidade: $('#unidade').value, data: $('#data').value, horario: $('#horario').value, transporte: $('#transporte').value, formaPagamento: $('#pagamento').value, observacao: $('#obs').value }) });
      $('#agenda-msg').textContent = 'Agendamento confirmado!';
      abrir();
    } catch (e) { message('#agenda-msg', e); }
  };

  $('#criar-pedido').onclick = async () => {
    try {
      await api('pedidos', { method: 'POST', body: JSON.stringify({ produtoId: $('#produto').value, quantidade: Number($('#quantidade').value), formaPagamento: $('#pedido-pagamento').value }) });
      $('#pedido-msg').textContent = 'Pedido recebido!';
      abrir();
    } catch (e) { message('#pedido-msg', e); }
  };

  $('#sair').onclick = async () => {
    try { await api('logout', { method: 'POST' }); }
    finally { localStorage.removeItem('lanePetsClienteToken'); location.href = 'cliente.html'; }
  };

  if (token) abrir();
})();
