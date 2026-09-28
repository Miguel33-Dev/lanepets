using LanePets.Data;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;

namespace LanePets.Controllers;

/// <summary>
/// 29/09: GET /api/admin/notificacoes?token= — o que esta esperando a equipe agir (sino do painel).
/// Basta estar logado; cada item respeita a permissao do modulo e a unidade do funcionario.
/// </summary>
[Route("api/admin/notificacoes")]
public class NotificacoesController(LanePetsDbContext db, PermissaoService permissoes) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string token = "")
    {
        try
        {
            var ctx = await permissoes.ResolverAsync(token);
            var itens = await NotificacoesPainelService.ListarAsync(db, ctx, DateTime.Now);
            return OkApi(new { total = itens.Sum(i => i.Quantidade), itens });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
}
