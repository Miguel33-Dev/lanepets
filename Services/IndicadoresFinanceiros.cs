using System.Text.Json;
using LanePets.Models;

namespace LanePets.Services;

/// <summary>
/// Item 11 (26/09): calculo financeiro do Dashboard (GET /api/admin/resumo) e do Relatorio
/// (GET /api/admin/relatorio). Estava dentro do AdminSyncController; veio para ca sem mudar
/// uma linha de regra. Tudo aqui e funcao pura: recebe as listas ja lidas do banco (e ja
/// filtradas pela permissao de quem pediu) e devolve os numeros — nao le banco, nao grava nada.
/// </summary>
public static class IndicadoresFinanceiros
{
    public sealed record Consolidado(
        List<Agendamento> Atendimentos, List<Pedido> Pedidos, decimal Receita, decimal ReceitaPedidos,
        decimal Despesas, decimal Transporte, int PagamentosPagos, int PagamentosPendentes, int Cancelados,
        Dictionary<string, decimal> Categorias)
    {
        public decimal Resultado => Receita - Despesas;
        public decimal Margem => Receita == 0 ? 0 : Math.Round(Resultado / Receita * 100, 1);
        public decimal Ticket => Atendimentos.Count == 0 ? 0 : Math.Round(Receita / Atendimentos.Count, 2);
    }

    /// <summary>
    /// Consolida um periodo. Agendamentos cancelados NAO entram na receita nem
    /// na contagem de atendimentos — sao reportados a parte.
    /// </summary>
    public static Consolidado Calcular(List<Agendamento> agendamentos, List<EntradaSaida> lancamentos, List<Pedido> pedidos, string? de, string? ate, string filtroUnidade)
    {
        bool DaUnidade(string? valor)
        {
            if (filtroUnidade is "todas" or "") return true;
            // Item 5: lista de unidades dinamica. "sem-unidade" = registro antigo
            // ou gravado sem unidade reconhecida.
            var id = Normalizador.IdUnidade(valor);
            return filtroUnidade == "sem-unidade" ? id == "" : id == filtroUnidade;
        }

        var doPeriodo = agendamentos
            .Where(a => Normalizador.Dentro(Normalizador.Data(a.DataHora), de, ate) && DaUnidade(a.Unidade))
            .ToList();

        var cancelados = doPeriodo.Count(a => Normalizador.Status(a.Status) == "Cancelado");
        var ativos = doPeriodo.Where(a => Normalizador.Status(a.Status) != "Cancelado").ToList();

        var lancamentosDoPeriodo = lancamentos
            .Where(e => Normalizador.Dentro(Normalizador.Data(e.Data), de, ate) && DaUnidade(e.Unidade))
            .ToList();

        // Item 5: pedido tem unidade de retirada. Pedido antigo (sem unidade)
        // entra em "todas" e em "sem-unidade", como antes.
        var pedidosDoPeriodo = pedidos.Where(p => Normalizador.Dentro(p.CriadoEm.ToString("yyyy-MM-dd"), de, ate)
                                 && !string.Equals(p.Status, "Cancelado", StringComparison.OrdinalIgnoreCase)
                                 && DaUnidade(p.Unidade)).ToList();

        var receitaAgendamentos = ativos.Sum(a => a.Total);
        var receitaPedidos = pedidosDoPeriodo.Sum(p => p.Total);
        var entradasManuais = lancamentosDoPeriodo.Where(e => Normalizador.Texto(e.Tipo) == "entrada").Sum(e => e.Valor);
        var despesas = lancamentosDoPeriodo.Where(e => Normalizador.Texto(e.Tipo).StartsWith("saida") || Normalizador.Texto(e.Tipo) == "saida").Sum(e => e.Valor);

        var transporte = ativos.Sum(a => a.ValorTransporte);
        var categorias = new Dictionary<string, decimal>
        {
            ["Servicos"] = ativos.Sum(a => Math.Max(0, a.Total - a.ValorTransporte)),
            ["Transporte"] = transporte,
            ["Produtos"] = receitaPedidos,
            ["Outros"] = entradasManuais
        };

        return new Consolidado(
            ativos, pedidosDoPeriodo,
            receitaAgendamentos + receitaPedidos + entradasManuais,
            receitaPedidos, despesas, transporte,
            ativos.Count(a => Normalizador.Texto(a.PagamentoStatus) == "pago"),
            ativos.Count(a => Normalizador.Texto(a.PagamentoStatus) != "pago"),
            cancelados, categorias);
    }

    /// <summary>Mesmo criterio de unidade do Calcular(): "todas", "sem-unidade" ou o id da unidade.</summary>
    public static bool NaUnidade(string? valor, string filtroUnidade)
    {
        if (filtroUnidade is "todas" or "") return true;
        var id = Normalizador.IdUnidade(valor);
        return filtroUnidade == "sem-unidade" ? id == "" : id == filtroUnidade;
    }

