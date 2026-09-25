using System.Text.Json;
using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Item 8 do roadmap (25/09): pagamentos.
///
/// UM pagamento por origem (agendamento, pedido, contratacao de seguro).
/// A regra inteira mora aqui:
///   - <see cref="ReconciliarAsync"/> cria o pagamento que falta, acompanha
///     valor/forma enquanto ele esta Pendente e aplica o cancelamento da origem
///     (decisao do Fabricio: Pendente -> Cancelado sozinho; Aprovado -> fica
///     "reembolso pendente" para a equipe marcar Reembolsado).
///   - <see cref="AlterarStatus"/> e a unica troca de status feita pela equipe,
///     com as transicoes permitidas, e espelha o campo antigo da origem.
/// Os campos antigos continuam existindo para os relatorios nao mudarem:
///   Agendamento.PagamentoStatus = "Pago" | "A pagar"
///   SolicitacaoSeguro.PagamentoStatus = texto do status
/// </summary>
public static class PagamentosService
{
    public const string Pendente = "Pendente";
    public const string Aprovado = "Aprovado";
    public const string Recusado = "Recusado";
    public const string Cancelado = "Cancelado";
    public const string Reembolsado = "Reembolsado";
    public static readonly string[] Todos = [Pendente, Aprovado, Recusado, Cancelado, Reembolsado];

    public const string OrigemAgendamento = "agendamento";
    public const string OrigemPedido = "pedido";
    public const string OrigemSeguro = "seguro";

    public static string Normalizar(string? valor) => Normalizador.Texto(valor) switch
    {
        "pendente" or "a pagar" or "aguardando" or "" => Pendente,
        "aprovado" or "pago" or "confirmado" => Aprovado,
        "recusado" or "negado" => Recusado,
        "cancelado" or "cancelada" => Cancelado,
        "reembolsado" or "estornado" => Reembolsado,
        _ => ""
    };

    /// <summary>Transicoes que a equipe pode fazer. Cancelado e Reembolsado sao finais.</summary>
    public static bool PodeIr(string de, string para) => (de, para) switch
    {
        (Pendente, Aprovado or Recusado or Cancelado) => true,
        (Recusado, Pendente or Aprovado or Cancelado) => true,
        (Aprovado, Pendente) => true,      // desfazer uma aprovacao marcada por engano
        (Aprovado, Reembolsado) => true,
        _ => false
    };

    /// <summary>
    /// Troca o status (sem SaveChanges). Espelha o campo antigo da origem.
    /// </summary>
    public static async Task AlterarStatusAsync(LanePetsDbContext db, Pagamento pag, string? novoStatus, string autor, string? observacao)
    {
        var novo = Normalizar(novoStatus);
        if (novo.Length == 0 || string.IsNullOrWhiteSpace(novoStatus))
            throw new Exception($"Status inválido. Use: {string.Join(", ", Todos)}.");
        if (novo == pag.Status) throw new Exception($"O pagamento já está {pag.Status}.");
        if (!PodeIr(pag.Status, novo))
            throw new Exception(pag.Status is Cancelado or Reembolsado
                ? $"Pagamento {pag.Status.ToLowerInvariant()} não muda mais de status."
                : $"Não é possível passar de {pag.Status} para {novo}.");
        if (pag.ReembolsoPendente && novo != Reembolsado)
            throw new Exception("A origem deste pagamento foi cancelada depois de pago. Marque como Reembolsado ou deixe como está.");

        pag.Status = novo;
        if (novo == Reembolsado) pag.ReembolsoPendente = false;
        pag.AtualizadoEm = DateTime.UtcNow;
        pag.AtualizadoPor = autor;
        var obs = (observacao ?? "").Trim();
        if (obs.Length > 300) throw new Exception("A observação pode ter no máximo 300 caracteres.");
        if (obs.Length > 0) pag.Observacao = obs;
        await EspelharAsync(db, pag);
    }

