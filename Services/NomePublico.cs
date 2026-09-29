namespace LanePets.Services;

/// <summary>
/// Seguranca — Etapa 3 da auditoria (29/09), item G: no site publico (avaliacoes) o tutor aparece com o primeiro
/// nome e a inicial do ultimo sobrenome — "Fabrício Miguel da Silva" vira "Fabrício S.". O nome completo continua
/// no banco e no painel (moderacao); so a vitrine publica encolhe.
/// </summary>
public static class NomePublico
{
    private static readonly HashSet<string> Particulas = new(StringComparer.OrdinalIgnoreCase) { "da", "de", "do", "das", "dos", "e" };

    public static string Curto(string? nomeCompleto)
    {
        var partes = (nomeCompleto ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (partes.Length == 0) return "Cliente";
        if (partes.Length == 1) return partes[0];
        var ultimo = partes.Skip(1).LastOrDefault(p => !Particulas.Contains(p));
        return ultimo is null ? partes[0] : $"{partes[0]} {char.ToUpperInvariant(ultimo[0])}.";
    }
}
