using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// 27/09: leitura da tela Agendamentos (agendamentos.html) sem o /api/admin/estado. A tela deixou
/// o LaneStore: pede ao servidor so a aba (unidade) aberta, ja filtrada, ordenada e cortada — a
/// lista em paginas de 12, o calendario pela janela visivel (dia / semana / mes). Indicadores,
/// contagem das abas, servicos do filtro e responsaveis vem calculados aqui, sobre a base inteira.
/// A GRAVACAO continua pelo POST /api/admin/sync/agendamentos (SyncPainelService, mesmas regras).
///
/// Regras de antes, mesmo resultado: funcionario so a unidade dele (§6.19), status pelos nomes
/// novos (StatusAgendamento), unidade comparada pelo id, "Hoje" sem cancelados.
/// </summary>
public class AgendaPainelService(LanePetsDbContext db, PermissaoService permissoes)
{
    public const string SemUnidade = "sem-unidade";
    public const string SemResponsavel = "__sem";
    public const int PorPagina = 12;

    public static string UnidadeDe(Agendamento a) => Normalizador.IdUnidade(a.Unidade) is { Length: > 0 } id ? id : SemUnidade;
    private static string Pagamento(Agendamento a) => string.IsNullOrWhiteSpace(a.PagamentoStatus) ? "A pagar" : a.PagamentoStatus;
    public static string ServicosResumo(Agendamento a) => FormatoPainel.NomesServicos(a.ServicosJson) is { Count: > 0 } n ? string.Join(" + ", n) : "Serviço não informado";

    private static DateTime? Quando(string? dataHora) => DateTime.TryParse(dataHora, out var d) ? d : null;

    public sealed record Filtro(string? Unidade, string? Busca, string? Servico, string? Status, string? Pagamento,
        string? Responsavel, string? De, string? Ate, string? JanelaDe, string? JanelaAte, string? Ordem, string? Direcao, bool Fotos);

    private async Task<List<Agendamento>> VisiveisAsync(ContextoAdmin ctx)
        => (await db.Agendamentos.AsNoTracking().ToListAsync()).Where(a => ctx.VeUnidade(a.Unidade)).ToList();

