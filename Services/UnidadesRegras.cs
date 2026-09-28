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

    /// <summary>
    /// Unidade como ela deve ser GRAVADA (26/09): sempre o id do cadastro ("franco").
    /// Valor que nao corresponde a nenhuma unidade e mantido como veio (a Integridade
    /// aponta), para nao apagar historico; vazio continua vazio.
    /// </summary>
    public static string ParaGravar(string? valor)
    {
        var texto = (valor ?? "").Trim();
        return Normalizador.IdUnidade(texto) is { Length: > 0 } id ? id : texto;
    }

    /// <summary>
    /// Unificacao da unidade gravada pelo NOME (26/09). O site gravava "Franco da Rocha" e o
    /// painel "franco"; as comparacoes ja passavam pelo Normalizador, mas o dado ficava misto.
    /// Na subida (depois do backup e do RecarregarAsync), troca pelo id tudo o que for
    /// reconhecido, em todas as tabelas com unidade. Idempotente: na segunda vez nao ha nada
    /// para trocar. Devolve quantos registros mudaram.
    /// </summary>
    public static async Task<int> UnificarGravadasPeloNomeAsync(LanePetsDbContext db)
    {
        var mudou = 0;
        void Acertar<T>(IEnumerable<T> itens, Func<T, string> ler, Action<T, string> gravar)
        {
            foreach (var item in itens)
            {
                var atual = ler(item) ?? "";
                var novo = ParaGravar(atual);
                if (novo != atual) { gravar(item, novo); mudou++; }
            }
        }

        Acertar(await db.Agendamentos.ToListAsync(), a => a.Unidade, (a, v) => a.Unidade = v);
        Acertar(await db.Pets.ToListAsync(), p => p.Unidade, (p, v) => p.Unidade = v);
        Acertar(await db.EntradasESaidas.ToListAsync(), e => e.Unidade, (e, v) => e.Unidade = v);
        Acertar(await db.Pacotes.ToListAsync(), p => p.Unidade, (p, v) => p.Unidade = v);
        Acertar(await db.Pedidos.ToListAsync(), p => p.Unidade, (p, v) => p.Unidade = v);
        Acertar(await db.UsuariosAdministradores.Where(u => u.Unidade != "").ToListAsync(), u => u.Unidade, (u, v) => u.Unidade = v);

        if (mudou > 0) await db.SaveChangesAsync();
        return mudou;
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
