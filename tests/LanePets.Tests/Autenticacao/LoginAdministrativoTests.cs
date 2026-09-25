using LanePets.Tests.Infra;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// Login do painel (POST /api/login) e sessao administrativa.
/// Regras conferidas: CONTEXTO §6.3 (envelope), §6.4 (401 sem sessao), §6.25 (login
/// recusado responde mensagem generica — nao revela se o e-mail existe).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class LoginAdministrativoTests(LanePetsApp app)
{
    [Fact]
    public async Task Health_responde_no_envelope()
    {
        var r = await app.Api().Get("/api/health");

        Assert.Equal(200, r.Codigo);
        Assert.True(r.Ok, r.ToString());
    }

    [Fact]
    public async Task Administrador_inicial_entra_e_recebe_token_e_cookie()
    {
        var r = await app.Api().Post("/api/login", new { email = Api.AdminEmail, senha = Api.AdminSenha });

        Assert.Equal(200, r.Codigo);
        Assert.True(r.Data.GetProperty("autenticado").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(r.Texto("token")));
        Assert.Contains(r.Http.Headers.GetValues("Set-Cookie"), c => c.StartsWith("lanePetsAdmin="));
    }

    [Fact]
    public async Task Email_com_maiusculas_e_espacos_tambem_entra()
    {
        var r = await app.Api().Post("/api/login", new { email = "  ADMIN@Gmail.com ", senha = Api.AdminSenha });

        Assert.Equal(200, r.Codigo);
    }

    [Fact]
    public async Task Senha_errada_e_email_inexistente_recebem_a_mesma_mensagem()
    {
        var api = app.Api();
        var senhaErrada = await api.Post("/api/login", new { email = Api.AdminEmail, senha = "senha-errada-1" });
        var emailInexistente = await api.Post("/api/login", new { email = Api.NovoEmail("ninguem"), senha = "qualquer1" });

        foreach (var r in new[] { senhaErrada, emailInexistente })
        {
            Assert.Equal(400, r.Codigo);
            Assert.False(r.Ok);
            Assert.Equal("ERR-4000", r.CodigoErro);
            Assert.Equal("E-mail ou senha inválidos.", r.Erro);
            Assert.False(r.Corpo.TryGetProperty("data", out _), "Resposta de erro não pode trazer data.");
        }
    }

    [Fact]
    public async Task Sem_email_pede_email_e_senha()
    {
        var r = await app.Api().Post("/api/login", new { email = "", senha = "123456" });

        Assert.Equal(400, r.Codigo);
        Assert.Equal("Informe e-mail e senha.", r.Erro);
    }

    [Fact]
    public async Task Login_recusado_vira_evento_com_o_motivo_real()
    {
        var email = Api.NovoEmail("intruso");
        await app.Api().Post("/api/login", new { email, senha = "qualquer1" });

        var evento = await app.NoBanco(db => Task.FromResult(
            db.EventosLog.Where(e => e.Acao == "Login administrativo recusado" && e.Autor == email).ToList()));

        var unico = Assert.Single(evento);
        Assert.Equal("E-mail não cadastrado.", unico.Detalhes);
    }

    [Fact]
    public async Task Admin_me_devolve_o_usuario_da_sessao()
    {
        var api = app.Api();
        var token = await api.LoginAdmin();

        var r = await api.Get($"/api/admin/me?token={token}");

        Assert.Equal(200, r.Codigo);
        Assert.Equal(Api.AdminEmail, r.Texto("usuario", "email"));
        Assert.True(r.Data.GetProperty("usuario").GetProperty("adminGeral").GetBoolean());
    }

    [Theory]
    [InlineData("/api/admin/me")]
    [InlineData("/api/admin/me?token=token-inventado")]
    [InlineData("/api/admin/clientes")]
    [InlineData("/api/admin/produtos?token=token-inventado")]
    public async Task Sem_sessao_valida_o_painel_responde_401(string caminho)
    {
        var r = await app.Api().Get(caminho);

        Assert.Equal(401, r.Codigo);
        Assert.Equal("ERR-4010", r.CodigoErro);
    }

    [Fact]
    public async Task Logout_invalida_o_token()
    {
        var api = app.Api();
        var token = await api.LoginAdmin();

        var sair = await api.Post("/api/logout", new { token });
        var depois = await api.Get($"/api/admin/me?token={token}");

        Assert.Equal(200, sair.Codigo);
        Assert.Equal(401, depois.Codigo);
    }
}
