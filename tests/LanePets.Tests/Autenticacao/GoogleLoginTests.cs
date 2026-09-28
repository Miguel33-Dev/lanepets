using LanePets.Services;
using LanePets.Tests.Infra;
using Microsoft.Extensions.Configuration;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// Tarefa 1 (Login com Google), etapa 1: configuracao. Sem Client ID o botao nao aparece;
/// Client ID fora do formato do Google conta como desligado (falha fechada).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class GoogleLoginTests(LanePetsApp app)
{
    private static IConfiguration Config(string? clientId)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [GoogleLogin.Chave] = clientId }).Build();

    [Fact]
    public async Task Sem_client_id_o_login_com_google_fica_desligado()
    {
        var r = await app.Api().Get("/api/cliente/google/config");
        Assert.True(r.Codigo == 200, r.ToString());
        Assert.False(r.Data.GetProperty("ativo").GetBoolean());
        Assert.Equal("", r.Data.GetProperty("clientId").GetString());
        Assert.False((await app.Api().Get("/api/health")).Data.GetProperty("google").GetBoolean());   // etapa 6
    }

    [Theory]
    [InlineData("123456789-abc123.apps.googleusercontent.com", true)]
    [InlineData("  123456789-abc123.apps.googleusercontent.com  ", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("coloque-aqui", false)]
    [InlineData(".apps.googleusercontent.com", false)]
    [InlineData("123 456.apps.googleusercontent.com", false)]
    public void Client_id_so_vale_com_o_formato_do_google(string? valor, bool ativo)
    {
        Assert.Equal(ativo, GoogleLogin.Ativo(Config(valor)));
        if (ativo) Assert.Equal(valor!.Trim(), GoogleLogin.ClientId(Config(valor)));
    }
}
