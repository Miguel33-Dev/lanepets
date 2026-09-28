using LanePets.Data;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;

/// <summary>
/// 27/09: leitura da tela Agendamentos do painel sem o estado inteiro (AgendaPainelService).
/// Gravacao continua em POST /api/admin/sync/agendamentos.
/// </summary>
[Route("api/admin/agenda")]
public class AgendaPainelController(LanePetsDbContext db, PermissaoService permissoes, AgendaPainelService agenda) : ApiControllerBase
{
    /// <summary>
    /// Agendamentos da aba (unidade) com filtros, ordem e pagina. Lista: limite 12 (padrao).
    /// Calendario: janelaDe/janelaAte = dias visiveis e limite ate 500; fotos=false no mes/semana.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string token = "", [FromQuery] string? unidade = null,
        [FromQuery] string? busca = null, [FromQuery] string? servico = null, [FromQuery] string? status = null,
        [FromQuery] string? pagamento = null, [FromQuery] string? responsavel = null, [FromQuery] string? de = null,
        [FromQuery] string? ate = null, [FromQuery] string? janelaDe = null, [FromQuery] string? janelaAte = null,
        [FromQuery] string? ordem = null, [FromQuery] string? dir = null, [FromQuery] bool fotos = true,
        [FromQuery] int limite = AgendaPainelService.PorPagina, [FromQuery] int offset = 0)
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(token, ModulosAdmin.Agendamentos, AcaoPermissao.Visualizar);
            return OkApi(await agenda.ListarAsync(contexto,
                new(unidade, busca, servico, status, pagamento, responsavel, de, ate, janelaDe, janelaAte, ordem, dir, fotos), limite, offset));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>Horarios lotados da unidade no dia (etapa "Data e hora" do novo agendamento).</summary>
    [HttpGet("lotados")]
    public async Task<IActionResult> Lotados([FromQuery] string token = "", [FromQuery] string? unidade = null, [FromQuery] string? data = null)
    {
        try
        {
            await permissoes.ExigirAsync(token, ModulosAdmin.Agendamentos, AcaoPermissao.Visualizar);
            return OkApi(new { unidade = Normalizador.IdUnidade(unidade), data = Normalizador.Data(data), lotados = await agenda.LotadosAsync(unidade, data) });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>
    /// Catalogo de servicos para o novo agendamento e a edicao. Antes vinha no estado so para quem
    /// tinha o modulo Servicos; agora quem agenda tambem recebe (o catalogo ja e publico no site).
    /// </summary>
    [HttpGet("servicos")]
    public async Task<IActionResult> Servicos([FromQuery] string token = "")
    {
        try
        {
            await permissoes.ExigirAsync(token, ModulosAdmin.Agendamentos, AcaoPermissao.Visualizar);
            var servicos = await db.Servicos.AsNoTracking().OrderBy(s => s.Nome).ToListAsync();
            return OkApi(servicos.Select(FormatoPainel.Servico));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>Um agendamento com pet (so com o modulo Pets) e cliente (so com o modulo Clientes).</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Detalhe(string id, [FromQuery] string token = "")
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(token, ModulosAdmin.Agendamentos, AcaoPermissao.Visualizar);
            return OkApi(await agenda.DetalheAsync(contexto, id));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
}