    public async Task<object> ListarAsync(ContextoAdmin ctx, Filtro f, int limite, int offset)
    {
        var todos = await VisiveisAsync(ctx);
        var unidade = Normalizador.Texto(f.Unidade) == SemUnidade ? SemUnidade
            : Normalizador.IdUnidade(f.Unidade) is { Length: > 0 } id ? id : "\u0000";
        var daUnidade = todos.Where(a => UnidadeDe(a) == unidade).ToList();
        var nomes = await ResponsaveisAgendamento.NomesAsync(db);

        // ---- base inteira (sem filtro): abas, indicadores e opcoes dos filtros
        var hoje = DateTime.Now.ToString("yyyy-MM-dd");
        var status = StatusAgendamento.Todos.ToDictionary(s => s, s => daUnidade.Count(a => StatusAgendamento.Exibir(a.Status) == s));
        var kpis = new Dictionary<string, int>(status)
        {
            ["hoje"] = daUnidade.Count(a => Normalizador.Data(a.DataHora) == hoje && StatusAgendamento.Exibir(a.Status) != StatusAgendamento.Cancelado)
        };
        var unidades = todos.GroupBy(UnidadeDe).ToDictionary(g => g.Key, g => new
        {
            total = g.Count(),
            ativos = g.Count(a => StatusAgendamento.Exibir(a.Status) != StatusAgendamento.Cancelado)
        });
        var servicosUsados = todos.SelectMany(a => FormatoPainel.NomesServicos(a.ServicosJson)).Distinct()
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        var responsaveis = daUnidade.Where(a => a.ResponsavelId.Length > 0).Select(a => a.ResponsavelId).Distinct()
            .Select(r => new { id = r, nome = nomes.TryGetValue(r, out var n) ? n : "Funcionário removido" }).ToList();

        // ---- filtros da tela
        var st = string.IsNullOrWhiteSpace(f.Status) ? "" : StatusAgendamento.Exibir(f.Status);
        var resp = (f.Responsavel ?? "").Trim();
        bool NoPeriodo(Agendamento a, string? de, string? ate)
            => (string.IsNullOrEmpty(de) && string.IsNullOrEmpty(ate)) || Normalizador.Dentro(Normalizador.Data(a.DataHora), de, ate);
        var filtrados = daUnidade.Where(a =>
            ListaPaginada.Combina(f.Busca, a.Pet, a.Dono, a.Telefone, ServicosResumo(a), a.Obs) &&
            (string.IsNullOrWhiteSpace(f.Servico) || FormatoPainel.NomesServicos(a.ServicosJson).Contains(f.Servico)) &&
            (st.Length == 0 || StatusAgendamento.Exibir(a.Status) == st) &&
            (string.IsNullOrWhiteSpace(f.Pagamento) || Pagamento(a) == f.Pagamento) &&
            (resp.Length == 0 || (resp == SemResponsavel ? a.ResponsavelId.Length == 0 : a.ResponsavelId == resp)) &&
            NoPeriodo(a, f.De, f.Ate) && NoPeriodo(a, f.JanelaDe, f.JanelaAte)).ToList();

        // ---- ordem (mesma da tela): data sem valor vai para o fim; empate desfeito pelo id (pagina estavel)
        var desc = Normalizador.Texto(f.Direcao) == "desc";
        IOrderedEnumerable<Agendamento> ordenada = Normalizador.Texto(f.Ordem) switch
        {
            "cliente" => Ordenar(filtrados, a => (a.Dono ?? "").ToLowerInvariant(), desc),
            "pet" => Ordenar(filtrados, a => (a.Pet ?? "").ToLowerInvariant(), desc),
            "servico" => Ordenar(filtrados, a => ServicosResumo(a).ToLowerInvariant(), desc),
            "status" => Ordenar(filtrados, a => (a.Status ?? "").ToLowerInvariant(), desc),
            "total" => desc ? filtrados.OrderByDescending(a => a.Total) : filtrados.OrderBy(a => a.Total),
            _ => desc ? filtrados.OrderByDescending(a => Quando(a.DataHora) ?? DateTime.MaxValue)
                      : filtrados.OrderBy(a => Quando(a.DataHora) ?? DateTime.MaxValue)
        };
        var pagina = ListaPaginada.Cortar(ordenada.ThenBy(a => a.Id, StringComparer.Ordinal).ToList(), limite, offset);

        // ---- o que a linha mostra do pet e do cliente (antes a tela cruzava com as listas inteiras)
        var extras = await ExtrasAsync(ctx, pagina.Itens, f.Fotos);
        return new
        {
            total = pagina.Total,
            offset = pagina.Offset,
            limite = pagina.Limite,
            temMais = pagina.TemMais,
            itens = pagina.Itens.Select(a => new { agendamento = FormatoPainel.Agendamento(a, nomes), extra = extras[a.Id] }),
            totalUnidade = daUnidade.Count,
            kpis,
            unidades,
            servicosUsados,
            responsaveis
        };
    }

    private static IOrderedEnumerable<Agendamento> Ordenar(IEnumerable<Agendamento> lista, Func<Agendamento, string> chave, bool desc)
        => desc ? lista.OrderByDescending(chave, StringComparer.Ordinal) : lista.OrderBy(chave, StringComparer.Ordinal);

    public sealed record Extra(string PetTipo, string PetRaca, string PetFotoUrl, string ClienteTelefone);

    /// <summary>Pet e cliente do agendamento: pelo id e, em registro antigo, por pet + dono (como a tela fazia).</summary>
    private async Task<Dictionary<string, Extra>> ExtrasAsync(ContextoAdmin ctx, List<Agendamento> itens, bool fotos)
    {
        var pets = ctx.Pode(ModulosAdmin.Pets, AcaoPermissao.Visualizar) ? await db.Pets.AsNoTracking().ToListAsync() : new();
        var clientes = ctx.Pode(ModulosAdmin.Clientes, AcaoPermissao.Visualizar) ? await db.Clientes.AsNoTracking().ToListAsync() : new();
        var resultado = new Dictionary<string, Extra>();
        foreach (var a in itens)
        {
            var pet = PetDe(a, pets);
            var cliente = ClienteDe(a, clientes);
            resultado[a.Id] = new Extra(pet?.Tipo ?? "", pet?.Raca ?? "", fotos ? pet?.FotoUrl ?? "" : "", cliente?.Telefone ?? "");
        }
        return resultado;
    }

