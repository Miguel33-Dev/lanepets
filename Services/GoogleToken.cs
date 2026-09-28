using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LanePets.Services;

/// <summary>Quem o Google diz que e a pessoa — so sai daqui depois de TODAS as conferencias.</summary>
public sealed record GoogleIdentidade(string Sub, string Email, string Nome, string Foto);

/// <summary>Resultado da conferencia: Identidade preenchida = token valido; senao Motivo diz por que (vai so para o log).</summary>
public sealed record GoogleValidacao(GoogleIdentidade? Identidade, string Motivo)
{
    public bool Valido => Identidade is not null;
    public static GoogleValidacao Recusa(string motivo) => new(null, motivo);
}

/// <summary>De onde vem a chave publica do Google para um "kid". Na aplicacao: JWKS do Google; nos testes: chave fixa.</summary>
public interface IChavesGoogle
{
    /// <summary>Chave RSA do kid, ou null se o Google nao publica esse kid. Pode lancar se o Google estiver fora do ar.</summary>
    Task<RSA?> ObterAsync(string kid, CancellationToken ct = default);
}

/// <summary>
/// Tarefa 1, etapa 2 (28/09): conferencia do ID token do Google SEM pacote novo.
/// Confere, nesta ordem: formato JWT · alg = RS256 (recusa "none"/HS256) · assinatura com a chave publica do kid ·
/// iss do Google · aud = o nosso Client ID · exp (com 5 min de folga) · iat nao no futuro · sub · e-mail verificado.
/// Nunca lanca: qualquer problema vira Recusa com o motivo. O token em si nunca vai para log nem para o Motivo.
/// </summary>
public sealed class GoogleTokenService(IChavesGoogle chaves)
{
    public static readonly TimeSpan Folga = TimeSpan.FromMinutes(5);
    private static readonly string[] Emissores = ["accounts.google.com", "https://accounts.google.com"];
    private const int TamanhoMaximo = 8 * 1024;   // ID token do Google tem ~1,2 KB

    public async Task<GoogleValidacao> ValidarAsync(string? idToken, string? clientId, DateTimeOffset agora, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return GoogleValidacao.Recusa("Login com Google desligado (sem Client ID).");
        if (string.IsNullOrWhiteSpace(idToken)) return GoogleValidacao.Recusa("Token vazio.");
        if (idToken.Length > TamanhoMaximo) return GoogleValidacao.Recusa("Token grande demais.");

        var partes = idToken.Trim().Split('.');
        if (partes.Length != 3 || partes.Any(p => p.Length == 0)) return GoogleValidacao.Recusa("Token fora do formato JWT.");

        JsonElement cabecalho, corpo;
        byte[] assinatura;
        try
        {
            cabecalho = JsonDocument.Parse(Base64Url.DecodeFromChars(partes[0])).RootElement.Clone();
            corpo = JsonDocument.Parse(Base64Url.DecodeFromChars(partes[1])).RootElement.Clone();
            assinatura = Base64Url.DecodeFromChars(partes[2]);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return GoogleValidacao.Recusa("Token ilegível (base64/JSON inválido).");
        }
        if (cabecalho.ValueKind != JsonValueKind.Object || corpo.ValueKind != JsonValueKind.Object)
            return GoogleValidacao.Recusa("Token ilegível (cabeçalho ou corpo não é objeto).");

        if (Texto(cabecalho, "alg") != "RS256") return GoogleValidacao.Recusa($"Algoritmo recusado ({Curto(Texto(cabecalho, "alg"))}).");
        var kid = Texto(cabecalho, "kid");
        if (kid.Length == 0) return GoogleValidacao.Recusa("Token sem kid.");

        RSA? rsa;
        try { rsa = await chaves.ObterAsync(kid, ct); }
        catch (Exception ex) { return GoogleValidacao.Recusa($"Chaves do Google indisponíveis ({ex.GetType().Name})."); }
        if (rsa is null) return GoogleValidacao.Recusa("Chave (kid) desconhecida pelo Google.");

        var assinado = Encoding.ASCII.GetBytes($"{partes[0]}.{partes[1]}");
        bool assinaturaOk;
        try { assinaturaOk = rsa.VerifyData(assinado, assinatura, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); }
        catch (CryptographicException) { assinaturaOk = false; }
        if (!assinaturaOk) return GoogleValidacao.Recusa("Assinatura inválida.");

        // Daqui para baixo o conteudo e do Google de verdade; falta ver se e para NOS e se ainda vale.
        if (!Emissores.Contains(Texto(corpo, "iss"))) return GoogleValidacao.Recusa("Emissor (iss) não é o Google.");
        if (!AudienciaConfere(corpo, clientId)) return GoogleValidacao.Recusa("Token emitido para outro aplicativo (aud).");

        if (Numero(corpo, "exp") is not { } exp) return GoogleValidacao.Recusa("Token sem validade (exp).");
        if (DateTimeOffset.FromUnixTimeSeconds(exp) + Folga < agora) return GoogleValidacao.Recusa("Token expirado.");
        if (Numero(corpo, "iat") is { } iat && DateTimeOffset.FromUnixTimeSeconds(iat) - Folga > agora)
            return GoogleValidacao.Recusa("Token emitido no futuro (iat).");

        var sub = Texto(corpo, "sub");
        if (sub.Length == 0) return GoogleValidacao.Recusa("Token sem identificador da conta (sub).");
        var email = Texto(corpo, "email").Trim().ToLowerInvariant();
        if (email.Length == 0) return GoogleValidacao.Recusa("Conta Google sem e-mail.");
        if (!EmailVerificado(corpo)) return GoogleValidacao.Recusa("E-mail da conta Google não verificado.");

        return new(new GoogleIdentidade(sub, email, Texto(corpo, "name").Trim(), Texto(corpo, "picture").Trim()), "");
    }

