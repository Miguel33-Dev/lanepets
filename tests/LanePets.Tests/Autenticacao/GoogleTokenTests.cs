using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanePets.Services;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// Tarefa 1, etapa 2: conferencia do ID token do Google. Os tokens sao assinados aqui com uma chave RSA de teste
/// (o "Google" dos testes); nenhum teste fala com o Google de verdade.
/// </summary>
public class GoogleTokenTests
{
    private const string ClientId = "123456789-teste.apps.googleusercontent.com";
    private const string Kid = "kid-teste";
    private static readonly DateTimeOffset Agora = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private static readonly RSA ChaveGoogle = RSA.Create(2048);
    private static readonly RSA ChaveIntrusa = RSA.Create(2048);

    private sealed class ChavesFixas(Func<string, RSA?> achar) : IChavesGoogle
    {
        public Task<RSA?> ObterAsync(string kid, CancellationToken ct = default) => Task.FromResult(achar(kid));
    }

    private static GoogleTokenService Servico() => new(new ChavesFixas(k => k == Kid ? ChaveGoogle : null));

    private static Dictionary<string, object?> CorpoValido() => new()
    {
        ["iss"] = "https://accounts.google.com",
        ["aud"] = ClientId,
        ["azp"] = ClientId,
        ["sub"] = "109876543210987654321",
        ["email"] = "Tutor.Teste@Gmail.com",
        ["email_verified"] = true,
        ["name"] = "Tutor Teste",
        ["picture"] = "https://lh3.googleusercontent.com/a/foto",
        ["iat"] = Agora.AddMinutes(-1).ToUnixTimeSeconds(),
        ["exp"] = Agora.AddMinutes(59).ToUnixTimeSeconds()
    };

    private static string B64(byte[] b) => Base64Url.EncodeToString(b);
    private static string B64(object o) => B64(JsonSerializer.SerializeToUtf8Bytes(o));

