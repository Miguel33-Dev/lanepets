using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// 29/09: cartao fidelidade. Cada atendimento CONCLUIDO do cliente vale 1 selo; completou o cartao
/// (<see cref="Selos"/>, padrao 10), ganha o premio (<see cref="Premio"/>, padrao "1 banho grátis").
/// A equipe entrega o premio na unidade e marca "Resgatar" no painel — so o resgate e gravado.
/// Nada inventado: sem atendimento concluido, o cartao aparece vazio. Configuravel por
/// LanePets:Fidelidade:Selos (2 a 50) e LanePets:Fidelidade:Premio. Classe static (§6.34).
/// </summary>
public static class FidelidadeService
{
    public const int SelosPadrao = 10;
    public const string PremioPadrao = "1 banho grátis";
    public const string SemPremio = "Este cliente ainda não completou o cartão fidelidade.";

    public static int Selos(IConfiguration config)
        => int.TryParse(config["LanePets:Fidelidade:Selos"], out var n) && n is >= 2 and <= 50 ? n : SelosPadrao;

    public static string Premio(IConfiguration config)
        => config["LanePets:Fidelidade:Premio"] is { Length: > 0 } p ? p.Trim() : PremioPadrao;

    /// <param name="SelosNoCartao">selos do cartao em andamento (0 a Selos-1)</param>
    /// <param name="PremiosDisponiveis">cartoes completos ainda nao resgatados</param>
    public sealed record Cartao(int Selos, string Premio, int Concluidos, int SelosNoCartao, int PremiosDisponiveis,
        int Resgatados, DateTime? UltimoResgate, int FaltamParaProximo);

    public static async Task<Cartao> CartaoAsync(LanePetsDbContext db, IConfiguration config, string clienteId)
    {
        var concluidos = (await db.Agendamentos.AsNoTracking().Where(a => a.ClienteId == clienteId).Select(a => a.Status).ToListAsync())
            .Count(s => StatusAgendamento.Exibir(s) == StatusAgendamento.Concluido);
        var resgates = await db.ResgatesFidelidade.AsNoTracking().Where(r => r.ClienteId == clienteId).ToListAsync();
        var n = Selos(config);
        var saldo = Math.Max(0, concluidos - resgates.Sum(r => r.Selos));
        var noCartao = saldo % n;
        return new Cartao(n, Premio(config), concluidos, noCartao, saldo / n, resgates.Count,
            resgates.Count == 0 ? null : resgates.Max(r => r.CriadoEm), n - noCartao);
    }

    /// <summary>Marca a entrega de UM premio (gasta um cartao completo). Sem cartao completo: erro de negocio.</summary>
    public static async Task<Cartao> ResgatarAsync(LanePetsDbContext db, IConfiguration config, string clienteId, UsuarioAdministrador autor)
    {
        if (!await db.Clientes.AnyAsync(c => c.Id == clienteId)) throw new Exception("Cliente não encontrado.");
        var atual = await CartaoAsync(db, config, clienteId);
        if (atual.PremiosDisponiveis < 1) throw new Exception(SemPremio);
        db.ResgatesFidelidade.Add(new ResgateFidelidade
        {
            Id = "FID-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            ClienteId = clienteId, Selos = atual.Selos, Premio = atual.Premio,
            AutorId = autor.Id, Autor = autor.Email, CriadoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return await CartaoAsync(db, config, clienteId);
    }
}