    private static bool AudienciaConfere(JsonElement corpo, string clientId)
    {
        if (!corpo.TryGetProperty("aud", out var aud)) return false;
        return aud.ValueKind switch
        {
            JsonValueKind.String => aud.GetString() == clientId,
            JsonValueKind.Array => aud.EnumerateArray().Any(a => a.ValueKind == JsonValueKind.String && a.GetString() == clientId),
            _ => false
        };
    }

    /// <summary>O Google manda email_verified como booleano; ja houve versoes com "true" em texto.</summary>
    private static bool EmailVerificado(JsonElement corpo)
        => corpo.TryGetProperty("email_verified", out var v)
           && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), "true", StringComparison.OrdinalIgnoreCase)));

    private static string Texto(JsonElement o, string nome)
        => o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long? Numero(JsonElement o, string nome)
        => o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    private static string Curto(string s) => s.Length == 0 ? "vazio" : s.Length > 12 ? s[..12] : s;
}

/// <summary>
/// Chaves publicas do Google (JWKS em https://www.googleapis.com/oauth2/v3/certs), em memoria pelo tempo do
/// Cache-Control (padrao 1 h). Kid desconhecido busca de novo — no maximo uma vez por minuto, para ninguem
/// usar tokens inventados para martelar o Google a partir do nosso servidor.
/// </summary>
public sealed class ChavesGoogleJwks(ILogger<ChavesGoogleJwks> logger) : IChavesGoogle
{
    public const string Endereco = "https://www.googleapis.com/oauth2/v3/certs";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim trava = new(1, 1);
    private Dictionary<string, RSA> cache = new();
    private DateTimeOffset validoAte = DateTimeOffset.MinValue;
    private DateTimeOffset ultimaBusca = DateTimeOffset.MinValue;

    public async Task<RSA?> ObterAsync(string kid, CancellationToken ct = default)
    {
        var agora = DateTimeOffset.UtcNow;
        if (agora < validoAte && cache.TryGetValue(kid, out var rsa)) return rsa;

        await trava.WaitAsync(ct);
        try
        {
            agora = DateTimeOffset.UtcNow;
            var vencido = agora >= validoAte;
            if (!vencido && cache.TryGetValue(kid, out rsa)) return rsa;
            if (!vencido && agora - ultimaBusca < TimeSpan.FromMinutes(1)) return null;   // kid estranho, cache ainda valido

            ultimaBusca = agora;
            using var resposta = await Http.GetAsync(Endereco, ct);
            resposta.EnsureSuccessStatusCode();
            var json = await resposta.Content.ReadAsStringAsync(ct);
            cache = LerJwks(json);
            validoAte = agora + (resposta.Headers.CacheControl?.MaxAge ?? TimeSpan.FromHours(1));
            logger.LogInformation("LanePets: chaves do Google atualizadas ({Quantas} chaves, válidas até {Ate:HH:mm} UTC).", cache.Count, validoAte);
            return cache.GetValueOrDefault(kid);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "LanePets: não foi possível buscar as chaves do Google em {Endereco}.", Endereco);
            throw;
        }
        finally { trava.Release(); }
    }

    /// <summary>JWKS -> kid => RSA. So chaves RSA; o resto e ignorado.</summary>
    public static Dictionary<string, RSA> LerJwks(string json)
    {
        var chaves = new Dictionary<string, RSA>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array) return chaves;
        foreach (var k in keys.EnumerateArray())
        {
            if (k.ValueKind != JsonValueKind.Object) continue;
            string P(string n) => k.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            if (P("kty") != "RSA" || P("kid").Length == 0 || P("n").Length == 0 || P("e").Length == 0) continue;
            var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = Base64Url.DecodeFromChars(P("n")), Exponent = Base64Url.DecodeFromChars(P("e")) });
            chaves[P("kid")] = rsa;
        }
        return chaves;
    }
}
