using LanePets.Data;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;

/// <summary>
/// Administração > Log de eventos (item 15 do roadmap, 24/09).
///
/// SO o Administrador Geral consulta: o log tem e-mail, IP e o historico de
/// quem fez o que — e a mesma regra da Auditoria de administradores. Um admin
/// comum (nem com acesso total) nem funcionario alcancam esta rota: 403.
///
/// Somente leitura. Nao existe endpoint para editar ou apagar evento.
/// </summary>
[Route("api/admin/eventos")]
public class EventosController(LanePetsDbContext db, PermissaoService permissoes) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string token = "",
        [FromQuery] string? categoria = null,
        [FromQuery] string? nivel = null,
        [FromQuery] string? de = null,
        [FromQuery] string? ate = null,
        [FromQuery] string? busca = null,
        [FromQuery] int limite = 100,
        [FromQuery] int offset = 0)
    {
        try
        {
            await permissoes.ExigirGeralAsync(token);

            var q = db.EventosLog.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(categoria)) q = q.Where(e => e.Categoria == categoria);
            if (!string.IsNullOrWhiteSpace(nivel)) q = q.Where(e => e.Nivel == nivel);

            // "de"/"ate" chegam como data local (yyyy-MM-dd, horario de Sao Paulo
            // na tela); o banco guarda UTC. Converte os limites do dia local.
            if (DateTime.TryParse(de, out var dataDe))
            {
                var inicio = DateTime.SpecifyKind(dataDe.Date, DateTimeKind.Local).ToUniversalTime();
                q = q.Where(e => e.DataHora >= inicio);
            }
            if (DateTime.TryParse(ate, out var dataAte))
            {
                var fim = DateTime.SpecifyKind(dataAte.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
                q = q.Where(e => e.DataHora < fim);
            }
            if (!string.IsNullOrWhiteSpace(busca))
            {
                var b = busca.Trim().ToLower();
                q = q.Where(e => e.Acao.ToLower().Contains(b) || e.Autor.ToLower().Contains(b) || e.Detalhes.ToLower().Contains(b)
                              || e.AlvoId.ToLower().Contains(b) || e.Referencia.ToLower().Contains(b) || e.Ip.Contains(b));
            }

            var total = await q.CountAsync();
            limite = Math.Clamp(limite, 1, 500);
            offset = Math.Max(0, offset);
            var itens = await q.OrderByDescending(e => e.DataHora).Skip(offset).Take(limite).ToListAsync();

            // Resumo das ultimas 24h — numeros reais, contados no banco.
            var desde = DateTime.UtcNow.AddHours(-24);
            var recentes = db.EventosLog.AsNoTracking().Where(e => e.DataHora >= desde);
            var resumo = new
            {
                total24h = await recentes.CountAsync(),
                loginsRecusados24h = await recentes.CountAsync(e => e.Categoria == "autenticacao" && e.Nivel == "aviso"),
                avisos24h = await recentes.CountAsync(e => e.Nivel == "aviso"),
                erros24h = await recentes.CountAsync(e => e.Nivel == "erro")
            };

            var categorias = await db.EventosLog.AsNoTracking().Select(e => e.Categoria).Distinct().OrderBy(c => c).ToListAsync();

            return OkApi(new
            {
                total,
                offset,
                limite,
                resumo,
                categorias,
                itens = itens.Select(e => new
                {
                    e.Id,
                    dataHora = DateTime.SpecifyKind(e.DataHora, DateTimeKind.Utc).ToString("O"),
                    e.Nivel,
                    e.Categoria,
                    e.Acao,
                    e.Origem,
                    e.AutorId,
                    e.Autor,
                    e.AlvoId,
                    e.Detalhes,
                    e.Ip,
                    e.Referencia
                })
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
}
