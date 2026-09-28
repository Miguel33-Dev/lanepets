namespace LanePets.Services;

/// <summary>
/// 27/09: paginacao e busca no servidor num padrao so, para as listas do painel que crescem
/// com o tempo (Pedidos primeiro; Pagamentos e Log ja paginavam por conta propria).
///
/// - Pagina: padrao 50, maximo 500; limite/offset fora da faixa sao ajustados, nunca erro.
/// - Busca: mesma regra do LaneBusca (js/busca.js) — sem acento e maiuscula, varias palavras =
///   todas precisam aparecer, e numero e comparado so pelos digitos ("11 9999" acha "(11) 99999-0000").
/// Classe static (§6.34): calculo puro, sem banco.
/// </summary>
public static class ListaPaginada
{
    public const int Padrao = 50;
    public const int Maximo = 500;

    public sealed record Pagina<T>(int Total, int Offset, int Limite, bool TemMais, List<T> Itens);

    /// <summary>Corta a lista ja filtrada e ordenada (a ordem precisa ser estavel).</summary>
    public static Pagina<T> Cortar<T>(IReadOnlyList<T> lista, int limite, int offset)
    {
        limite = Math.Clamp(limite, 1, Maximo);
        offset = Math.Max(offset, 0);
        var itens = lista.Skip(offset).Take(limite).ToList();
        return new Pagina<T>(lista.Count, offset, limite, offset + itens.Count < lista.Count, itens);
    }

    private static string Digitos(string? v) => new((v ?? "").Where(char.IsDigit).ToArray());

    /// <summary>true quando TODAS as palavras do termo aparecem em algum dos campos (termo vazio = true).</summary>
    public static bool Combina(string? termo, params string?[] campos)
    {
        var palavras = Normalizador.Texto(termo).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (palavras.Length == 0) return true;
        // Separador entre campos: uma palavra nunca "emenda" o fim de um campo com o comeco do outro.
        var texto = string.Join(" \u0001 ", campos.Select(Normalizador.Texto));
        var soDigitos = string.Join(" ", campos.Select(Digitos));
        return palavras.All(p =>
        {
            if (texto.Contains(p, StringComparison.Ordinal)) return true;
            var d = Digitos(p);
            var semPontuacao = p.Count(c => !" ().-".Contains(c));
            return d.Length >= 3 && d.Length == semPontuacao && soDigitos.Contains(d, StringComparison.Ordinal);
        });
    }
}
