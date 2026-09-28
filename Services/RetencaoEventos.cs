using System.Globalization;
using LanePets.Data;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Retencao do Log de eventos (26/09, decisao do Fabricio: 12 meses). Evento com mais de
/// <see cref="Meses"/> meses e apagado — na subida (depois do backup automatico, que guarda o
/// banco inteiro) e uma vez por dia (<see cref="RetencaoLogWorker"/>). A limpeza vira um evento
/// proprio, gravado DEPOIS de apagar, entao ele mesmo nunca e removido na hora.
/// Nenhuma outra tabela e tocada: a retencao e so do Log.
/// </summary>
public static class RetencaoEventos
{
    public const int Meses = 12;

    public static DateTime Corte(DateTime agoraUtc) => agoraUtc.AddMonths(-Meses);

    /// <summary>Apaga os eventos anteriores ao corte. Devolve quantos saíram.</summary>
    public static async Task<int> ApagarAntigosAsync(LanePetsDbContext db, DateTime agoraUtc)
    {
        var corte = Corte(agoraUtc);
        return await db.EventosLog.Where(e => e.DataHora < corte).ExecuteDeleteAsync();
    }

    /// <summary>Apaga e, se saiu algo, registra no proprio Log quantos e de antes de quando.</summary>
    public static async Task<int> ExecutarAsync(LanePetsDbContext db, EventosService eventos, DateTime agoraUtc)
    {
        var apagados = await ApagarAntigosAsync(db, agoraUtc);
        if (apagados > 0)
            await eventos.RegistrarAsync(new("sistema", "Eventos antigos removidos", "info", "sistema", "", "sistema", "",
                $"{apagados} evento(s) com mais de {Meses} meses (anteriores a {Corte(agoraUtc).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}) removido(s) pela retenção do Log."));
        return apagados;
    }
}

/// <summary>Roda a retencao do Log uma vez por dia. Falha vira aviso no log e nunca derruba a aplicacao.</summary>
public sealed class RetencaoLogWorker(IServiceScopeFactory escopos, EventosService eventos, ILogger<RetencaoLogWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        using var relogio = new PeriodicTimer(TimeSpan.FromHours(24));
        try
        {
            while (await relogio.WaitForNextTickAsync(parar))
            {
                try
                {
                    using var escopo = escopos.CreateScope();
                    var apagados = await RetencaoEventos.ExecutarAsync(escopo.ServiceProvider.GetRequiredService<LanePetsDbContext>(), eventos, DateTime.UtcNow);
                    if (apagados > 0) log.LogInformation("LanePets: retenção do Log removeu {Total} evento(s) antigo(s).", apagados);
                }
                catch (Exception ex) when (!parar.IsCancellationRequested)
                {
                    log.LogWarning(ex, "LanePets: a retenção do Log de eventos falhou; tenta de novo amanhã.");
                }
            }
        }
        catch (OperationCanceledException) { /* aplicacao encerrando */ }
    }
}
