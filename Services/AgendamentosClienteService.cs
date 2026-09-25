using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Item 11.4 (26/09): agendamento feito pela AREA DO CLIENTE (minha-conta.html).
/// Estava dentro do ClientPortalController; veio para ca sem mudar regra nem mensagem.
///
/// O cliente sempre chega aqui ja resolvido pela sessao (nunca por id do corpo) e toda
/// consulta de agendamento/pet carrega "ClienteId == cliente.Id" na mesma consulta.
/// Sessao, evento no Log e aviso em tempo real continuam no controller.
/// </summary>
public static class AgendamentosClienteService
{
    /// <summary>Horarios oferecidos na area do cliente (grade fixa; a capacidade da unidade decide o que sobra).</summary>
    public static readonly string[] Grade = ["09:00", "10:00", "11:00", "13:00", "14:00", "15:00", "16:00", "17:00"];

    /// <summary>Taxa de cada perna do transporte (busca e/ou entrega).</summary>
    public const decimal TaxaTransporte = 15m;

    /// <summary>Dados que a tela manda para marcar um atendimento. Nao existe ClienteId aqui.</summary>
    public sealed record NovoAgendamento(string? PetId, string? ServicoId, string? Unidade, string? Data, string? Horario,
        string? Transporte, string? FormaPagamento, string? Observacao);

    /// <summary>
    /// Item 4: agendamentos da conta com status no nome novo e o PRIMEIRO nome
    /// do funcionario responsavel ("Atendimento com Ana"). O Id do responsavel
    /// nao sai (JsonIgnore na entidade).
    /// </summary>
    public static async Task<List<Agendamento>> ListarAsync(LanePetsDbContext db, string clienteId)
    {
        var lista = await db.Agendamentos.AsNoTracking().Where(a => a.ClienteId == clienteId).OrderByDescending(a => a.DataHora).ToListAsync();
        var nomes = lista.Any(a => a.ResponsavelId.Length > 0) ? await ResponsaveisAgendamento.NomesAsync(db) : new Dictionary<string, string>();
        foreach (var a in lista)
        {
            a.Status = StatusAgendamento.Exibir(a.Status);
            a.ResponsavelNome = a.ResponsavelId.Length > 0 && nomes.TryGetValue(a.ResponsavelId, out var n) ? ResponsaveisAgendamento.PrimeiroNome(n) : "";
        }
        return lista;
    }

    /// <summary>
    /// Horarios livres de uma unidade num dia. Item 5: horario so fica indisponivel quando
    /// atinge a CAPACIDADE da unidade, e a unidade e comparada normalizada — agendamento do
    /// painel ("franco") e do cliente ("Franco da Rocha") ocupam o mesmo horario.
    /// </summary>
    public static async Task<string[]> HorariosLivresAsync(LanePetsDbContext db, string? unidade, string? data)
    {
        if (!DateOnly.TryParse(data, out _) || string.IsNullOrWhiteSpace(unidade)) throw new Exception("Informe unidade e data.");
        var registro = await UnidadesRegras.AcharAsync(db, unidade);
        if (registro is null || !registro.Ativa) throw new Exception("Unidade indisponível para agendamento.");
        var capacidade = UnidadesRegras.CapacidadeDe(registro);
        var ocupados = (await db.Agendamentos.AsNoTracking().Where(a => a.DataHora.StartsWith(data!) && a.Status != "Cancelado").Select(a => new { a.Unidade, a.DataHora, a.Status }).ToListAsync())
            .Where(a => Normalizador.Status(a.Status) != "Cancelado" && Normalizador.IdUnidade(a.Unidade) == registro.Id)
            .GroupBy(a => UnidadesRegras.ChaveHorario(a.DataHora).Length >= 16 ? UnidadesRegras.ChaveHorario(a.DataHora)[11..16] : "")
            .ToDictionary(g => g.Key, g => g.Count());
        return Grade.Where(hora => (ocupados.TryGetValue(hora, out var n) ? n : 0) < capacidade).ToArray();
    }

