using LanePets.Services;
using Microsoft.AspNetCore.SignalR;

namespace LanePets.Hubs;

/// <summary>
/// Canal de tempo real do painel. O servidor nunca envia dados de negocio por
/// aqui: envia apenas um aviso curto ("a entidade X mudou"). Quem recebe volta
/// a consultar a API normal, com o token de administrador.
///
/// Item 1 do roadmap (24/09): o aviso as vezes carrega um resumo com nome de
/// cliente ou de pet, entao a conexao passou a exigir sessao administrativa.
/// O painel ja manda o cookie lanePetsAdmin (mesma origem, HttpOnly) ao abrir
/// a conexao; sem ele, ou com sessao expirada, a conexao e derrubada na hora.
/// </summary>
public class LanePetsHub(SessionService sessions) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var token = Context.GetHttpContext()?.Request.Cookies["lanePetsAdmin"] ?? "";
        try
        {
            sessions.RequireAdmin(token);
        }
        catch (UnauthorizedAccessException)
        {
            Context.Abort();
            return;
        }
        await base.OnConnectedAsync();
    }
}
