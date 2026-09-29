using LanePets.Models;
using LanePets.Services;
using LanePets.Tests.Infra;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// 30/09: o acesso de visitante ao painel (vitrine de 29/09) foi REMOVIDO a pedido do Fabrício — quem não é da
/// equipe não vê nada do painel. A tela de login também não mostra mais as credenciais de teste.
/// Mesmo com a chave antiga LanePets:Visitante=true (que ele tinha no appsettings.Development.json), nada volta.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class VisitanteTests(LanePetsApp app)
{
    [Fact]
    public async Task Entrar_como_visitante_nao_existe_mais_nem_com_a_chave_antiga_ligada()
    {
        using var comChave = app.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["LanePets:Visitante"] = "true" })));
        var api = new Api(comChave.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

        Assert.Equal(404, (await api.Post("/api/login/visitante")).Codigo);
        Assert.False((await api.Get("/api/health")).Data.TryGetProperty("visitante", out _));
    }

    [Fact]
    public async Task Tela_de_login_do_painel_nao_mostra_visitante_nem_senha_de_exemplo()
    {
        var html = await app.CreateClient().GetStringAsync("/admin-login.html");
        Assert.DoesNotContain("visitante", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Credenciais de teste", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456", html);
    }

    [Fact]
    public async Task Conta_de_visitante_antiga_e_desativada_e_nao_entra()
    {
        await app.NoBanco(async db =>
        {
            var conta = db.UsuariosAdministradores.FirstOrDefault(u => u.Id == Visitante.Id);
            if (conta is null)
                db.UsuariosAdministradores.Add(new UsuarioAdministrador { Id = Visitante.Id, Email = "visitante@lanepets.demo", Nome = "Visitante", Perfil = PerfilAdmin.Comum, Ativo = true });
            else conta.Ativo = true;
            await db.SaveChangesAsync();
            await Visitante.DesativarAsync(db, NullLogger.Instance);
            return true;
        });
        Assert.False(await app.NoBanco(db => Task.FromResult(db.UsuariosAdministradores.Single(u => u.Id == Visitante.Id).Ativo)));
    }
}
