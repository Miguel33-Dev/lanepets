using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;

/// <summary>
/// Item 8 do roadmap (25/09): tela "Pagamentos" do painel.
/// Modulo "pagamentos" (o mesmo de Entradas e Saidas). Funcionario so ve e
/// mexe nos pagamentos da propria unidade (seguro nao tem unidade: nao aparece).
/// A regra de status mora em PagamentosService.
/// </summary>
[Route("api/admin/pagamentos")]
public class PagamentosController(LanePetsDbContext db, PermissaoService permissoes, EventosService eventos, RealtimeNotifier realtime) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string token = "", [FromQuery] string? status = null, [FromQuery] string? origem = null,
        [FromQuery] string? unidade = null, [FromQuery] string? de = null, [FromQuery] string? ate = null, [FromQuery] string? busca = null,
        [FromQuery] bool reembolso = false)
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(token, ModulosAdmin.Pagamentos, AcaoPermissao.Visualizar);
            // Garante que nenhuma origem nova ficou sem pagamento (ex.: gravada por outro caminho).
            await PagamentosService.ReconciliarAsync(db);

            var todos = (await db.Pagamentos.AsNoTracking().ToListAsync()).Where(p => contexto.VeUnidade(p.Unidade)).ToList();
            var st = PagamentosService.Normalizar(status);
            var org = Normalizador.Texto(origem);
            var uni = Normalizador.Texto(unidade);
            // Item 16: varias palavras = todas precisam aparecer (mesmo padrao da tela).
            var palavras = Normalizador.Texto(busca).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lista = todos.Where(p =>
                    (string.IsNullOrWhiteSpace(status) || p.Status == st) &&
                    (org.Length == 0 || p.Origem == org) &&
                    (uni.Length == 0 || uni == "todas" || (uni == "sem-unidade" ? p.Unidade == "" : p.Unidade == uni)) &&
                    ((string.IsNullOrEmpty(de) && string.IsNullOrEmpty(ate)) || Normalizador.Dentro(Normalizador.Data(p.DataReferencia), de, ate)) &&
                    (!reembolso || p.ReembolsoPendente) &&
                    (palavras.Length == 0 || palavras.All(Normalizador.Texto($"{p.Id} {p.OrigemId} {p.Cliente} {p.Descricao} {p.Forma}").Contains)))
                .OrderByDescending(p => p.DataReferencia).ToList();

            decimal Soma(string s) => lista.Where(p => p.Status == s).Sum(p => p.Valor);
            return OkApi(new
            {
                itens = lista.Select(p => new
                {
                    p.Id, p.Origem, p.OrigemId, p.ClienteId, p.Cliente, p.Descricao, p.Valor, p.Forma, p.Status, p.ReembolsoPendente,
                    unidade = p.Unidade, unidadeNome = Normalizador.NomeUnidade(p.Unidade) is { Length: > 0 } n ? n : (p.Origem == PagamentosService.OrigemSeguro ? "—" : "Sem unidade"),
                    p.DataReferencia, atualizadoEm = DateTime.SpecifyKind(p.AtualizadoEm, DateTimeKind.Utc).ToString("O"), p.AtualizadoPor, p.Observacao
                }),
                resumo = new
                {
                    total = lista.Count,
                    pendentes = lista.Count(p => p.Status == PagamentosService.Pendente),
                    valorPendente = Soma(PagamentosService.Pendente),
                    aprovados = lista.Count(p => p.Status == PagamentosService.Aprovado),
                    valorAprovado = Soma(PagamentosService.Aprovado),
                    recusados = lista.Count(p => p.Status == PagamentosService.Recusado),
                    cancelados = lista.Count(p => p.Status == PagamentosService.Cancelado),
                    reembolsados = lista.Count(p => p.Status == PagamentosService.Reembolsado),
                    valorReembolsado = Soma(PagamentosService.Reembolsado),
                    // Reembolso pendente olha a base inteira visivel, nao so o filtro: e um alerta.
                    reembolsosPendentes = todos.Count(p => p.ReembolsoPendente)
                },
                status = PagamentosService.Todos
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpPost("{id}/status")]
    public async Task<IActionResult> Status(string id, [FromBody] StatusPagamentoRequest req)
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(req.Token ?? "", ModulosAdmin.Pagamentos, AcaoPermissao.Editar);
            var pag = await db.Pagamentos.FirstOrDefaultAsync(p => p.Id == id) ?? throw new Exception("Pagamento não encontrado.");
            if (!contexto.VeUnidade(pag.Unidade)) throw new AcessoNegadoException("Este pagamento é de outra unidade.");
            var antes = pag.Status;
            await PagamentosService.AlterarStatusAsync(db, pag, req.Status, contexto.Usuario.Email, req.Observacao);
            await db.SaveChangesAsync();
            await realtime.NotificarAsync("pagamentos", "status", new { pag.Id, pag.Status });
            // Alterar status de pagamento muda o "Pago/A pagar" do agendamento: avisa a agenda tambem.
            if (pag.Origem == PagamentosService.OrigemAgendamento) await realtime.NotificarAsync("agendamentos", "pagamento", new { pag.OrigemId, pag.Status });
            await eventos.RegistrarAsync(new("pagamento", $"Pagamento {pag.Status.ToLowerInvariant()}",
                pag.Status is PagamentosService.Recusado or PagamentosService.Reembolsado or PagamentosService.Cancelado ? "aviso" : "info",
                "admin", contexto.Usuario.Id, contexto.Usuario.Email, pag.Id,
                $"{antes} → {pag.Status} · {pag.Descricao} · {pag.Valor:C} · {pag.Origem} {pag.OrigemId}" + (string.IsNullOrWhiteSpace(req.Observacao) ? "" : $" · {req.Observacao!.Trim()}")), HttpContext);
            return OkApi(new { pag.Id, pag.Status, pag.ReembolsoPendente, message = $"Pagamento marcado como {pag.Status}." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    public record StatusPagamentoRequest(string? Token, string? Status, string? Observacao);
}
