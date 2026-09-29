using System.Text.Json;
using LanePets.Services;
using Microsoft.AspNetCore.Http.Features;

namespace LanePets.Middleware;

/// <summary>
/// Seguranca — Etapa 3 da auditoria (29/09): cabecalhos de seguranca + limite de tamanho da requisicao.
///
/// CABECALHOS (itens C da auditoria), em TODA resposta (paginas, arquivos, API e erros):
///   Content-Security-Policy  so scripts/estilos/fontes do proprio LanePets, do Google Fonts e do login com Google;
///                            nenhuma pagina pode ser aberta dentro de iframe de outro site; sem plugins.
///   X-Content-Type-Options   nosniff (o navegador nao "adivinha" o tipo do arquivo)
///   X-Frame-Options          DENY (o mesmo que frame-ancestors, para navegador antigo)
///   Referrer-Policy          strict-origin-when-cross-origin (o login do Google precisa da origem)
///   Permissions-Policy       camera, microfone, localizacao e pagamento desligados
///
/// 'unsafe-inline' em script-src: varias telas do painel ainda tem &lt;script&gt; e onclick no proprio HTML.
/// A CSP continua barrando script de OUTRO site, envio de dados para fora (connect-src), &lt;base&gt;, &lt;object&gt;
/// e formulario apontando para fora. Tirar os scripts inline das telas e melhoria futura (Tarefa 7).
///
/// Escritos no OnStarting porque o ErroGlobalMiddleware da Response.Clear() (que apagaria os cabecalhos).
///
/// TAMANHO (item E): corpo acima de LanePets:Seguranca:TamanhoMaximoKB (padrao 2048 = 2 MB; a maior foto
/// aceita tem 200 KB) e recusado com 413 ERR-4130 antes de chegar no controller. A importacao do navegador
/// (/api/admin/importar, so Administrador com Configuracoes) leva o banco inteiro do localStorage e tem o
/// proprio teto: LanePets:Seguranca:TamanhoMaximoImportacaoKB (padrao 20480 = 20 MB).
/// Sem Content-Length (envio em partes) o Kestrel corta no mesmo limite (IHttpMaxRequestBodySizeFeature).
/// </summary>
public class SegurancaHttpMiddleware(RequestDelegate next, IConfiguration config)
{
    public const string Csp =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://accounts.google.com/gsi/client; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://accounts.google.com/gsi/style; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: blob: https://*.googleusercontent.com; " +
        "connect-src 'self' https://accounts.google.com/gsi/; " +
        "frame-src https://accounts.google.com/gsi/; " +
        "frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext contexto)
    {
        contexto.Response.OnStarting(() =>
        {
            var h = contexto.Response.Headers;
            h["Content-Security-Policy"] = Csp;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            h.Remove("Server");
            h.Remove("X-Powered-By");
            return Task.CompletedTask;
        });

        var importacao = contexto.Request.Path.StartsWithSegments("/api/admin/importar");
        var limiteKb = importacao
            ? config.GetValue("LanePets:Seguranca:TamanhoMaximoImportacaoKB", 20480)
            : config.GetValue("LanePets:Seguranca:TamanhoMaximoKB", 2048);
        long limite = Math.Max(1, limiteKb) * 1024L;

        var recurso = contexto.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (recurso is { IsReadOnly: false }) recurso.MaxRequestBodySize = limite;

        if (contexto.Request.ContentLength > limite)
        {
            contexto.Response.StatusCode = 413;
            contexto.Response.ContentType = "application/json; charset=utf-8";
            await contexto.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                ok = false,
                error = MensagemGrande(limite),
                codigo = CodigoErro.GrandeDemais,
                proibido = false,
                timestamp = DateTime.UtcNow.ToString("O")
            }, Json));
            return;
        }

        await next(contexto);
    }

    public static string MensagemGrande(long limiteBytes) =>
        $"O envio é grande demais (máximo de {Math.Max(1, limiteBytes / (1024 * 1024))} MB). Diminua as fotos ou envie em partes.";
}
