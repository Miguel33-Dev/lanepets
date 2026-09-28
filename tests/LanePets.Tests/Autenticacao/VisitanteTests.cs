using LanePets.Services;
using LanePets.Tests.Infra;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// 29/09: acesso de visitante (vitrine). Desligado por padrao; ligado, a conta so VISUALIZA — toda gravacao e 403.
/// A aplicacao de teste sobe sem LanePets:Visitante, entao a conta e ligada/desligada direto pelo Visitante.GarantirAsync.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class VisitanteTests(LanePetsApp app)
{
    private static IConfiguration Config(bool ligado)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["LanePets:Visitante"] = ligado ? "true" : "false" }).Build();

    [Fact]
    public async Task Sem_a_chave_o_botao_de_visitante_nao_entra()
    {
        var api = app.Api();
        Assert.False((await api.Get("/api/health")).Data.GetProperty("visitante").GetBoolean());
        Assert.Equal(400, (await api.Post("/api/login/visitante")).Codigo);
    }

    [Fact]
    public async Task Visitante_ve_o_painel_mas_nao_grava_nada()
    {
        await app.NoBanco(async db => { await Visitante.GarantirAsync(db, Config(true), NullLogger.Instance); return 0; });
        try
        {
            var token = app.Services.GetRequiredService<SessionService>().CreateAdmin(Visitante.Id);
            var api = app.Api();

            var eu = await api.Get($"/api/admin/me?token={token}");
            Assert.True(eu.Codigo == 200, eu.ToString());
            Assert.True(eu.Data.GetProperty("usuario").GetProperty("visitante").GetBoolean());
            Assert.False(eu.Data.GetProperty("usuario").GetProperty("adminGeral").GetBoolean());
            var modulos = eu.Data.GetProperty("modulos");
            Assert.True(modulos.GetProperty("agendamentos").GetProperty("visualizar").GetBoolean());
            Assert.False(modulos.GetProperty("agendamentos").GetProperty("editar").GetBoolean());
            Assert.False(modulos.GetProperty("usuarios").GetProperty("visualizar").GetBoolean());

            Assert.Equal(200, (await api.Get($"/api/admin/unidades?token={token}")).Codigo);                       // le
            Assert.Equal(403, (await api.Post("/api/admin/unidades", new { token, nome = "Invasao Visitante", capacidade = 1 })).Codigo);   // nao grava
            Assert.Equal(403, (await api.Get($"/api/admin/usuarios?token={token}")).Codigo);                       // area do Geral fechada
        }
        finally
        {
            await app.NoBanco(async db => { await Visitante.GarantirAsync(db, Config(false), NullLogger.Instance); return 0; });
        }
        Assert.False(await app.NoBanco(db => Task.FromResult(db.UsuariosAdministradores.Single(u => u.Id == Visitante.Id).Ativo)));
    }
}
