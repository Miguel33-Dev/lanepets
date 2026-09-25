using LanePets.Data;
using LanePets.Models;

namespace LanePets.Services;

/// <summary>
/// LOG DE EVENTOS (item 15 do roadmap, 24/09).
///
/// Grava um EventoLog por acontecimento relevante. Tres regras:
///
///   1. NUNCA derruba a operacao principal. Se gravar o evento falhar, o
///      problema vai para o console e a requisicao segue.
///   2. Usa um DbContext PROPRIO (escopo novo) a cada gravacao. Assim um erro
///      de banco na requisicao nao impede de registrar o proprio erro, e o
///      evento nao e salvo "de carona" com alteracoes pendentes de outra coisa.
///   3. Nunca recebe senha, token ou cartao. Quem chama monta so o texto de
///      "Detalhes" que pode ser lido por um administrador.
/// </summary>
public class EventosService(IServiceScopeFactory escopos, ILogger<EventosService> log)
{
    public const int LimiteDetalhes = 1000;

    public record Evento(
        string Categoria,
        string Acao,
        string Nivel = "info",
        string Origem = "sistema",
        string AutorId = "",
        string Autor = "",
        string AlvoId = "",
        string Detalhes = "",
        string Referencia = "");

    public Task RegistrarAsync(Evento e, HttpContext? contexto = null)
        => RegistrarComIpAsync(e, contexto?.Connection.RemoteIpAddress?.ToString() ?? "");

    /// <summary>Versao "dispara e esquece" para quem nao pode aguardar (ex.: tradutor de erros).</summary>
    public void Registrar(Evento e, HttpContext? contexto = null)
    {
        var ip = contexto?.Connection.RemoteIpAddress?.ToString();
        _ = Task.Run(async () =>
        {
            // O HttpContext nao pode ser usado depois que a requisicao termina:
            // leva so o IP, capturado agora.
            await RegistrarComIpAsync(e, ip ?? "");
        });
    }

    private async Task RegistrarComIpAsync(Evento e, string ip)
    {
        try
        {
            using var escopo = escopos.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<LanePetsDbContext>();
            db.EventosLog.Add(new EventoLog
            {
                Id = "EVT-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),
                DataHora = DateTime.UtcNow,
                Nivel = e.Nivel is "aviso" or "erro" ? e.Nivel : "info",
                Categoria = Cortar(e.Categoria, 40),
                Acao = Cortar(e.Acao, 120),
                Origem = Cortar(e.Origem, 20),
                AutorId = Cortar(e.AutorId, 80),
                Autor = Cortar(e.Autor, 160),
                AlvoId = Cortar(e.AlvoId, 80),
                Detalhes = Cortar(e.Detalhes, LimiteDetalhes),
                Ip = Cortar(ip, 64),
                Referencia = Cortar(e.Referencia, 20)
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "LanePets: não foi possível gravar o evento {Categoria}/{Acao}.", e.Categoria, e.Acao);
        }
    }

    private static string Cortar(string? valor, int max)
    {
        var texto = (valor ?? "").Trim();
        return texto.Length <= max ? texto : texto[..max];
    }
}
