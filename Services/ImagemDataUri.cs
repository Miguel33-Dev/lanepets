namespace LanePets.Services;

/// <summary>
/// REGRA UNICA DE IMAGEM DO LANEPETS (item 10 do roadmap, 24/09).
///
/// Decisao: a imagem vive DENTRO do registro, como data URI
/// ("data:image/jpeg;base64,..."), e nao como arquivo em disco. Nesse tamanho
/// (foto reduzida no navegador para 320px, ~12-25 KB) nao compensa manter uma
/// pasta de uploads sincronizada com o banco: dado e exibicao sao a mesma coisa,
/// e o backup do lanepets.db ja leva as fotos junto.
///
/// Toda entidade com imagem (hoje Pet.FotoUrl; Produto no item 6) usa ESTA
/// classe, entao a regra e a mesma em qualquer lugar:
///
///   - formatos aceitos: JPEG, PNG e WebP (os que o navegador gera);
///   - conteudo base64 precisa decodificar de verdade;
///   - teto de tamanho (padrao 200 KB de texto);
///   - troca: so substitui quando vem imagem nova;
///   - remocao: so com pedido explicito (vazio/ausente PRESERVA a atual);
///   - sem imagem: a tela mostra o simbolo padrao (especie do pet / categoria).
/// </summary>
public static class ImagemDataUri
{
    public const int LimitePadraoBytes = 200 * 1024;

    private static readonly string[] TiposAceitos = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>Erro de imagem com mensagem pronta para a tela (vira HTTP 400).</summary>
    public sealed class ImagemInvalidaException(string mensagem) : Exception(mensagem) { }

    /// <summary>
    /// Decide o valor final da imagem de um registro. Fecha a classe de bug
    /// "editei outro campo e a foto sumiu": so o pedido explicito de remocao
    /// apaga; vazio/ausente mantem o que ja estava salvo.
    /// </summary>
    public static string Resolver(string atual, string? enviada, bool remover, int limiteBytes = LimitePadraoBytes)
    {
        if (remover) return "";
        var texto = (enviada ?? "").Trim();
        if (texto.Length == 0) return atual ?? "";
        return Validar(texto, limiteBytes);
    }

    /// <summary>Confere que e mesmo imagem, num formato aceito e dentro do tamanho.</summary>
    public static string Validar(string? valor, int limiteBytes = LimitePadraoBytes)
    {
        var texto = (valor ?? "").Trim();
        if (texto.Length == 0) return "";
        if (!texto.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            throw new ImagemInvalidaException("Formato de imagem não reconhecido. Envie um arquivo JPG, PNG ou WebP.");

        var virgula = texto.IndexOf(',');
        if (virgula < 0) throw new ImagemInvalidaException("Não foi possível ler a imagem. Tente enviar outra foto.");

        var cabecalho = texto[..virgula].ToLowerInvariant();
        if (!cabecalho.Contains(";base64"))
            throw new ImagemInvalidaException("Não foi possível ler a imagem. Tente enviar outra foto.");

        var tipo = cabecalho["data:".Length..].Split(';')[0];
        if (!TiposAceitos.Contains(tipo))
            throw new ImagemInvalidaException("Formato de imagem não aceito. Envie um arquivo JPG, PNG ou WebP.");

        if (texto.Length > limiteBytes)
            throw new ImagemInvalidaException("A foto ficou grande demais. Escolha uma imagem menor.");

        var conteudo = texto[(virgula + 1)..];
        Span<byte> buffer = new byte[((conteudo.Length * 3) / 4) + 4];
        if (!Convert.TryFromBase64String(conteudo, buffer, out var escritos) || escritos == 0)
            throw new ImagemInvalidaException("Não foi possível ler a imagem. Tente enviar outra foto.");

        return texto;
    }
}
