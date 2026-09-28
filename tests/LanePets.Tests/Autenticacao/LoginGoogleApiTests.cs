using LanePets.Services;
using LanePets.Tests.Infra;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// Tarefa 1, etapa 4: POST /api/cliente/google de ponta a ponta (token -> conta -> sessao -> Minha Conta),
/// com o "Google" de mentira da <see cref="GoogleFalso"/>.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class LoginGoogleApiTests(LanePetsApp app)
{
    private Task<int> Eventos(string acao, string autor)
        => app.NoBanco(db => db.EventosLog.CountAsync(e => e.Acao == acao && e.Autor == autor));

    [Fact]
    public async Task Com_o_google_desligado_o_endpoint_recusa()
    {
        var r = await app.Api().Post("/api/cliente/google", new { credential = GoogleFalso.Token(Api.NovoEmail(), GoogleFalso.NovoSub()) });
        Assert.Equal(400, r.Codigo);
        Assert.Contains("indisponível", r.Erro);
    }

    [Fact]
    public async Task Cliente_novo_entra_ganha_sessao_e_ve_a_minha_conta()
    {
        var api = GoogleFalso.Api(app);
        var email = Api.NovoEmail("google");
        var r = await api.Post("/api/cliente/google", new { credential = GoogleFalso.Token(email, GoogleFalso.NovoSub(), "Ana Google") });

        Assert.True(r.Codigo == 200, r.ToString());
        Assert.True(r.Data.GetProperty("novo").GetBoolean());
        Assert.True(r.Data.GetProperty("completarCadastro").GetBoolean());
        Assert.Equal("Ana Google", r.Texto("cliente", "nome"));

        api.TokenCliente = r.Texto("token");
        var conta = await api.Get("/api/cliente/conta");
        Assert.True(conta.Codigo == 200, conta.ToString());
        Assert.Equal(email, conta.Texto("email"));
        Assert.True(conta.Data.GetProperty("googleVinculado").GetBoolean());
        Assert.False(conta.Data.GetProperty("temSenha").GetBoolean());
        Assert.Equal(0, conta.Data.GetProperty("pets").GetArrayLength());

        Assert.Equal(1, await Eventos("Conta de cliente criada", email));
        Assert.Equal(1, await Eventos("Login de cliente", email));
    }

    [Fact]
    public async Task Segunda_entrada_cai_na_mesma_conta()
    {
        var api = GoogleFalso.Api(app);
        var (email, sub) = (Api.NovoEmail("google"), GoogleFalso.NovoSub());
        var primeira = await api.Post("/api/cliente/google", new { credential = GoogleFalso.Token(email, sub) });
        var segunda = await api.Post("/api/cliente/google", new { credential = GoogleFalso.Token(email, sub) });

        Assert.Equal(200, segunda.Codigo);
        Assert.False(segunda.Data.GetProperty("novo").GetBoolean());
        Assert.NotEqual(primeira.Texto("token"), segunda.Texto("token"));
        Assert.Equal(1, await app.NoBanco(db => db.UsuariosClientes.CountAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task Conta_com_senha_pede_vinculacao_e_so_vincula_com_a_senha_certa()
    {
        var api = GoogleFalso.Api(app);
        var conta = await api.CadastrarCliente();
        api.TokenCliente = null;
        var credential = GoogleFalso.Token(conta.Email, GoogleFalso.NovoSub());

        var pede = await api.Post("/api/cliente/google", new { credential });
        Assert.True(pede.Codigo == 200, pede.ToString());
        Assert.True(pede.Data.GetProperty("vincular").GetBoolean());
        Assert.False(pede.Data.TryGetProperty("token", out _));

        var errada = await api.Post("/api/cliente/google", new { credential, senha = "SenhaErrada9" });
        Assert.Equal(400, errada.Codigo);
        Assert.Equal(1, await Eventos("Vinculação do Google recusada", conta.Email));

        var certa = await api.Post("/api/cliente/google", new { credential, senha = conta.Senha });
        Assert.True(certa.Codigo == 200, certa.ToString());
        Assert.False(certa.Data.GetProperty("novo").GetBoolean());
        Assert.False(certa.Data.GetProperty("completarCadastro").GetBoolean());   // cadastro normal ja tem telefone e pet
        Assert.Equal(1, await Eventos("Conta Google vinculada", conta.Email));

        var depois = await api.Post("/api/cliente/google", new { credential });
        Assert.True(depois.Data.TryGetProperty("token", out _), depois.ToString());
    }

    [Fact]
    public async Task Token_invalido_e_recusado_com_mensagem_unica_e_evento_sem_o_token()
    {
        var api = GoogleFalso.Api(app);
        var email = Api.NovoEmail("google");
        var deOutroApp = GoogleFalso.Token(email, GoogleFalso.NovoSub(), ajuste: c => c["aud"] = "999-outro.apps.googleusercontent.com");

        foreach (var credential in new[] { deOutroApp, "lixo", "" })
        {
            var r = await api.Post("/api/cliente/google", new { credential });
            Assert.Equal(400, r.Codigo);
            Assert.Equal(ContaGoogleService.MensagemRecusa, r.Erro);
        }
        Assert.False(await app.NoBanco(db => db.UsuariosClientes.AnyAsync(u => u.Email == email)));
        var assinatura = deOutroApp.Split('.')[2][..20];
        Assert.False(await app.NoBanco(db => db.EventosLog.AnyAsync(e => e.Detalhes.Contains(assinatura))));
        Assert.True(await app.NoBanco(db => db.EventosLog.AnyAsync(e => e.Acao == "Login com Google recusado" && e.Detalhes.Contains("aud"))));
    }

    [Fact]
    public async Task Sessao_do_google_sai_pelo_logout_de_sempre()
    {
        var api = GoogleFalso.Api(app);
        var r = await api.Post("/api/cliente/google", new { credential = GoogleFalso.Token(Api.NovoEmail("google"), GoogleFalso.NovoSub()) });
        api.TokenCliente = r.Texto("token");
        Assert.Equal(200, (await api.Get("/api/cliente/conta")).Codigo);

        Assert.Equal(200, (await api.Post("/api/cliente/logout")).Codigo);
        Assert.Equal(401, (await api.Get("/api/cliente/conta")).Codigo);
    }
}
