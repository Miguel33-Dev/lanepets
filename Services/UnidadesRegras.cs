using System.Text.Json;
using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Regras de unidade compartilhadas (item 5 do roadmap, 24/09): recarga do
/// registro dinamico do Normalizador, servicos oferecidos e capacidade por
/// horario. Um lugar so, usado pela area do cliente, pelo painel e pelo CRUD.
/// </summary>
public static class UnidadesRegras
{
    public const int CapacidadeMaxima = 20;

    /// <summary>Refaz Normalizador.Unidades a partir da tabela. Chamar na subida e depois de cada escrita.</summary>
    public static async Task RecarregarAsync(LanePetsDbContext db)
    {
        var unidades = await db.Unidades.AsNoTracking().ToListAsync();
        Normalizador.RegistrarUnidades(unidades.Select(u => (u.Id, u.Nome, u.Ativa)));
    }

    /// <summary>Ids dos servicos da unidade. Lista vazia = oferece todos.</summary>
    public static List<string> Servicos(Unidade unidade)
    {
        try { return JsonSerializer.Deserialize<List<string>>(unidade.ServicosJson ?? "[]") ?? new(); }
        catch { return new(); }
    }

    public static bool OfereceServico(Unidade unidade, string servicoId)
    {
        var lista = Servicos(unidade);
        return lista.Count == 0 || lista.Contains(servicoId, StringComparer.OrdinalIgnoreCase);
    }

    public static int CapacidadeDe(Unidade? unidade) => Math.Clamp(unidade?.Capacidade ?? 1, 1, CapacidadeMaxima);

    /// <summary>"yyyy-MM-ddTHH:mm" de qualquer forma gravada ("...T10:00", "...T10:00:00", "... 10:00").</summary>
    public static string ChaveHorario(string? dataHora)
    {
        var texto = (dataHora ?? "").Trim().Replace(' ', 'T');
        return texto.Length >= 16 ? texto[..16] : texto;
    }

    /// <summary>
    /// Quantos agendamentos ativos (nao cancelados) a unidade ja tem naquele
    /// horario. Compara a unidade NORMALIZADA: o painel grava "franco" e a area
    /// do cliente grava "Franco da Rocha" — antes os dois nao se enxergavam e
    /// dava para marcar dois pets no mesmo horario, um por cada lado.
    /// </summary>
    public static async Task<int> OcupadosAsync(LanePetsDbContext db, string unidade, string dataHora, string? ignorarId = null)
    {
        var id = Normalizador.IdUnidade(unidade);
        var chave = ChaveHorario(dataHora);
        if (id.Length == 0 || chave.Length < 16) return 0;
        var dia = chave[..10];
        var candidatos = await db.Agendamentos.AsNoTracking()
            .Where(a => a.DataHora.StartsWith(dia) && a.Status != "Cancelado")
            .Select(a => new { a.Id, a.Unidade, a.DataHora, a.Status })
            .ToListAsync();
        return candidatos.Count(a => a.Id != ignorarId
                                     && Normalizador.Status(a.Status) != "Cancelado"
                                     && Normalizador.IdUnidade(a.Unidade) == id
                                     && ChaveHorario(a.DataHora) == chave);
    }

    public static async Task<Unidade?> AcharAsync(LanePetsDbContext db, string? valor)
    {
        var id = Normalizador.IdUnidade(valor);
        return id.Length == 0 ? null : await db.Unidades.FirstOrDefaultAsync(u => u.Id == id);
    }
}