    private static Pet? PetDe(Agendamento a, List<Pet> pets)
        => (a.PetId.Length > 0 ? pets.FirstOrDefault(p => p.Id == a.PetId) : null)
           ?? pets.FirstOrDefault(p => string.Equals(p.PetNome, a.Pet, StringComparison.OrdinalIgnoreCase)
                                       && string.Equals(p.Dono, a.Dono, StringComparison.OrdinalIgnoreCase));

    private static Cliente? ClienteDe(Agendamento a, List<Cliente> clientes)
        => (a.ClienteId.Length > 0 ? clientes.FirstOrDefault(c => c.Id == a.ClienteId) : null)
           ?? clientes.FirstOrDefault(c => string.Equals(c.Nome, a.Dono, StringComparison.OrdinalIgnoreCase));

    /// <summary>Um agendamento com a ficha do pet (formato do sync, para a baixa do pacote) e o cliente.</summary>
    public async Task<object> DetalheAsync(ContextoAdmin ctx, string id)
    {
        var a = await db.Agendamentos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) ?? throw new Exception("Agendamento não encontrado.");
        if (!ctx.VeUnidade(a.Unidade)) throw new AcessoNegadoException("Este agendamento é de outra unidade.");
        Pet? pet = null;
        if (ctx.Pode(ModulosAdmin.Pets, AcaoPermissao.Visualizar))
        {
            var visiveis = await permissoes.PetsVisiveisAsync(ctx);
            pet = PetDe(a, await db.Pets.AsNoTracking().ToListAsync());
            if (pet is not null && visiveis is not null && !visiveis.Contains(pet.Id)) pet = null;
        }
        var cliente = ctx.Pode(ModulosAdmin.Clientes, AcaoPermissao.Visualizar) ? ClienteDe(a, await db.Clientes.AsNoTracking().ToListAsync()) : null;
        string email = "";
        if (cliente is not null)
            email = (await db.UsuariosClientes.AsNoTracking().Where(u => u.ClienteId == cliente.Id).OrderBy(u => u.CriadoEm).FirstOrDefaultAsync())?.Email ?? "";

        return new
        {
            agendamento = FormatoPainel.Agendamento(a, await ResponsaveisAgendamento.NomesAsync(db)),
            pet = pet is null ? null : FormatoPainel.Pet(pet),
            cliente = cliente is null ? null : new { id = cliente.Id, nome = cliente.Nome, telefone = cliente.Telefone, endereco = cliente.Endereco, email }
        };
    }

    /// <summary>
    /// Horarios lotados ("HH:mm") da unidade no dia: tantos agendamentos nao cancelados quanto a
    /// capacidade. Conta TODAS as unidades do banco pelo id — capacidade e fisica, nao depende de
    /// quem esta olhando. Mesma regra do GET /api/cliente/horarios e da gravacao (ValidarCapacidadeAsync).
    /// </summary>
    public async Task<List<string>> LotadosAsync(string? unidade, string? data)
    {
        var id = Normalizador.IdUnidade(unidade);
        var dia = Normalizador.Data(data);
        if (id.Length == 0 || dia.Length != 10) return [];
        var capacidade = UnidadesRegras.CapacidadeDe(await UnidadesRegras.AcharAsync(db, id));
        var candidatos = await db.Agendamentos.AsNoTracking().Where(a => a.DataHora.StartsWith(dia))
            .Select(a => new { a.Unidade, a.DataHora, a.Status }).ToListAsync();
        return candidatos
            .Where(a => Normalizador.IdUnidade(a.Unidade) == id && StatusAgendamento.Exibir(a.Status) != StatusAgendamento.Cancelado)
            .Select(a => UnidadesRegras.ChaveHorario(a.DataHora))
            .Where(c => c.Length >= 16).Select(c => c[11..16])
            .GroupBy(h => h).Where(g => g.Count() >= capacidade).Select(g => g.Key).OrderBy(h => h, StringComparer.Ordinal).ToList();
    }
}
