using System.IO.Compression;
using System.Security;
using System.Text;

namespace LanePets.Services;

/// <summary>
/// 29/09: gerador minimo de planilha .xlsx (Office Open XML) — sem pacote NuGet: o arquivo e um ZIP com
/// alguns XML, montado com System.IO.Compression. Suficiente para o relatorio: varias abas, primeira linha
/// de cada bloco em negrito, numeros como numero (somam no Excel), dinheiro com 2 casas, texto como texto.
///
/// Uso: PlanilhaXlsx.Gerar([new("Resumo", linhas), ...]) onde cada linha e object?[]:
///   string -> texto · int/long -> inteiro · decimal/double -> numero com 2 casas · null -> celula vazia ·
///   PlanilhaXlsx.Titulo("x") -> texto em negrito (cabecalhos).
/// </summary>
public static class PlanilhaXlsx
{
    public sealed record Aba(string Nome, List<object?[]> Linhas);
    public sealed record Negrito(string Texto);
    public static Negrito Titulo(string texto) => new(texto);

    public static byte[] Gerar(IReadOnlyList<Aba> abas)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Escrever(string caminho, string conteudo)
            {
                using var w = new StreamWriter(zip.CreateEntry(caminho, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                w.Write(conteudo);
            }

            var nomes = NomesUnicos(abas.Select(a => a.Nome));
            Escrever("[Content_Types].xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>"""
                + string.Concat(abas.Select((_, i) => $"""<Override PartName="/xl/worksheets/sheet{i + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>"""))
                + "</Types>");
            Escrever("_rels/.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Escrever("xl/workbook.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>"""
                + string.Concat(nomes.Select((n, i) => $"""<sheet name="{Esc(n)}" sheetId="{i + 1}" r:id="rId{i + 1}"/>"""))
                + "</sheets></workbook>");
            Escrever("xl/_rels/workbook.xml.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
                + string.Concat(abas.Select((_, i) => $"""<Relationship Id="rId{i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i + 1}.xml"/>"""))
                + $"""<Relationship Id="rId{abas.Count + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            // Estilos: 0 normal · 1 negrito · 2 numero com 2 casas
            Escrever("xl/styles.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.00"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""");
            for (var i = 0; i < abas.Count; i++) Escrever($"xl/worksheets/sheet{i + 1}.xml", Planilha(abas[i].Linhas));
        }
        return memoria.ToArray();
    }

    private static string Planilha(List<object?[]> linhas)
    {
        var colunas = linhas.Count == 0 ? 0 : linhas.Max(l => l.Length);
        var larguras = new double[colunas];
        foreach (var l in linhas)
            for (var c = 0; c < l.Length; c++)
                larguras[c] = Math.Max(larguras[c], Math.Min(60, Texto(l[c]).Length + 2));

        var sb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        if (colunas > 0)
            sb.Append("<cols>").Append(string.Concat(larguras.Select((w, c) => $"""<col min="{c + 1}" max="{c + 1}" width="{Math.Max(10, w).ToString(System.Globalization.CultureInfo.InvariantCulture)}" customWidth="1"/>"""))).Append("</cols>");
        sb.Append("<sheetData>");
        for (var r = 0; r < linhas.Count; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < linhas[r].Length; c++)
            {
                var refCel = Coluna(c) + (r + 1);
                switch (linhas[r][c])
                {
                    case null: break;
                    case Negrito n: sb.Append($"<c r=\"{refCel}\" t=\"inlineStr\" s=\"1\"><is><t xml:space=\"preserve\">{Esc(n.Texto)}</t></is></c>"); break;
                    case int or long: sb.Append($"<c r=\"{refCel}\"><v>{Convert.ToString(linhas[r][c], System.Globalization.CultureInfo.InvariantCulture)}</v></c>"); break;
                    case decimal or double or float: sb.Append($"<c r=\"{refCel}\" s=\"2\"><v>{Convert.ToString(linhas[r][c], System.Globalization.CultureInfo.InvariantCulture)}</v></c>"); break;
                    default: sb.Append($"<c r=\"{refCel}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(Texto(linhas[r][c]))}</t></is></c>"); break;
                }
            }
            sb.Append("</row>");
        }
        return sb.Append("</sheetData></worksheet>").ToString();
    }

    private static string Texto(object? v) => v switch { null => "", Negrito n => n.Texto, _ => Convert.ToString(v, System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) ?? "" };

    private static string Coluna(int indice)
    {
        var nome = "";
        for (var n = indice + 1; n > 0; n = (n - 1) / 26) nome = (char)('A' + (n - 1) % 26) + nome;
        return nome;
    }

    /// <summary>Excel: nome da aba ate 31 caracteres, sem []:*?/\ e sem repetir.</summary>
    private static List<string> NomesUnicos(IEnumerable<string> nomes)
    {
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var saida = new List<string>();
        foreach (var bruto in nomes)
        {
            var limpo = new string((bruto ?? "Aba").Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
            if (limpo.Length == 0) limpo = "Aba";
            if (limpo.Length > 31) limpo = limpo[..31];
            var nome = limpo;
            for (var i = 2; !usados.Add(nome); i++) nome = (limpo.Length > 28 ? limpo[..28] : limpo) + " " + i;
            saida.Add(nome);
        }
        return saida;
    }

    // XML nao aceita caracteres de controle (exceto tab/quebra): some com eles antes de escapar.
    private static string Esc(string texto)
        => SecurityElement.Escape(new string(texto.Where(ch => ch == '\t' || ch == '\n' || ch == '\r' || ch >= ' ').ToArray())) ?? "";
}
