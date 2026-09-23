using System.Text;

namespace LanePets.Services;

public static class CsvService
{
    public static List<string[]> Read(string path)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var text = File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((c == '\n' || c == '\r') && !quoted)
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                if (row.Any(x => !string.IsNullOrWhiteSpace(x))) rows.Add(row.ToArray());
                row.Clear();
            }
            else field.Append(c);
        }
        row.Add(field.ToString());
        if (row.Any(x => !string.IsNullOrWhiteSpace(x))) rows.Add(row.ToArray());
        return rows;
    }

    public static decimal Decimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var s = value.Trim().Replace("R$", "").Replace(" ", "");
        if (s.Contains(',')) s = s.Replace(".", "").Replace(',', '.');
        return System.Decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
}
