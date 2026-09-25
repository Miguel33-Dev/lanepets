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

    public static string Unidade(string? valor) => Texto(valor) switch
    {
        "franco" or "franco da rocha" => "Franco",
        "caieiras" => "Caieiras",
        _ => ""
    };

    public static string Status(string? valor) => Texto(valor) switch
    {
        "pendente" or "aguardando" or "agendado" => "Pendente",
        "em andamento" or "em_andamento" or "andamento" or "iniciado" or "em processo" => "Em andamento",
        "entregue" or "finalizado" or "finalizado/entregue" or "concluido" or "concluído" => "Entregue",
        "cancelado" => "Cancelado",
        _ => ""
    };

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
