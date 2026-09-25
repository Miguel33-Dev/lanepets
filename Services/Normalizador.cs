using System.Globalization;
using System.Text;

namespace LanePets.Services;

/// <summary>
/// Regras de normalizacao compartilhadas por toda a API (unidade, status, data,
/// texto sem acento). Ficavam apenas dentro do DataController; foram extraidas
/// para ca para que o painel e a area do cliente contem exatamente da mesma
/// forma. Nenhuma regra mudou de comportamento.
/// </summary>
public static class Normalizador
{
    public static string Texto(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "";
        var semAcento = valor.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return new string(semAcento.ToArray()).ToLowerInvariant().Trim();
    }

    // -----------------------------------------------------------------------
    // UNIDADES — registro dinamico (item 5 do roadmap, 24/09)
    //
    // Antes a lista era fixa (so "franco" e "caieiras"). Agora o registro e
    // montado a partir da tabela Unidades na subida e refeito a cada escrita do
    // CRUD de unidades (RegistrarUnidades). Cada unidade responde por varios
    // apelidos (id, nome) e tem tres formas:
    //
    //     Id      -> "franco", "caieiras", "jundiai"   (o que o painel grava)
    //     Rotulo  -> "Franco", "Caieiras", "Jundiaí"   (o que as telas comparam)
    //     Nome    -> "Franco da Rocha", ...            (o que se mostra por extenso)
    //
    // Franco e Caieiras mantem os rotulos curtos de sempre, para nenhuma tela
    // antiga quebrar. Valor que nao casa com nenhuma unidade -> "" (sem unidade).
    // -----------------------------------------------------------------------
    public record UnidadeRegistrada(string Id, string Rotulo, string Nome, bool Ativa);

    private static readonly Dictionary<string, string> RotulosLegados = new()
    {
        ["franco"] = "Franco",
        ["caieiras"] = "Caieiras"
    };

    private static volatile Dictionary<string, UnidadeRegistrada> _apelidos = Montar(
    [
        ("franco", "Franco da Rocha", true),
        ("caieiras", "Caieiras", true)
    ]);

    private static volatile IReadOnlyList<UnidadeRegistrada> _lista = _apelidos.Values.Distinct().OrderBy(u => u.Nome).ToList();

    private static Dictionary<string, UnidadeRegistrada> Montar(IEnumerable<(string Id, string Nome, bool Ativa)> unidades)
    {
        var mapa = new Dictionary<string, UnidadeRegistrada>();
        foreach (var (id, nome, ativa) in unidades)
        {
            var chave = Texto(id);
            if (chave.Length == 0) continue;
            var rotulo = RotulosLegados.TryGetValue(chave, out var legado) ? legado : (string.IsNullOrWhiteSpace(nome) ? id : nome.Trim());
            var registro = new UnidadeRegistrada(chave, rotulo, string.IsNullOrWhiteSpace(nome) ? rotulo : nome.Trim(), ativa);
            mapa[chave] = registro;
            if (Texto(nome).Length > 0) mapa.TryAdd(Texto(nome), registro);
            mapa.TryAdd(Texto(rotulo), registro);
        }
        return mapa;
    }

    /// <summary>Refaz o registro a partir da tabela Unidades. Chamado na subida e a cada escrita do CRUD.</summary>
    public static void RegistrarUnidades(IEnumerable<(string Id, string Nome, bool Ativa)> unidades)
    {
        var mapa = Montar(unidades);
        _apelidos = mapa;
        _lista = mapa.Values.Distinct().OrderBy(u => u.Nome).ToList();
    }

    /// <summary>Todas as unidades conhecidas (ativas e inativas), por nome.</summary>
    public static IReadOnlyList<UnidadeRegistrada> Unidades => _lista;

    private static UnidadeRegistrada? Achar(string? valor)
        => _apelidos.TryGetValue(Texto(valor), out var u) ? u : null;

    /// <summary>Rotulo de comparacao ("Franco", "Caieiras", "Jundiaí"...) ou "" quando nao e unidade conhecida.</summary>
    public static string Unidade(string? valor) => Achar(valor)?.Rotulo ?? "";

    /// <summary>Id da unidade ("franco", "jundiai"...) ou "".</summary>
    public static string IdUnidade(string? valor) => Achar(valor)?.Id ?? "";

    /// <summary>Nome por extenso ("Franco da Rocha") ou "".</summary>
    public static string NomeUnidade(string? valor) => Achar(valor)?.Nome ?? "";

    /// <summary>Status do agendamento (item 4): delega para StatusAgendamento.</summary>
    public static string Status(string? valor) => StatusAgendamento.Normalizar(valor);

    /// <summary>Extrai o "yyyy-MM-dd" de uma data guardada como texto.</summary>
    public static string Data(string? valor)
    {
        if (DateTime.TryParse(valor, out var d)) return d.ToString("yyyy-MM-dd");
        return (valor ?? "").Length >= 10 ? valor![..10] : "";
    }

    /// <summary>true quando a data (yyyy-MM-dd) esta dentro do intervalo; limites vazios sao ignorados.</summary>
    public static bool Dentro(string data, string? de, string? ate)
        => !string.IsNullOrEmpty(data)
           && (string.IsNullOrEmpty(de) || string.CompareOrdinal(data, de) >= 0)
           && (string.IsNullOrEmpty(ate) || string.CompareOrdinal(data, ate) <= 0);
}