    /// <summary>Grava o status no campo antigo da origem (compatibilidade).</summary>
    private static async Task EspelharAsync(LanePetsDbContext db, Pagamento pag)
    {
        if (pag.Origem == OrigemAgendamento)
        {
            var a = await db.Agendamentos.FirstOrDefaultAsync(x => x.Id == pag.OrigemId);
            if (a is not null) a.PagamentoStatus = pag.Status == Aprovado ? "Pago" : "A pagar";
        }
        else if (pag.Origem == OrigemSeguro)
        {
            var s = await db.SolicitacoesSeguro.FirstOrDefaultAsync(x => x.Id == pag.OrigemId);
            if (s is not null) s.PagamentoStatus = pag.Status == Aprovado ? "Pago" : pag.Status;
        }
    }

    // -----------------------------------------------------------------------
    // RECONCILIACAO
    // -----------------------------------------------------------------------

    private record Fonte(string Origem, string OrigemId, string ClienteId, string Cliente, string Descricao, decimal Valor,
        string Forma, string Unidade, string DataReferencia, bool Cancelada, string? StatusAntigo);

    /// <summary>
    /// Deixa a tabela Pagamentos coerente com as origens e salva. Idempotente:
    /// roda na subida e depois de toda gravacao que mexe em agendamento,
    /// pedido ou seguro. Devolve quantos pagamentos criou/alterou.
    /// </summary>
    public static async Task<int> ReconciliarAsync(LanePetsDbContext db)
    {
        try { return await ReconciliarUmaVezAsync(db); }
        catch (DbUpdateException)
        {
            // Duas requisicoes ao mesmo tempo podem tentar criar o mesmo
            // pagamento (indice unico Origem+OrigemId). Descarta o que ficou
            // pendente e refaz uma vez, agora vendo o que a outra gravou.
            foreach (var e in db.ChangeTracker.Entries<Pagamento>().ToList()) e.State = EntityState.Detached;
            return await ReconciliarUmaVezAsync(db);
        }
    }

    private static async Task<int> ReconciliarUmaVezAsync(LanePetsDbContext db)
    {
        var fontes = new List<Fonte>();
        var clientes = await db.Clientes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nome);
        string NomeCliente(string id, string reserva) => id.Length > 0 && clientes.TryGetValue(id, out var n) ? n : reserva;

        foreach (var a in await db.Agendamentos.AsNoTracking().ToListAsync())
            fontes.Add(new(OrigemAgendamento, a.Id, a.ClienteId ?? "", NomeCliente(a.ClienteId ?? "", a.Dono),
                $"{ServicosDe(a.ServicosJson)} · {a.Pet}", a.Total, a.FormaPagamento, Normalizador.IdUnidade(a.Unidade),
                a.DataHora, StatusAgendamento.EhCancelado(a.Status), a.PagamentoStatus));

        foreach (var p in await db.Pedidos.AsNoTracking().ToListAsync())
            fontes.Add(new(OrigemPedido, p.Id, p.ClienteId, NomeCliente(p.ClienteId, "(cliente removido)"),
                $"{p.Quantidade}× {p.ProdutoNome}", p.Total, p.FormaPagamento, Normalizador.IdUnidade(p.Unidade),
                p.CriadoEm.ToString("yyyy-MM-ddTHH:mm:ss"), PedidosService.Exibir(p.Status) == PedidosService.Cancelado,
                // Pedido nao tinha status de pagamento: entregue = pago na retirada.
                PedidosService.Exibir(p.Status) == PedidosService.Entregue ? "Pago" : null));

        // Seguro: so contratacao (tem valor). Pedido de contato do site publico nao e cobranca.
        foreach (var s in await db.SolicitacoesSeguro.AsNoTracking().Where(s => s.ClienteId != "").ToListAsync())
            if (s.Valor > 0)
                fontes.Add(new(OrigemSeguro, s.Id, s.ClienteId, NomeCliente(s.ClienteId, s.NomeCliente),
                    $"Seguro {s.NomePlano} · {s.NomePet}", s.Valor, s.MetodoPagamento, "",
                    s.CriadoEm.ToString("yyyy-MM-ddTHH:mm:ss"), string.Equals(s.Status, "Cancelada", StringComparison.OrdinalIgnoreCase), s.PagamentoStatus));

        var existentes = (await db.Pagamentos.ToListAsync()).GroupBy(p => (p.Origem, p.OrigemId)).ToDictionary(g => g.Key, g => g.First());
        var alterados = 0;
        var agora = DateTime.UtcNow;

