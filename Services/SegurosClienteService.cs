using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Item 11.4c (26/09): Seguro Pet pela AREA DO CLIENTE ("Meu Seguro"): consultar, contratar
/// e cancelar. Estava no ClientPortalController; veio para ca sem mudar regra nem mensagem.
///
/// Regras que valem aqui:
///   - o cliente vem da sessao; o pet precisa ser comprovadamente desta conta;
///   - o valor vem do catalogo (congelado na contratacao), nunca do corpo da requisicao;
///   - nao ha gateway: o pagamento nasce Pendente para a equipe confirmar (item 8);
///   - do cartao chegam no maximo os 4 ultimos digitos; numero completo e CVV nao existem aqui;
///   - cancelar nao apaga: Status "Cancelada" + DataCancelamento.
/// Evento no Log e aviso em tempo real ficam no controller.
/// </summary>
public static class SegurosClienteService
{
    /// <summary>Formas de pagamento aceitas na contratacao.</summary>
    public static readonly string[] FormasAceitas = ["PIX", "Cartão de crédito", "Cartão de débito"];

    private static string Digitos(string? valor) => new((valor ?? "").Where(char.IsDigit).ToArray());

    /// <summary>Aceita apenas as formas de pagamento que o sistema conhece.</summary>
    public static string MetodoDePagamento(string? valor)
    {
        var escolhido = (valor ?? "").Trim();
        var achado = FormasAceitas.FirstOrDefault(a => string.Equals(a, escolhido, StringComparison.OrdinalIgnoreCase));
        return achado ?? throw new Exception("Escolha uma forma de pagamento para continuar.");
    }

    /// <summary>
    /// Seguros da propria conta, com o plano do catalogo ao lado (null se o plano sumiu).
    ///
    /// O filtro e por ClienteId. Contratos criados pelo formulario publico antes desse campo
    /// existir ficaram sem ClienteId; sao recuperados pelo par nome+telefone e ADOTADOS pela
    /// conta na primeira consulta, para nao sumirem da tela.
    /// </summary>
    public static async Task<List<(SolicitacaoSeguro Seguro, PlanoSeguro? Plano)>> MeusAsync(LanePetsDbContext db, Cliente cliente)
    {
        var telefone = Digitos(cliente.Telefone);
        var orfaos = await db.SolicitacoesSeguro
            .Where(s => s.ClienteId == "" && s.NomeCliente == cliente.Nome)
            .ToListAsync();
        var adotados = orfaos.Where(s => telefone.Length >= 8 && Digitos(s.Telefone) == telefone).ToList();
        if (adotados.Count > 0)
        {
            foreach (var s in adotados) s.ClienteId = cliente.Id;
            await db.SaveChangesAsync();
        }

        var planos = await db.PlanosSeguro.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p);
        var meus = await db.SolicitacoesSeguro.AsNoTracking()
            .Where(s => s.ClienteId == cliente.Id)
            .OrderByDescending(s => s.CriadoEm)
            .ToListAsync();
        return meus.Select(s => (s, planos.TryGetValue(s.PlanoSeguroId, out var p) ? p : null)).ToList();
    }

    /// <summary>Contrato que ainda esta valendo pode ser cancelado; cancelado nao cancela de novo.</summary>
    public static bool PodeCancelar(SolicitacaoSeguro s) => !string.Equals(s.Status, "Cancelada", StringComparison.OrdinalIgnoreCase);

    /// <summary>Fecha a contratacao de um plano para um pet da propria conta (depois das etapas da tela).</summary>
    public static async Task<SolicitacaoSeguro> ContratarAsync(LanePetsDbContext db, Cliente cliente,
        string? planoId, string? petId, string? metodoPagamento, string? cartaoFinalInformado, string? observacao)
    {
        var plano = await db.PlanosSeguro.FirstOrDefaultAsync(p => p.Id == planoId && p.Ativo)
                    ?? throw new Exception("Plano indisponivel.");

        if (string.IsNullOrWhiteSpace(petId)) throw new Exception("Selecione um pet para continuar.");
        // O pet tem de ser comprovadamente desta conta. Nao existe atalho
        // "pega o primeiro pet": seguro no pet errado e um erro silencioso.
        var pet = await db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == petId && p.ClienteId == cliente.Id)
                  ?? throw new Exception("Pet nao localizado na sua conta.");

        var metodo = MetodoDePagamento(metodoPagamento);
        var cartaoFinal = "";
        if (metodo.StartsWith("Cartao", StringComparison.OrdinalIgnoreCase) || metodo.StartsWith("Cartão", StringComparison.OrdinalIgnoreCase))
        {
            cartaoFinal = new string(Digitos(cartaoFinalInformado).TakeLast(4).ToArray());
            if (cartaoFinal.Length != 4) throw new Exception("Confira os dados do cartao para concluir a contratacao.");
        }

        var jaTem = await db.SolicitacoesSeguro.AnyAsync(s =>
            s.ClienteId == cliente.Id && s.PetId == pet.Id && s.PlanoSeguroId == plano.Id
            && (s.Status == "Pendente" || s.Status == "Em contato"));
        if (jaTem) throw new Exception($"Ja existe um pedido em andamento do plano {plano.Nome} para {pet.PetNome}.");

        var solicitacao = new SolicitacaoSeguro
        {
            Id = "SOL-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            PlanoSeguroId = plano.Id,
            NomePlano = plano.Nome,
            ClienteId = cliente.Id,
            PetId = pet.Id,
            NomeCliente = cliente.Nome,
            Telefone = cliente.Telefone,
            NomePet = pet.PetNome,
            Observacao = (observacao ?? "").Trim(),
            Status = "Pendente",
            // Valor congelado no momento da contratacao.
            Valor = plano.ValorMensal,
            MetodoPagamento = metodo,
            PagamentoStatus = "Pendente",
            CartaoFinal = cartaoFinal
        };
        db.SolicitacoesSeguro.Add(solicitacao);
        await db.SaveChangesAsync();
        await PagamentosService.ReconciliarAsync(db);   // item 8: um pagamento por contratacao
        return solicitacao;
    }

    /// <summary>
    /// Cancela um seguro da PROPRIA conta. Pagamento que nunca foi confirmado vira Cancelado;
    /// pagamento ja confirmado nao e tocado aqui (vira "reembolso pendente" no item 8).
    /// </summary>
    public static async Task<SolicitacaoSeguro> CancelarAsync(LanePetsDbContext db, string clienteId, string id)
    {
        var seguro = await db.SolicitacoesSeguro.FirstOrDefaultAsync(s => s.Id == id && s.ClienteId == clienteId)
                     ?? throw new Exception("Seguro nao localizado na sua conta.");
        if (!PodeCancelar(seguro)) throw new Exception("Este seguro ja esta cancelado.");

        seguro.Status = "Cancelada";
        seguro.DataCancelamento = DateTime.UtcNow;
        if (!string.Equals(seguro.PagamentoStatus, "Pago", StringComparison.OrdinalIgnoreCase))
            seguro.PagamentoStatus = "Cancelado";
        await db.SaveChangesAsync();
        await PagamentosService.ReconciliarAsync(db);
        return seguro;
    }
}
