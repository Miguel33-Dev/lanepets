using LanePets.Data;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// 30/09: o acesso de VISITANTE ao painel (vitrine, criado em 29/09) foi REMOVIDO a pedido do Fabricio — cliente e
/// visitante nao veem nada do painel. Sobrou so a limpeza: se o banco ainda tem a conta ADM-VISITANTE ativa, ela e
/// desativada na subida (a senha era aleatoria, mas conta sem uso nao fica ligada). LanePets:Visitante nao faz mais nada.
/// </summary>
public static class Visitante
{
    public const string Id = "ADM-VISITANTE";

    public static async Task DesativarAsync(LanePetsDbContext db, ILogger log)
    {
        var conta = await db.UsuariosAdministradores.FirstOrDefaultAsync(u => u.Id == Id && u.Ativo);
        if (conta is null) return;
        conta.Ativo = false;
        await db.SaveChangesAsync();
        log.LogInformation("LanePets: conta de visitante antiga desativada (o acesso de visitante foi removido).");
    }
}
