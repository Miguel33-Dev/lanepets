using LanePets.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace LanePets.Services;

/// <summary>
/// Avisa o painel administrativo de que algo mudou no banco.
/// Chamado depois de cada SaveChangesAsync que altera dados vistos pelo admin.
/// Nunca lanca excecao para quem chamou: uma falha de notificacao nao pode
/// derrubar a operacao que ja foi gravada com sucesso.
/// </summary>
public class RealtimeNotifier(IHubContext<LanePetsHub> hub, ILogger<RealtimeNotifier> log)
{
    public async Task NotificarAsync(string entidade, string acao = "alterado", object? resumo = null)
    {
        try
        {
            await hub.Clients.All.SendAsync("dadosAlterados", new
            {
                entidade,
                acao,
                resumo,
                em = DateTime.UtcNow.ToString("O")
            });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "LanePets: falha ao notificar o painel sobre {Entidade}.", entidade);
        }
    }
}
