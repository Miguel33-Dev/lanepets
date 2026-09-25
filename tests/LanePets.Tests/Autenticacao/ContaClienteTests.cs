using LanePets.Tests.Infra;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// Cadastro e login da area do cliente (POST /api/cliente/cadastro e /login) e a sessao
/// pelo header X-LanePets-Client. Regras: CONTEXTO §6.5 (identidade vem da sessao),
/// §6.8 (BCrypt), §6.14/§6.23 (validacao em um lugar so; senha 8+ com letra e numero).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class ContaClienteTests(LanePetsApp app)
{
    private static object Cadastro(string? nome = "Maria Teste", string? email = null, string? senha = "Senha123",
        string? telefone = "(11) 98765-4321", string? pet = "Rex")
        => new { nome, email = email ?? Api.NovoEmail(), senha, telefone, endereco = "Rua A, 1", pet, tipo = "Cachorro", raca = "SRD" };

    // ------------------------------------------------------------------ cadastro

    [Fact]
    public async Task Cadastro_valido_cria_cliente_pet_e_acesso_e_ja_devolve_sessao()
    {
        var api = app.Api();
        var email = Api.NovoEmail("Maria");   // maiusculas de proposito

        var r = await api.Post("/api/cliente/cadastro", Cadastro(email: email));

        Assert.Equal(200, r.Codigo);
        Assert.Equal(email.ToLowerInvariant(), r.Texto("cliente", "email"));
        Assert.Equal("Maria Teste", r.Texto("cliente", "nome"));

        api.TokenCliente = r.Texto("token");
        var conta = await api.Get("/api/cliente/conta");
        Assert.Equal(200, conta.Codigo);
        Assert.Equal("Maria Teste", conta.Texto("nome"));
        Assert.Equal(1, conta.Data.GetProperty("pets").GetArrayLength());
    }

    [Fact]
    public async Task Senha_fica_guardada_so_como_hash_bcrypt()
    {
        var api = app.Api();
        var criado = await api.CadastrarCliente(senha: "Segredo123");

        var usuario = await app.NoBanco(db => Task.FromResult(db.UsuariosClientes.Single(u => u.Email == criado.Email)));

        Assert.NotEqual("Segredo123", usuario.SenhaHash);
        Assert.StartsWith("$2", usuario.SenhaHash);
        Assert.DoesNotContain("Segredo123", usuario.SenhaHash + usuario.SenhaSalt);
    }

    [Fact]
    public async Task Cadastro_vira_evento_no_log()
    {
        var criado = await app.Api().CadastrarCliente();

        var eventos = await app.NoBanco(db => Task.FromResult(
            db.EventosLog.Where(e => e.Acao == "Conta de cliente criada" && e.Autor == criado.Email).ToList()));

        Assert.Single(eventos);
    }

    [Fact]
    public async Task Email_repetido_e_recusado()
    {
        var api = app.Api();
        var criado = await api.CadastrarCliente();

        var r = await api.Post("/api/cliente/cadastro", Cadastro(email: criado.Email.ToUpperInvariant()));

        Assert.Equal(400, r.Codigo);
        Assert.Equal("Já existe uma conta com este e-mail.", r.Erro);
    }

    [Theory]
    [InlineData("nome", "Jo", "nome completo")]
    [InlineData("email", "nao-e-email", "e-mail válido")]
    [InlineData("senha", "abc12", "ao menos")]
    [InlineData("senha", "somenteletras", "letra e um número")]
    [InlineData("senha", "12345678", "letra e um número")]
    [InlineData("telefone", "123", "telefone")]
    [InlineData("telefone", "", "telefone")]
    [InlineData("pet", "", "nome do pet")]
    public async Task Cadastro_invalido_e_recusado_com_mensagem_e_nao_grava_nada(string campo, string valor, string trechoDaMensagem)
    {
        var email = campo == "email" ? valor : Api.NovoEmail("invalido");
        var corpo = campo switch
        {
            "nome" => Cadastro(nome: valor, email: email),
            "email" => Cadastro(email: email),
            "senha" => Cadastro(senha: valor, email: email),
            "telefone" => Cadastro(telefone: valor, email: email),
            "pet" => Cadastro(pet: valor, email: email),
            _ => throw new ArgumentException(campo)
        };

        var r = await app.Api().Post("/api/cliente/cadastro", corpo);

        Assert.Equal(400, r.Codigo);
        Assert.Equal("ERR-4000", r.CodigoErro);
        Assert.Contains(trechoDaMensagem, r.Erro, StringComparison.OrdinalIgnoreCase);

        var gravou = await app.NoBanco(db => Task.FromResult(db.UsuariosClientes.Any(u => u.Email == email.ToLower())));
        Assert.False(gravou, "Cadastro recusado não pode deixar acesso gravado.");
    }

    // ------------------------------------------------------------------ login

    [Fact]
    public async Task Login_com_a_senha_do_cadastro_abre_nova_sessao()
    {
        var api = app.Api();
        var criado = await api.CadastrarCliente();
        api.TokenCliente = null;

        var r = await api.Post("/api/cliente/login", new { email = "  " + criado.Email.ToUpperInvariant(), senha = criado.Senha });

        Assert.Equal(200, r.Codigo);
        Assert.Equal(criado.Email, r.Texto("cliente", "email"));
        Assert.NotEqual(criado.Token, r.Texto("token"));

        api.TokenCliente = r.Texto("token");
        Assert.Equal(200, (await api.Get("/api/cliente/conta")).Codigo);
    }

    [Fact]
    public async Task Senha_errada_e_email_inexistente_recebem_a_mesma_mensagem()
    {
        var api = app.Api();
        var criado = await api.CadastrarCliente();

        var senhaErrada = await api.Post("/api/cliente/login", new { email = criado.Email, senha = "Errada999" });
        var semConta = await api.Post("/api/cliente/login", new { email = Api.NovoEmail("semconta"), senha = "Errada999" });

        foreach (var r in new[] { senhaErrada, semConta })
        {
            Assert.Equal(400, r.Codigo);
            Assert.Equal("E-mail ou senha inválidos.", r.Erro);
        }

        var recusas = await app.NoBanco(db => Task.FromResult(
            db.EventosLog.Where(e => e.Acao == "Login de cliente recusado" && e.Autor == criado.Email).ToList()));
        Assert.Equal("Senha incorreta.", Assert.Single(recusas).Detalhes);
    }

    // ------------------------------------------------------------------ sessao

    [Theory]
    [InlineData(null)]
    [InlineData("token-inventado")]
    public async Task Area_do_cliente_sem_sessao_valida_responde_401(string? token)
    {
        var api = app.Api();
        api.TokenCliente = token;

        var r = await api.Get("/api/cliente/conta");

        Assert.Equal(401, r.Codigo);
        Assert.Equal("ERR-4010", r.CodigoErro);
    }

    [Fact]
    public async Task Logout_encerra_a_sessao_do_cliente()
    {
        var api = app.Api();
        await api.CadastrarCliente();

        Assert.Equal(200, (await api.Post("/api/cliente/logout")).Codigo);
        Assert.Equal(401, (await api.Get("/api/cliente/conta")).Codigo);
    }

    [Fact]
    public async Task Token_de_cliente_nao_abre_o_painel()
    {
        var criado = await app.Api().CadastrarCliente();

        var r = await app.Api().Get($"/api/admin/me?token={criado.Token}");

        Assert.Equal(401, r.Codigo);
    }

    [Fact]
    public async Task Cada_cliente_so_ve_a_propria_conta()
    {
        var ana = app.Api();
        var bruno = app.Api();
        var contaAna = await ana.CadastrarCliente(nome: "Ana Teste", pet: "Mel");
        await bruno.CadastrarCliente(nome: "Bruno Teste", pet: "Thor");

        var r = await bruno.Get("/api/cliente/conta");

        Assert.Equal("Bruno Teste", r.Texto("nome"));
        Assert.NotEqual(contaAna.Email, r.Texto("email"));
        Assert.Equal(1, r.Data.GetProperty("pets").GetArrayLength());
    }
}