        foreach (var f in fontes)
        {
            if (!existentes.TryGetValue((f.Origem, f.OrigemId), out var pag))
            {
                // Primeiro pagamento desta origem: status tirado do campo antigo.
                var inicial = Normalizar(f.StatusAntigo) is { Length: > 0 } st ? st : Pendente;
                if (inicial is Cancelado && !f.Cancelada) inicial = Pendente;
                pag = new Pagamento
                {
                    Id = "PAG-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                    Origem = f.Origem, OrigemId = f.OrigemId, Status = inicial,
                    CriadoEm = agora, AtualizadoEm = agora, AtualizadoPor = "sistema"
                };
                db.Pagamentos.Add(pag);
                existentes[(f.Origem, f.OrigemId)] = pag;
                alterados++;
            }
            alterados += Acompanhar(pag, f, agora);
        }

        // Origem apagada (ex.: agendamento excluido no painel): trata como cancelada.
        var chaves = fontes.Select(f => (f.Origem, f.OrigemId)).ToHashSet();
        foreach (var pag in existentes.Values.Where(p => !chaves.Contains((p.Origem, p.OrigemId))))
            alterados += AplicarCancelamento(pag, agora, "Origem excluída");

        if (alterados > 0) await db.SaveChangesAsync();
        return alterados;
    }

    /// <summary>Copia os dados da origem e aplica cancelamento / marcacao antiga.</summary>
    private static int Acompanhar(Pagamento pag, Fonte f, DateTime agora)
    {
        var mudou = false;
        void Set<T>(T atual, T novo, Action<T> gravar) { if (!EqualityComparer<T>.Default.Equals(atual, novo)) { gravar(novo); mudou = true; } }

        Set(pag.ClienteId, f.ClienteId, v => pag.ClienteId = v);
        Set(pag.Cliente, f.Cliente, v => pag.Cliente = v);
        Set(pag.Descricao, f.Descricao, v => pag.Descricao = v);
        Set(pag.Unidade, f.Unidade, v => pag.Unidade = v);
        Set(pag.DataReferencia, f.DataReferencia, v => pag.DataReferencia = v);
        // Valor e forma so acompanham a origem enquanto nada foi pago.
        if (pag.Status is Pendente or Recusado)
        {
            Set(pag.Valor, f.Valor, v => pag.Valor = v);
            Set(pag.Forma, f.Forma ?? "", v => pag.Forma = v);
        }

        var n = 0;
        if (f.Cancelada) n += AplicarCancelamento(pag, agora, "Origem cancelada");
        else if (f.Origem == OrigemAgendamento)
        {
            // "Pago / A pagar" do painel de Agendamentos continua valendo.
            var antigo = Normalizar(f.StatusAntigo);
            if (antigo == Aprovado && pag.Status is Pendente or Recusado) { pag.Status = Aprovado; n++; }
            else if (antigo == Pendente && pag.Status == Aprovado && !pag.ReembolsoPendente) { pag.Status = Pendente; n++; }
        }
        if (mudou || n > 0) { pag.AtualizadoEm = agora; return 1; }
        return 0;
    }

    private static int AplicarCancelamento(Pagamento pag, DateTime agora, string motivo)
    {
        if (pag.Status is Pendente or Recusado)
        {
            pag.Status = Cancelado; pag.AtualizadoEm = agora; pag.AtualizadoPor = "sistema";
            pag.Observacao = motivo;
            return 1;
        }
        if (pag.Status == Aprovado && !pag.ReembolsoPendente)
        {
            pag.ReembolsoPendente = true; pag.AtualizadoEm = agora; pag.AtualizadoPor = "sistema";
            pag.Observacao = motivo + " depois do pagamento: decidir o reembolso.";
            return 1;
        }
        return 0;
    }

    private static string ServicosDe(string json)
    {
        try
        {
            var nomes = JsonSerializer.Deserialize<List<JsonElement>>(json ?? "[]")?
                .Select(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("nome", out var n) ? n.GetString() : null)
                .Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? [];
            return nomes.Count > 0 ? string.Join(" + ", nomes) : "Serviço";
        }
        catch { return "Serviço"; }
    }
}