    private static string Token(Dictionary<string, object?>? corpo = null, string alg = "RS256", string kid = Kid, RSA? chave = null)
    {
        var inicio = $"{B64(new Dictionary<string, object?> { ["alg"] = alg, ["kid"] = kid, ["typ"] = "JWT" })}.{B64(corpo ?? CorpoValido())}";
        var assinatura = (chave ?? ChaveGoogle).SignData(Encoding.ASCII.GetBytes(inicio), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{inicio}.{B64(assinatura)}";
    }

    private static Dictionary<string, object?> Com(string campo, object? valor)
    {
        var c = CorpoValido();
        if (valor is null) c.Remove(campo); else c[campo] = valor;
        return c;
    }

    [Fact]
    public async Task Token_valido_devolve_a_identidade_com_email_normalizado()
    {
        var r = await Servico().ValidarAsync(Token(), ClientId, Agora);
        Assert.True(r.Valido, r.Motivo);
        Assert.Equal("109876543210987654321", r.Identidade!.Sub);
        Assert.Equal("tutor.teste@gmail.com", r.Identidade.Email);
        Assert.Equal("Tutor Teste", r.Identidade.Nome);
        Assert.StartsWith("https://", r.Identidade.Foto);
    }

    [Fact]
    public async Task Emissor_sem_https_tambem_vale()
    {
        var r = await Servico().ValidarAsync(Token(Com("iss", "accounts.google.com")), ClientId, Agora);
        Assert.True(r.Valido, r.Motivo);
    }

    [Fact]
    public async Task Assinado_por_outra_chave_e_recusado()
    {
        var r = await Servico().ValidarAsync(Token(chave: ChaveIntrusa), ClientId, Agora);
        Assert.False(r.Valido);
        Assert.Contains("Assinatura", r.Motivo);
    }

    [Fact]
    public async Task Corpo_alterado_depois_de_assinado_e_recusado()
    {
        var partes = Token().Split('.');
        var trocado = $"{partes[0]}.{B64(Com("email", "outra.pessoa@gmail.com"))}.{partes[2]}";
        var r = await Servico().ValidarAsync(trocado, ClientId, Agora);
        Assert.False(r.Valido);
        Assert.Contains("Assinatura", r.Motivo);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    [InlineData("RS512")]
    public async Task Algoritmo_diferente_de_RS256_e_recusado(string alg)
    {
        var r = await Servico().ValidarAsync(Token(alg: alg), ClientId, Agora);
        Assert.False(r.Valido);
        Assert.Contains("Algoritmo", r.Motivo);
    }

    [Fact]
    public async Task Kid_desconhecido_e_recusado()
    {
        var r = await Servico().ValidarAsync(Token(kid: "outro-kid"), ClientId, Agora);
        Assert.False(r.Valido);
        Assert.Contains("kid", r.Motivo);
    }

    [Fact]
    public async Task Token_de_outro_aplicativo_e_recusado()
    {
        var r = await Servico().ValidarAsync(Token(Com("aud", "999-outro.apps.googleusercontent.com")), ClientId, Agora);
        Assert.False(r.Valido);
        Assert.Contains("aud", r.Motivo);
    }

    [Fact]
    public async Task Emissor_que_nao_e_o_google_e_recusado()
    {
        var r = await Servico().ValidarAsync(Token(Com("iss", "https://falso.exemplo.com")), ClientId, Agora);
        Assert.False(r.Valido);
        Assert.Contains("iss", r.Motivo);
    }

    [Fact]
    public async Task Token_expirado_e_recusado_mas_a_folga_de_5_minutos_vale()
    {
        var quaseVencido = Token(Com("exp", Agora.AddMinutes(-4).ToUnixTimeSeconds()));
        Assert.True((await Servico().ValidarAsync(quaseVencido, ClientId, Agora)).Valido);

        var r = await Servico().ValidarAsync(Token(Com("exp", Agora.AddMinutes(-6).ToUnixTimeSeconds())), ClientId, Agora);
        Assert.False(r.Valido);
        Assert.Contains("expirado", r.Motivo);
    }

    [Fact]
    public async Task Token_emitido_no_futuro_e_recusado()
    {
        var r = await Servico().ValidarAsync(Token(Com("iat", Agora.AddMinutes(10).ToUnixTimeSeconds())), ClientId, Agora);
        Assert.False(r.Valido);
        Assert.Contains("iat", r.Motivo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    [InlineData("false")]
    public async Task Email_nao_verificado_e_recusado(object? verificado)
    {
        var r = await Servico().ValidarAsync(Token(Com("email_verified", verificado)), ClientId, Agora);
        Assert.False(r.Valido);
        Assert.Contains("verificado", r.Motivo);
    }

    [Fact]
    public async Task Email_verificado_em_texto_tambem_vale()
    {
        Assert.True((await Servico().ValidarAsync(Token(Com("email_verified", "true")), ClientId, Agora)).Valido);
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("email")]
    [InlineData("exp")]
    public async Task Sem_campo_obrigatorio_e_recusado(string campo)
    {
        var r = await Servico().ValidarAsync(Token(Com(campo, null)), ClientId, Agora);
        Assert.False(r.Valido);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-um-jwt")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("!!!.@@@.###")]
    public async Task Lixo_no_lugar_do_token_e_recusado_sem_excecao(string? token)
    {
        var r = await Servico().ValidarAsync(token, ClientId, Agora);
        Assert.False(r.Valido);
        Assert.NotEmpty(r.Motivo);
    }

    [Fact]
    public async Task Sem_client_id_configurado_nada_passa()
    {
        var r = await Servico().ValidarAsync(Token(), "", Agora);
        Assert.False(r.Valido);
        Assert.Contains("desligado", r.Motivo);
    }

    [Fact]
    public async Task Google_fora_do_ar_vira_recusa_e_nao_excecao()
    {
        var servico = new GoogleTokenService(new ChavesFixas(_ => throw new HttpRequestException("sem rede")));
        var r = await servico.ValidarAsync(Token(), ClientId, Agora);
        Assert.False(r.Valido);
        Assert.Contains("indisponíveis", r.Motivo);
    }

    [Fact]
    public void Motivo_da_recusa_nunca_carrega_o_token()
    {
        var token = Token(Com("aud", "outro"));
        var r = Servico().ValidarAsync(token, ClientId, Agora).GetAwaiter().GetResult();
        Assert.DoesNotContain(token.Split('.')[2][..20], r.Motivo);
        Assert.DoesNotContain(token.Split('.')[1][..20], r.Motivo);
    }

    [Fact]
    public async Task Jwks_do_google_e_lido_e_a_chave_confere_a_assinatura()
    {
        var p = ChaveGoogle.ExportParameters(false);
        var jwks = JsonSerializer.Serialize(new
        {
            keys = new object[]
            {
                new { kty = "RSA", kid = Kid, alg = "RS256", use = "sig", n = B64(p.Modulus!), e = B64(p.Exponent!) },
                new { kty = "EC", kid = "ignorada", crv = "P-256", x = "a", y = "b" }
            }
        });
        var chaves = ChavesGoogleJwks.LerJwks(jwks);
        Assert.Single(chaves);

        var servico = new GoogleTokenService(new ChavesFixas(k => chaves.GetValueOrDefault(k)));
        Assert.True((await servico.ValidarAsync(Token(), ClientId, Agora)).Valido);
    }
}
