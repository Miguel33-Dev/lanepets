using System.Text.Json;
using LanePets.Services;

namespace LanePets.Middleware;

/// <summary>
/// Rede de seguranca da API (item 14 do roadmap, 24/09).
///
/// A maioria dos endpoints ja tem try/catch e responde por ApiControllerBase.
/// Este middleware pega o que escapa (endpoint sem try/catch, erro em outro
/// middleware) e responde no MESMO envelope, com a MESMA traducao
/// (ErrosApi.Traduzir): nada de pagina HTML de erro nem stack trace para
/// quem chamou /api/*.
///
/// Fora de /api: em desenvolvimento a excecao segue para a pagina de erro do
/// ASP.NET (util para quem programa); em producao vira uma mensagem simples.
/// </summary>
public class ErroGlobalMiddleware(RequestDelegate next, IWebHostEnvironment ambiente)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await next(contexto);
        }
        catch (Exception ex) when (!contexto.Response.HasStarted)
        {
            var ehApi = contexto.Request.Path.StartsWithSegments("/api");
            if (!ehApi && ambiente.IsDevelopment()) throw;

            var (status, corpo) = ErrosApi.Traduzir(ex, contexto);
            contexto.Response.Clear();
            contexto.Response.StatusCode = status;

            if (ehApi)
            {
                contexto.Response.ContentType = "application/json; charset=utf-8";
                await contexto.Response.WriteAsync(JsonSerializer.Serialize(corpo, Json));
            }
            else
            {
                contexto.Response.ContentType = "text/plain; charset=utf-8";
                await contexto.Response.WriteAsync(status == 500
                    ? $"{ErrosApi.MensagemInterna} Código: {CodigoErro.Interno}"
                    : "Não foi possível abrir esta página.");
            }
        }
    }
}
