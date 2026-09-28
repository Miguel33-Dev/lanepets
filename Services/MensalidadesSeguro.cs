using System.Globalization;
using LanePets.Models;

namespace LanePets.Services;

/// <summary>
/// Cobranca mensal do Seguro Pet (26/09). Decisoes do Fabricio:
///   - a mensalidade de cada mes e gerada AUTOMATICAMENTE (sem clique da equipe);
///   - cada mes vira um Pagamento proprio (origem "seguro", Competencia "AAAA-MM"),
///     que aparece na tela de Pagamentos e no extrato do cliente;
///   - mes em aberto ha mais de <see cref="DiasTolerancia"/> dias deixa o seguro
///     INADIMPLENTE, sem cancelar; pagou, volta a ficar em dia sozinho.
///
/// Calculo puro (sem banco): quais meses sao devidos e em que situacao o contrato esta.
/// Quem grava e o PagamentosService.ReconciliarAsync; a situacao nao e gravada, e
/// calculada a partir dos pagamentos em toda leitura (nunca fica desatualizada).
///
/// Vencimento do mes N = data da contratacao + N meses, sempre contado a partir da
/// contratacao (31/01 -> 28/02 -> 31/03). O mes 0 e a propria contratacao.
/// Contrato cancelado para de gerar a partir da data do cancelamento.
/// </summary>
public static class MensalidadesSeguro
{
    /// <summary>Dias depois do vencimento ate o contrato ser considerado inadimplente.</summary>
    public const int DiasTolerancia = 10;

    public const string EmDia = "Em dia";
    public const string Aguardando = "Aguardando pagamento";
    public const string Inadimplente = "Inadimplente";
    public const string Encerrado = "Encerrado";

    private const string FormatoData = "yyyy-MM-ddTHH:mm:ss";

    public record Mensalidade(int Numero, string Competencia, DateTime Vencimento);

    public record Situacao(string Rotulo, int EmAberto, decimal ValorEmAberto, int DiasEmAtraso,
        string? VencimentoEmAberto, string? ProximoVencimento);

    /// <summary>So contrato feito pela area do cliente gera cobranca (tem conta e valor).
    /// Pedido de contato do site publico nao e cobranca.</summary>
    public static bool Cobravel(SolicitacaoSeguro s) => !string.IsNullOrEmpty(s.ClienteId) && s.Valor > 0;

    public static bool Cancelado(SolicitacaoSeguro s) => string.Equals(s.Status, "Cancelada", StringComparison.OrdinalIgnoreCase);

    public static DateTime Vencimento(SolicitacaoSeguro s, int numero) => s.CriadoEm.AddMonths(numero);

    public static string Competencia(DateTime vencimento) => vencimento.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>"2026-10" -> "10/2026".</summary>
    public static string CompetenciaTexto(string competencia) =>
        competencia.Length == 7 ? $"{competencia[5..]}/{competencia[..4]}" : competencia;

    public static string DataReferencia(DateTime data) => data.ToString(FormatoData, CultureInfo.InvariantCulture);

    public static DateTime? LerData(string? texto) =>
        DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>
    /// Meses devidos ate agora (o mes 0 sempre). Contrato cancelado: so os meses que
    /// venceram ate o cancelamento; cancelamento antigo sem data: so a contratacao.
    /// </summary>
    public static List<Mensalidade> Devidas(SolicitacaoSeguro s, DateTime agoraUtc)
    {
        var limite = Cancelado(s) ? s.DataCancelamento ?? s.CriadoEm : agoraUtc;
        var lista = new List<Mensalidade>();
        for (var n = 0; n < 1200; n++)
        {
            var venc = Vencimento(s, n);
            if (n > 0 && venc > limite) break;
            lista.Add(new(n, Competencia(venc), venc));
        }
        return lista;
    }

    /// <summary>Situacao da cobranca a partir dos pagamentos (mensalidades) do contrato.</summary>
    public static Situacao Calcular(SolicitacaoSeguro s, IEnumerable<Pagamento> doSeguro, DateTime agoraUtc)
    {
        var abertos = doSeguro
            .Where(p => p.Status is PagamentosService.Pendente or PagamentosService.Recusado)
            .Select(p => (Pagamento: p, Venc: LerData(p.DataReferencia) ?? s.CriadoEm))
            .OrderBy(x => x.Venc)
            .ToList();
        var valor = abertos.Sum(x => x.Pagamento.Valor);

        if (Cancelado(s)) return new(Encerrado, abertos.Count, valor, 0, null, null);

        var n = 0;
        while (n < 1200 && Vencimento(s, n) <= agoraUtc) n++;
        var proximo = DataReferencia(Vencimento(s, n));

        if (abertos.Count == 0) return new(EmDia, 0, 0m, 0, null, proximo);

        var maisAntigo = abertos[0].Venc;
        var dias = Math.Max(0, (int)Math.Floor((agoraUtc - maisAntigo).TotalDays));
        return new(dias > DiasTolerancia ? Inadimplente : Aguardando, abertos.Count, valor, dias,
            DataReferencia(maisAntigo), proximo);
    }

    /// <summary>
    /// Bloco "cobranca" devolvido pelas telas (painel e area do cliente). Null para o
    /// pedido de contato do site, que nao tem cobranca.
    /// </summary>
    public static object? Resumo(SolicitacaoSeguro s, IEnumerable<Pagamento> doSeguro, DateTime agoraUtc)
    {
        if (!Cobravel(s)) return null;
        var lista = doSeguro.ToList();
        var sit = Calcular(s, lista, agoraUtc);
        return new
        {
            situacao = sit.Rotulo,
            emAberto = sit.EmAberto,
            valorEmAberto = sit.ValorEmAberto,
            diasEmAtraso = sit.DiasEmAtraso,
            vencimentoEmAberto = sit.VencimentoEmAberto,
            proximoVencimento = sit.ProximoVencimento,
            toleranciaDias = DiasTolerancia,
            mensalidades = lista
                .OrderByDescending(p => p.Competencia, StringComparer.Ordinal)
                .Select(p => new
                {
                    p.Id,
                    competencia = CompetenciaTexto(p.Competencia),
                    vencimento = p.DataReferencia,
                    p.Valor,
                    p.Status,
                    p.ReembolsoPendente
                })
                .ToList()
        };
    }
}
