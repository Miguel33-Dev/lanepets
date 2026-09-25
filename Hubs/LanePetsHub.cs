using Microsoft.AspNetCore.SignalR;

namespace LanePets.Hubs;

/// <summary>
/// Canal de tempo real do painel. O servidor nunca envia dados de negocio por
/// aqui: envia apenas um aviso curto ("a entidade X mudou"). Quem recebe volta
/// a consultar a API normal, com o token de administrador. Assim o hub nao
/// vira uma porta de saida de dados sem autenticacao.
/// </summary>
public class LanePetsHub : Hub
{
}
