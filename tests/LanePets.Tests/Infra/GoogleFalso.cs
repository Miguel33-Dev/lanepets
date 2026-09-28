using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LanePets.Tests.Infra;

/// <summary>
/// Tarefa 1, etapa 4: um "Google" de mentira para os testes do endpoint. Assina ID tokens com uma chave RSA de teste
/// e entrega a mesma aplicacao (derivada da <see cref="LanePetsApp"/>, mesmo banco) com o login com Google ligado e as
/// chaves do Google trocadas por essa chave. Nenhum teste fala com o Google de verdade.
/// </summary>
public static class GoogleFalso
{
    public const string ClientId = "123456789-testes.apps.googleusercontent.com";
    public const string Kid = "kid-testes";
    private static readonly RSA Chave = RSA.Create(2048);

    private sealed class Chaves : IChavesGoogle
    {
        public Task<RSA?> ObterAsync(string kid, CancellationToken ct = default) => Task.FromResult(kid == Kid ? Chave : null);
    }

    private static readonly object Trava = new();
    private static WebApplicationFactory<Program>? fabrica;

    /// <summary>Aplicacao com Google ligado; criada uma vez (a LanePetsApp descarta junto no fim da rodada).</summary>
    public static Api Api(LanePetsApp app)
    {
        lock (Trava)
        {
            fabrica ??= app.WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { [GoogleLogin.Chave] = ClientId }));
                b.ConfigureTestServices(s => s.AddSingleton<IChavesGoogle, Chaves>());
            });
            return new Api(fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        }
    }

    /// <summary>ID token assinado como o Google assinaria. `ajuste` mexe no corpo (ex.: trocar aud).</summary>
    public static string Token(string email, string sub, string nome = "Tutor Google", Action<Dictionary<string, object?>>? ajuste = null)
    {
        var agora = DateTimeOffset.UtcNow;
        var corpo = new Dictionary<string, object?>
        {
            ["iss"] = "https://accounts.google.com", ["aud"] = ClientId, ["azp"] = ClientId, ["sub"] = sub,
            ["email"] = email, ["email_verified"] = true, ["name"] = nome, ["picture"] = "",
            ["iat"] = agora.AddMinutes(-1).ToUnixTimeSeconds(), ["exp"] = agora.AddMinutes(59).ToUnixTimeSeconds()
        };
        ajuste?.Invoke(corpo);
        var inicio = $"{B64(new { alg = "RS256", kid = Kid, typ = "JWT" })}.{B64(corpo)}";
        var assinatura = Chave.SignData(Encoding.ASCII.GetBytes(inicio), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{inicio}.{Base64Url.EncodeToString(assinatura)}";
    }

    public static string NovoSub() => Guid.NewGuid().ToString("N");

    private static string B64(object o) => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(o));
}