    /// <summary>
    /// Marca o atendimento (nasce Solicitado, pagamento Pendente) e grava. Valida pet da conta,
    /// servico, unidade ativa, servico oferecido na unidade e capacidade do horario.
    /// Devolve o agendamento gravado e o servico (para o texto do evento).
    /// </summary>
    public static async Task<(Agendamento Agendamento, Servico Servico)> CriarAsync(LanePetsDbContext db, Cliente cliente, NovoAgendamento dados)
    {
        var pet = await db.Pets.FirstOrDefaultAsync(p => p.Id == dados.PetId && p.ClienteId == cliente.Id) ?? throw new Exception("Pet não localizado.");
        var servico = await db.Servicos.FindAsync(dados.ServicoId) ?? throw new Exception("Serviço não localizado.");
        if (!DateOnly.TryParse(dados.Data, out _) || !TimeOnly.TryParse(dados.Horario, out _) || string.IsNullOrWhiteSpace(dados.Unidade)) throw new Exception("Escolha unidade, data e horário válidos.");
        var dataHora = dados.Data + "T" + dados.Horario + ":00";

        // Item 5: unidade ativa, servico oferecido nela e capacidade do horario.
        var unidadeReg = await UnidadesRegras.AcharAsync(db, dados.Unidade);
        if (unidadeReg is null || !unidadeReg.Ativa) throw new Exception("Esta unidade não está disponível para agendamento. Escolha outra.");
        if (!UnidadesRegras.OfereceServico(unidadeReg, servico.Id)) throw new Exception($"{servico.Nome} não é oferecido na unidade {unidadeReg.Nome}.");
        if (await UnidadesRegras.OcupadosAsync(db, unidadeReg.Id, dataHora) >= UnidadesRegras.CapacidadeDe(unidadeReg))
            throw new Exception("Este horário acabou de ser ocupado. Escolha outro horário.");

        var total = servico.Preco
            + (dados.Transporte?.Contains("Busca", StringComparison.OrdinalIgnoreCase) == true ? TaxaTransporte : 0m)
            + (dados.Transporte?.Contains("Entrega", StringComparison.OrdinalIgnoreCase) == true ? TaxaTransporte : 0m);

        var agendamento = new Agendamento
        {
            Id = "AGD-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            ClienteId = cliente.Id,
            PetId = pet.Id,
            Pet = pet.PetNome,
            Dono = cliente.Nome,
            Telefone = cliente.Telefone,
            DataHora = dataHora,
            ServicosJson = $"[{{\"id\":\"{servico.Id}\",\"nome\":\"{servico.Nome.Replace("\"", "")}\"}}]",
            Total = total,
            Transporte = dados.Transporte ?? "Cliente leva",
            ValorTransporte = total - servico.Preco,
            Status = StatusAgendamento.Solicitado,
            PagamentoStatus = "Pendente",
            FormaPagamento = dados.FormaPagamento ?? "A combinar",
            Unidade = dados.Unidade!,
            Obs = (dados.Observacao ?? "").Trim()
        };
        db.Agendamentos.Add(agendamento);
        await db.SaveChangesAsync();
        await PagamentosService.ReconciliarAsync(db);
        return (agendamento, servico);
    }

    /// <summary>
    /// Cancela um agendamento da propria conta. O registro NAO e apagado: vira "Cancelado".
    /// Item 4: o cliente cancela enquanto o atendimento nao comecou (Solicitado ou Confirmado).
    /// Item 8: o pagamento pendente vira Cancelado (aprovado fica "reembolso pendente").
    /// </summary>
    public static async Task<Agendamento> CancelarAsync(LanePetsDbContext db, string clienteId, string id)
    {
        var agendamento = await db.Agendamentos.FirstOrDefaultAsync(a => a.Id == id && a.ClienteId == clienteId)
                          ?? throw new Exception("Agendamento nao localizado na sua conta.");
        if (StatusAgendamento.EhCancelado(agendamento.Status))
            throw new Exception("Este agendamento já está cancelado.");
        if (!StatusAgendamento.ClientePodeCancelar(agendamento.Status))
            throw new Exception(StatusAgendamento.EhConcluido(agendamento.Status)
                ? "Atendimentos concluídos não podem ser cancelados. Fale com a equipe LanePets."
                : "Este atendimento já começou e não pode mais ser cancelado por aqui. Fale com a equipe LanePets.");

        agendamento.Status = StatusAgendamento.Cancelado;
        await db.SaveChangesAsync();
        await PagamentosService.ReconciliarAsync(db);
        return agendamento;
    }
}
