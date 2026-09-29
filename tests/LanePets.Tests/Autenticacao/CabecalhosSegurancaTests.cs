using System.Text;
using System.Text.Json;
using LanePets.Services;
using LanePets.Tests.Infra;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// Segurança — Etapa 3 da auditoria (29/09): cabeçalhos de segurança, cookie Secure, limite de tamanho,
/// /api/health enxuto e nome curto do tutor nas avaliações públicas.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class CabecalhosSegurancaTests(LanePetsApp app)
{
    private HttpClient Cliente(string baseUrl = "http://localhost")
        => app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri(baseUrl) });

    private static string Cabecalho(HttpResponseMessage r, string nome)
        => r.Headers.TryGetValues(nome, out var v) ? string.Join(",", v)
         : r.Content.Headers.TryGetValues(nome, out var c) ? string.Join(",", c) : "";

    [Fact]
    public async Task Pagina_API_e_erro_saem_com_os_cabecalhos_de_seguranca_e_sem_Server()
    {
        var http = Cliente();
        foreach (var caminho in new[] { "/", "/admin-login.html", "/api/health", "/api/rota-que-nao-existe" })
        {
            var r = await http.GetAsync(caminho);
            var csp = Cabecalho(r, "Content-Security-Policy");
            Assert.Contains("default-src 'self'", csp);
            Assert.Contains("frame-ancestors 'none'", csp);
            Assert.Contains("object-src 'none'", csp);
            Assert.Contains("https://accounts.google.com/gsi/client", csp);   // o login com Google continua abrindo
            Assert.Equal("nosniff", Cabecalho(r, "X-Content-Type-Options"));
            Assert.Equal("DENY", Cabecalho(r, "X-Frame-Options"));
            Assert.Equal("strict-origin-when-cross-origin", Cabecalho(r, "Referrer-Policy"));
            Assert.Contains("camera=()", Cabecalho(r, "Permissions-Policy"));
            Assert.Equal("", Cabecalho(r, "Server"));
        }
    }

    [Fact]
    public async Task Health_so_devolve_o_necessario()
    {
        var r = await app.Api().Get("/api/health");
        Assert.True(r.Codigo == 200, r.ToString());
        var campos = r.Data.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "demo", "google", "ok" }, campos);
    }

    [Fact]
    public async Task Envio_acima_de_2_MB_e_recusado_com_413_mas_a_importacao_tem_teto_proprio()
    {
        var http = Cliente();
        var gigante = "{\"email\":\"a@b.com\",\"senha\":\"" + new string('x', 2 * 1024 * 1024 + 10) + "\"}";

        var r = await http.PostAsync("/api/cliente/login", new StringContent(gigante, Encoding.UTF8, "application/json"));
        Assert.Equal(413, (int)r.StatusCode);
        var corpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ERR-4130", corpo.GetProperty("codigo").GetString());
        Assert.Contains("grande demais", corpo.GetProperty("error").GetString());

        // Mesmo tamanho na importação (banco inteiro do navegador): passa do limite e para na permissão.
        var importacao = await http.PostAsync("/api/admin/importar", new StringContent(gigante, Encoding.UTF8, "application/json"));
        Assert.NotEqual(413, (int)importacao.StatusCode);

        // Envio normal continua passando.
        var normal = await app.Api().Post("/api/cliente/login", new { email = "ninguem@exemplo.com", senha = "Errada123" });
        Assert.NotEqual(413, normal.Codigo);
    }

    [Fact]
    public async Task Cookie_do_painel_e_Secure_no_HTTPS_e_continua_funcionando_no_localhost()
    {
        static async Task<string> CookieDoLogin(HttpClient cliente)
        {
            var r = await cliente.PostAsync("/api/login", new StringContent(
                JsonSerializer.Serialize(new { email = Api.AdminEmail, senha = Api.AdminSenha }), Encoding.UTF8, "application/json"));
            Assert.Equal(200, (int)r.StatusCode);
            return r.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("lanePetsAdmin="));
        }

        var https = await CookieDoLogin(Cliente("https://localhost"));
        Assert.Contains("secure", https, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", https, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", https, StringComparison.OrdinalIgnoreCase);

        var http = await CookieDoLogin(Cliente("http://localhost"));
        Assert.DoesNotContain("secure", http, StringComparison.OrdinalIgnoreCase);   // senao o login local pararia
    }

    [Fact]
    public async Task Avaliacao_publica_mostra_so_o_primeiro_nome_e_a_inicial()
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet(nome: "Fabrício Miguel da Silva", pet: "Thor");
        var comentario = "Nome curto " + Guid.NewGuid().ToString("N");
        var enviada = await cliente.Post("/api/cliente/avaliacoes", new { petId, avaliacao = 5, comentario });
        Assert.True(enviada.Codigo == 200, enviada.ToString());

        var painel = app.Api();
        var token = await painel.LoginAdmin();
        var id = await app.NoBanco(db => Task.FromResult(db.Depoimentos.Single(d => d.Comentario == comentario).Id));
        Assert.Equal(200, (await painel.Post($"/api/admin/depoimentos/{id}/status", new { token, status = "Aprovado" })).Codigo);

        var publico = await app.Api().Get("/api/public/depoimentos");
        var item = publico.Data.EnumerateArray().Single(d => d.GetProperty("comentario").GetString() == comentario);
        Assert.Equal("Fabrício S.", item.GetProperty("nomeCliente").GetString());

        // O painel (moderação) continua vendo o nome completo.
        var gravado = await app.NoBanco(db => Task.FromResult(db.Depoimentos.Single(d => d.Id == id).NomeCliente));
        Assert.Equal("Fabrício Miguel da Silva", gravado);
    }

    [Theory]
    [InlineData("Fabrício Miguel da Silva", "Fabrício S.")]
    [InlineData("ana souza", "ana S.")]
    [InlineData("Maria dos Santos", "Maria S.")]
    [InlineData("Joao", "Joao")]
    [InlineData("   ", "Cliente")]
    public void Nome_curto(string completo, string esperado) => Assert.Equal(esperado, NomePublico.Curto(completo));
}
