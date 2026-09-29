using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LanePets.Tests.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// Segurança — Etapa 4 da auditoria (29/09): o token do painel vai no header X-LanePets-Admin, nunca na URL
/// (histórico do navegador, logs de acesso, Referer). O jeito antigo (?token= / corpo) continua valendo para uma
/// aba aberta com JS antigo; o header tem prioridade. Cookie sozinho continua não valendo para a API (CSRF).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class TokenPorHeaderTests(LanePetsApp app)
{
    private HttpClient Http() => app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static HttpRequestMessage Pedido(HttpMethod metodo, string caminho, string? admin = null, object? corpo = null, string? financeiro = null)
    {
        var r = new HttpRequestMessage(metodo, caminho);
        if (admin is not null) r.Headers.Add("X-LanePets-Admin", admin);
        if (financeiro is not null) r.Headers.Add("X-LanePets-Financeiro", financeiro);
        if (corpo is not null) r.Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");
        return r;
    }

    private static async Task<(int Codigo, JsonElement Corpo)> Ler(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        return ((int)r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
    }

    [Fact]
    public async Task GET_do_painel_funciona_so_com_o_header_e_o_header_vence_um_token_errado_na_URL()
    {
        var token = await app.Api().LoginAdmin();
        var http = Http();

        var soHeader = await Ler(await http.SendAsync(Pedido(HttpMethod.Get, "/api/admin/me", token)));
        Assert.Equal(200, soHeader.Codigo);

        var headerVence = await Ler(await http.SendAsync(Pedido(HttpMethod.Get, "/api/admin/notificacoes?token=token-errado", token)));
        Assert.Equal(200, headerVence.Codigo);

        var semNada = await Ler(await http.SendAsync(Pedido(HttpMethod.Get, "/api/admin/me")));
        Assert.Equal(401, semNada.Codigo);

        // Compatibilidade: aba com JS antigo ainda funciona pela URL.
        Assert.Equal(200, (await app.Api().Get($"/api/admin/me?token={token}")).Codigo);
    }

    [Fact]
    public async Task POST_do_painel_funciona_com_o_token_so_no_header()
    {
        var token = await app.Api().LoginAdmin();
        var r = await Ler(await Http().SendAsync(Pedido(HttpMethod.Post, "/api/admin/sync/clientes", token,
            new { criados = Array.Empty<object>(), atualizados = Array.Empty<object>(), removidos = Array.Empty<string>() })));
        Assert.True(r.Codigo == 200, r.Corpo.ToString());

        var semToken = await Ler(await Http().SendAsync(Pedido(HttpMethod.Post, "/api/admin/sync/clientes", null,
            new { criados = Array.Empty<object>(), atualizados = Array.Empty<object>(), removidos = Array.Empty<string>() })));
        Assert.Equal(401, semToken.Codigo);
    }

    [Fact]
    public async Task Cookie_sozinho_nao_abre_a_API_do_painel()
    {
        // O login devolve o cookie lanePetsAdmin (usado só para abrir as páginas e o tempo real).
        var http = Http();
        var login = await http.PostAsync("/api/login", new StringContent(
            JsonSerializer.Serialize(new { email = Api.AdminEmail, senha = Api.AdminSenha }), Encoding.UTF8, "application/json"));
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("lanePetsAdmin=")).Split(';')[0];

        var pedido = Pedido(HttpMethod.Get, "/api/admin/me");
        pedido.Headers.Add("Cookie", cookie);
        Assert.Equal(401, (await Ler(await http.SendAsync(pedido))).Codigo);
    }

    [Fact]
    public async Task Senha_financeira_status_e_logout_pelo_header()
    {
        var token = await app.Api().LoginAdmin();
        var http = Http();

        var entrar = await Ler(await http.SendAsync(Pedido(HttpMethod.Post, "/api/financeiro_login", token, new { senha = "123456" })));
        Assert.True(entrar.Codigo == 200, entrar.Corpo.ToString());
        var fin = entrar.Corpo.GetProperty("data").GetProperty("token").GetString()!;

        Assert.Equal(200, (await Ler(await http.SendAsync(Pedido(HttpMethod.Get, "/api/financeiro_status", financeiro: fin)))).Codigo);
        Assert.Equal(200, (await Ler(await http.SendAsync(Pedido(HttpMethod.Post, "/api/financeiro_logout", corpo: new { }, financeiro: fin)))).Codigo);
        Assert.Equal(401, (await Ler(await http.SendAsync(Pedido(HttpMethod.Get, "/api/financeiro_status", financeiro: fin)))).Codigo);

        // Logout do painel pelo header derruba a sessão.
        Assert.Equal(200, (await Ler(await http.SendAsync(Pedido(HttpMethod.Post, "/api/logout", token, new { })))).Codigo);
        Assert.Equal(401, (await Ler(await http.SendAsync(Pedido(HttpMethod.Get, "/api/admin/me", token)))).Codigo);
    }

    [Fact]
    public void Nenhuma_tela_poe_o_token_do_painel_na_URL()
    {
        var raiz = app.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var proibido = new Regex(@"[?&]token=|searchParams\.set\(\s*['""]token['""]|URLSearchParams\(\{\s*token\b|financeiro_token['""]?\s*,",
            RegexOptions.IgnoreCase);
        var achados = Directory.EnumerateFiles(raiz, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".js") || f.EndsWith(".html")) && !f.Replace('\\', '/').Contains("/lib/"))
            .SelectMany(f => File.ReadAllLines(f).Select((linha, i) => (f, i, linha)))
            .Where(x => proibido.IsMatch(x.linha))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.linha.Trim()}")
            .ToList();
        Assert.True(achados.Count == 0, "Token na URL:\n" + string.Join("\n", achados));
    }
}