    public static (string? De, string? Ate) PeriodoAnterior(string? de, string? ate)
    {
        if (!DateTime.TryParse(de, out var inicio) || !DateTime.TryParse(ate, out var fim)) return (null, null);
        var dias = (fim - inicio).Days + 1;
        var fimAnterior = inicio.AddDays(-1);
        return (fimAnterior.AddDays(-dias + 1).ToString("yyyy-MM-dd"), fimAnterior.ToString("yyyy-MM-dd"));
    }

    public static object SerieMensal(List<Agendamento> agendamentos, List<EntradaSaida> lancamentos, List<Pedido> pedidos, string filtroUnidade)
    {
        var hoje = DateTime.Today;
        return Enumerable.Range(0, 6).Select(i =>
        {
            var referencia = new DateTime(hoje.Year, hoje.Month, 1).AddMonths(-(5 - i));
            var de = referencia.ToString("yyyy-MM-dd");
            var ate = referencia.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");
            var d = Calcular(agendamentos, lancamentos, pedidos, de, ate, filtroUnidade);
            return new { mes = referencia.ToString("yyyy-MM"), receita = d.Receita, despesas = d.Despesas };
        }).ToArray();
    }

    public static string NomeUnidade(string id)
        => id == "sem-unidade" ? "Sem unidade / antigos" : (Normalizador.NomeUnidade(id) is { Length: > 0 } nome ? nome : id);

    /// <summary>Ids para o bloco "por unidade" dos relatorios: todas as unidades conhecidas + "sem-unidade".</summary>
    public static string[] IdsPorUnidade()
        => Normalizador.Unidades.Select(u => u.Id).Append("sem-unidade").ToArray();

    public static IEnumerable<string> NomesDosServicos(string? servicosJson, Dictionary<string, string> nomesPorId)
    {
        if (string.IsNullOrWhiteSpace(servicosJson)) yield break;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(servicosJson); } catch { yield break; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) yield break;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string? nome = null;
                if (item.ValueKind == JsonValueKind.Object)
                {
                    if (item.TryGetProperty("nome", out var n) && n.ValueKind == JsonValueKind.String) nome = n.GetString();
                    if (string.IsNullOrWhiteSpace(nome) && item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        nomesPorId.TryGetValue(idEl.GetString() ?? "", out nome);
                }
                else if (item.ValueKind == JsonValueKind.String) nome = item.GetString();
                if (!string.IsNullOrWhiteSpace(nome)) yield return nome!.Trim();
            }
        }
    }

    /// <summary>
    /// Serie de receita/despesa/saldo adaptada ao tamanho do periodo: diaria
    /// ate 31 dias, semanal ate 180 dias, mensal acima disso — nunca gera mais
    /// que ~60 pontos, entao o grafico/tabela no navegador fica legivel.
    /// </summary>
    public static List<object> SeriePeriodo(List<Agendamento> agendamentos, List<EntradaSaida> lancamentos, List<Pedido> pedidos, string? de, string? ate, string filtroUnidade)
    {
        if (!DateTime.TryParse(de, out var inicio) || !DateTime.TryParse(ate, out var fim) || fim < inicio)
            return new();

        var dias = (fim - inicio).Days + 1;
        var pontos = new List<(string Rotulo, DateTime Inicio, DateTime Fim)>();

        if (dias <= 31)
        {
            for (var d = inicio; d <= fim; d = d.AddDays(1))
                pontos.Add((d.ToString("dd/MM"), d, d));
        }
        else if (dias <= 180)
        {
            var cursor = inicio;
            while (cursor <= fim)
            {
                var fimSemana = cursor.AddDays(6) > fim ? fim : cursor.AddDays(6);
                pontos.Add(($"{cursor:dd/MM}-{fimSemana:dd/MM}", cursor, fimSemana));
                cursor = fimSemana.AddDays(1);
            }
        }
        else
        {
            var cursor = new DateTime(inicio.Year, inicio.Month, 1);
            while (cursor <= fim)
            {
                var fimMes = cursor.AddMonths(1).AddDays(-1);
                pontos.Add((cursor.ToString("MM/yyyy"), cursor < inicio ? inicio : cursor, fimMes > fim ? fim : fimMes));
                cursor = cursor.AddMonths(1);
            }
        }

        return pontos.Select(p =>
        {
            var d = Calcular(agendamentos, lancamentos, pedidos, p.Inicio.ToString("yyyy-MM-dd"), p.Fim.ToString("yyyy-MM-dd"), filtroUnidade);
            return (object)new { periodo = p.Rotulo, receita = d.Receita, despesas = d.Despesas, saldo = d.Resultado };
        }).ToList();
    }
}
