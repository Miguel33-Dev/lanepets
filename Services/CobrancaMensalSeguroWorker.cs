using LanePets.Data;

namespace LanePets.Services;

/// <summary>
/// Cobranca mensal do Seguro Pet (26/09): gera sozinho a mensalidade que venceu.
///
/// A geracao em si e o PagamentosService.ReconciliarAsync (idempotente), que ja roda na
/// subida, depois de toda gravacao e sempre que alguem abre Pagamentos, Seguros ou a
/// conta do cliente. Este servico so garante que ela tambem rode com o sistema parado
/// de uso: uma vez por hora. Falha aqui vira aviso no log e nunca derruba a aplicacao.
/// </summary>
public sealed class CobrancaMensalSeguroWorker(IServiceScopeFactory escopos, ILogger<CobrancaMensalSeguroWorker> log) : BackgroundService
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        using var relogio = new PeriodicTimer(Intervalo);
        try
        {
            while (await relogio.WaitForNextTickAsync(parar))
            {
                try
                {
                    using var escopo = escopos.CreateScope();
                    var db = escopo.ServiceProvider.GetRequiredService<LanePetsDbContext>();
                    var alterados = await PagamentosService.ReconciliarAsync(db);
                    if (alterados > 0) log.LogInformation("LanePets: {Total} pagamento(s) criados/atualizados pela rotina de cobranca mensal.", alterados);
                }
                catch (Exception ex) when (!parar.IsCancellationRequested)
                {
                    log.LogWarning(ex, "LanePets: a rotina de cobranca mensal do Seguro Pet falhou; tenta de novo na proxima hora.");
                }
            }
        }
        catch (OperationCanceledException) { /* aplicacao encerrando */ }
    }
}
