using LanePets.Tests.Infra;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// Segurança — Etapa 2 da auditoria (29/09): força bruta e limite de requisições.
/// Antes, 12 senhas erradas seguidas no painel não bloqueavam nada. Agora cada conta aceita poucas
/// senhas erradas numa janela; passou disso, fica bloqueada (429 ERR-4290) até a janela acabar, mesmo com
/// a senha certa. As rotas sensíveis também têm limite de requisições por IP.
/// A aplicação principal dos testes deixa as travas folgadas; aqui elas são ligadas em aplicações derivadas
/// (mesmo banco), como o GoogleFalso faz com o Google.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class ForcaBrutaTests(LanePetsApp app)
{
    private static readonly object Trava = new();
    private static WebApplicationFactory<Program>? travada, limitada;

    /// <summary>Trava de senha: 3 erradas bloqueiam a conta. Limite de requisições folgado.</summary>
    private Api ApiTravada()
    {
        lock (Trava)
            travada ??= app.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LanePets:Seguranca:TentativasPorConta"] = "3",
                ["LanePets:Seguranca:TentativasPorIp"] = "1000",
                ["LanePets:Seguranca:BloqueioMinutos"] = "15",
                ["LanePets:Seguranca:LimitarRequisicoes"] = "true",
                ["LanePets:Seguranca:RequisicoesPorMinuto"] = "100000",
                ["LanePets:Seguranca:RequisicoesSensiveisPorMinuto"] = "100000"
            })));
        return new Api(travada.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
    }

    /// <summary>Limite de requisições: 3 por minuto nas rotas sensíveis.</summary>
    private Api ApiLimitada()
    {
        lock (Trava)
            limitada ??= app.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LanePets:Seguranca:LimitarRequisicoes"] = "true",
                ["LanePets:Seguranca:RequisicoesPorMinuto"] = "100000",
                ["LanePets:Seguranca:RequisicoesSensiveisPorMinuto"] = "3"
            })));
        return new Api(limitada.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
    }

    private static Task<Resposta> LoginCliente(Api api, string email, string senha)
        => api.Post("/api/cliente/login", new { email, senha });

    [Fact]
    public async Task Cliente_fica_bloqueado_depois_de_3_senhas_erradas_ate_com_a_senha_certa()
    {
        var api = ApiTravada();
        var conta = await api.CadastrarCliente(senha: "Certa1234");

        for (var i = 1; i <= 3; i++)
        {
            var errada = await LoginCliente(api, conta.Email, "Errada" + i + "x");
            Assert.Equal(400, errada.Codigo);
        }

        var certa = await LoginCliente(api, conta.Email, "Certa1234");
        Assert.Equal(429, certa.Codigo);
        Assert.Equal("ERR-4290", certa.CodigoErro);
        Assert.Contains("aguarde", certa.Erro, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(certa.Http.Headers.RetryAfter);

        var evento = await app.NoBanco(db => Task.FromResult(db.EventosLog.Any(e =>
            e.Acao == "Acesso bloqueado por excesso de tentativas" && e.Autor == conta.Email)));
        Assert.True(evento);
    }

    [Fact]
    public async Task Bloquear_uma_conta_nao_atrapalha_outra()
    {
        var api = ApiTravada();
        var bloqueada = await api.CadastrarCliente(senha: "Certa1234");
        var outra = await api.CadastrarCliente(senha: "Outra1234");

        for (var i = 1; i <= 3; i++) await LoginCliente(api, bloqueada.Email, "Errada" + i + "x");
        Assert.Equal(429, (await LoginCliente(api, bloqueada.Email, "Certa1234")).Codigo);

        var r = await LoginCliente(api, outra.Email, "Outra1234");
        Assert.True(r.Codigo == 200, r.ToString());
    }

    [Fact]
    public async Task Acertar_a_senha_zera_o_contador()
    {
        var api = ApiTravada();
        var conta = await api.CadastrarCliente(senha: "Certa1234");

        for (var rodada = 0; rodada < 3; rodada++)
        {
            await LoginCliente(api, conta.Email, "Errada1x");
            await LoginCliente(api, conta.Email, "Errada2x");
            var r = await LoginCliente(api, conta.Email, "Certa1234");
            Assert.True(r.Codigo == 200, $"rodada {rodada}: {r}");
        }
    }

    [Fact]
    public async Task Painel_bloqueia_por_email_e_o_administrador_verdadeiro_continua_entrando()
    {
        var api = ApiTravada();
        var tentativa = Api.NovoEmail("ataque");

        for (var i = 1; i <= 3; i++)
            Assert.Equal(400, (await api.Post("/api/login", new { email = tentativa, senha = "Chute" + i + "x" })).Codigo);

        var bloqueado = await api.Post("/api/login", new { email = tentativa, senha = "Chute4x" });
        Assert.Equal(429, bloqueado.Codigo);
        Assert.Equal("ERR-4290", bloqueado.CodigoErro);

        // Mesmo IP, outra conta: o admin de verdade entra normalmente.
        var token = await api.LoginAdmin();
        Assert.False(string.IsNullOrEmpty(token));
    }

    [Fact]
    public async Task Rota_sensivel_tem_limite_de_requisicoes_e_o_resto_do_site_segue_normal()
    {
        var api = ApiLimitada();
        var email = Api.NovoEmail("limite");

        for (var i = 1; i <= 3; i++)
        {
            var ok = await api.Post("/api/cliente/senha/esqueci", new { email });
            Assert.True(ok.Codigo == 200, $"pedido {i}: {ok}");
        }

        var barrado = await api.Post("/api/cliente/senha/esqueci", new { email });
        Assert.Equal(429, barrado.Codigo);
        Assert.Equal("ERR-4290", barrado.CodigoErro);
        Assert.False(barrado.Ok);

        var publico = await api.Get("/api/public/servicos");
        Assert.Equal(200, publico.Codigo);
    }
}
