using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LanePets.Tests.Infra;

/// <summary>
/// Resposta da API ja lida: status HTTP + corpo JSON no envelope { ok, data } ou
/// { ok:false, error, codigo, ... } (CONTEXTO §6.3).
/// </summary>
public sealed record Resposta(HttpStatusCode Status, JsonElement Corpo, HttpResponseMessage Http)
{
    public int Codigo => (int)Status;
    public bool Ok => Corpo.ValueKind == JsonValueKind.Object && Corpo.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
    public JsonElement Data => Corpo.GetProperty("data");
    public string Erro => Corpo.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
    public string CodigoErro => Corpo.TryGetProperty("codigo", out var c) ? c.GetString() ?? "" : "";

    /// <summary>Texto de data.&lt;campo&gt;[.&lt;campo&gt;...] (ex.: Texto("cliente", "email")).</summary>
    public string Texto(params string[] caminho)
    {
        var atual = Data;
        foreach (var parte in caminho) atual = atual.GetProperty(parte);
        return atual.GetString() ?? "";
    }

    public override string ToString() => $"HTTP {Codigo} {Corpo}";
}

/// <summary>
/// Chamadas a API como uma tela faria. Nunca lanca em 4xx/5xx: o status E o que o
/// teste confere. O token do cliente vai no header X-LanePets-Client; o do painel,
/// em ?token= (como as telas e os scripts testar-*.ps1 fazem).
/// </summary>
public sealed class Api(HttpClient http)
{
    public const string AdminEmail = "admin@gmail.com";
    public const string AdminSenha = "123456";

    /// <summary>Token de cliente mandado em toda chamada (null = sem sessao).</summary>
    public string? TokenCliente { get; set; }

    public Task<Resposta> Get(string caminho) => Enviar(HttpMethod.Get, caminho, null);
    public Task<Resposta> Post(string caminho, object? corpo = null) => Enviar(HttpMethod.Post, caminho, corpo ?? new { });
    public Task<Resposta> Put(string caminho, object? corpo = null) => Enviar(HttpMethod.Put, caminho, corpo ?? new { });

    public async Task<Resposta> Enviar(HttpMethod metodo, string caminho, object? corpo)
    {
        using var requisicao = new HttpRequestMessage(metodo, caminho);
        if (corpo is not null) requisicao.Content = JsonContent.Create(corpo);
        if (!string.IsNullOrEmpty(TokenCliente)) requisicao.Headers.Add("X-LanePets-Client", TokenCliente);

        var resposta = await http.SendAsync(requisicao);
        var texto = await resposta.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(texto)
            ? JsonDocument.Parse("{}").RootElement
            : JsonDocument.Parse(texto).RootElement.Clone();
        return new Resposta(resposta.StatusCode, json, resposta);
    }

    /// <summary>Login no painel com o administrador inicial; devolve o token.</summary>
    public async Task<string> LoginAdmin(string email = AdminEmail, string senha = AdminSenha)
    {
        var r = await Post("/api/login", new { email, senha });
        Assert.True(r.Codigo == 200, $"Login administrativo falhou: {r}");
        return r.Texto("token");
    }

    /// <summary>Cria uma conta de cliente valida pela area do cliente e guarda o token.</summary>
    public async Task<ClienteCriado> CadastrarCliente(string? email = null, string senha = "Senha123", string nome = "Cliente Teste", string pet = "Rex")
    {
        email ??= NovoEmail();
        var r = await Post("/api/cliente/cadastro", new
        {
            nome, email, senha, telefone = "(11) 98765-4321", endereco = "Rua dos Testes, 12", pet, tipo = "Cachorro", raca = "SRD"
        });
        Assert.True(r.Codigo == 200, $"Cadastro de cliente falhou: {r}");
        TokenCliente = r.Texto("token");
        return new ClienteCriado(email.Trim().ToLowerInvariant(), senha, TokenCliente);
    }

    public static string NovoEmail(string prefixo = "teste") => $"{prefixo}.{Guid.NewGuid():N}@exemplo.com";

    /// <summary>Id unico para registros criados pelo teste (ex.: "PRD-T-1A2B3C4D").</summary>
    public static string NovoId(string prefixo) => $"{prefixo}-T-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    // ------------------------------------------------------------------ painel

    /// <summary>
    /// POST /api/admin/sync/{colecao}: a mesma gravacao que as telas do painel fazem
    /// (criados / atualizados / removidos num lote so).
    /// </summary>
    public Task<Resposta> Sync(string token, string colecao, object[]? criados = null, object[]? atualizados = null, string[]? removidos = null)
        => Post($"/api/admin/sync/{colecao}", new
        {
            token,
            criados = criados ?? Array.Empty<object>(),
            atualizados = atualizados ?? Array.Empty<object>(),
            removidos = removidos ?? Array.Empty<string>()
        });

    /// <summary>
    /// Cria (com o token do Administrador Geral) um administrador COMUM com exatamente as
    /// permissoes pedidas, entra com ele e devolve o token. Ex.:
    ///     await api.AdminCom(tokenGeral, Permissao.So("produtos", visualizar: true))
    /// </summary>
    public async Task<string> AdminCom(string tokenGeral, params Permissao[] permissoes)
    {
        var email = NovoEmail("admin");
        const string senha = "Senha123";
        var criado = await Post("/api/admin/usuarios", new { token = tokenGeral, nome = "Admin de Teste", email, senha, confirmarSenha = senha, perfil = "Admin" });
        Assert.True(criado.Codigo == 200, $"Criar administrador falhou: {criado}");
        var id = criado.Texto("id");

        if (permissoes.Length > 0)
        {
            var salvo = await Put($"/api/admin/usuarios/{id}/permissoes", new
            {
                token = tokenGeral,
                acessoTotal = false,
                modulos = permissoes.Select(p => new { chave = p.Modulo, visualizar = p.Visualizar, criar = p.Criar, editar = p.Editar, excluir = p.Excluir }).ToArray()
            });
            Assert.True(salvo.Codigo == 200, $"Salvar permissões falhou: {salvo}");
        }

        return await LoginAdmin(email, senha);
    }
}

/// <summary>Permissao de um modulo para <see cref="Api.AdminCom"/>.</summary>
public sealed record Permissao(string Modulo, bool Visualizar, bool Criar = false, bool Editar = false, bool Excluir = false)
{
    public static Permissao So(string modulo, bool visualizar = true, bool criar = false, bool editar = false, bool excluir = false)
        => new(modulo, visualizar, criar, editar, excluir);

    public static Permissao Tudo(string modulo) => new(modulo, true, true, true, true);
}

public sealed record ClienteCriado(string Email, string Senha, string Token);
