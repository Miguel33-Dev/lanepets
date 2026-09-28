using System.Globalization;
using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// 27/09: leitura da tela Clientes (clientes.html) sem o /api/admin/estado. A tela deixou o
/// LaneStore: a lista de clientes e a de pets vem paginadas e filtradas daqui, o detalhe de um
/// cliente vem sozinho e a GRAVACAO continua pelo POST /api/admin/sync/{clientes|pets} (mesmas
/// regras, permissoes e eventos de sempre, SyncPainelService).
///
/// Regras de visibilidade iguais as do estado (§6.19): contagem de pets / agendamentos /
/// pedidos so do que o usuario pode ver (modulo + unidade do Funcionario).
/// Service scoped (§6.34): estado da requisicao = contexto do administrador.
/// </summary>
public class ClientesPainelService(LanePetsDbContext db, PermissaoService permissoes, IConfiguration config)
{
    public const int PorPaginaClientes = 12;

    private static readonly StringComparer Nomes = StringComparer.Create(new CultureInfo("pt-BR"), ignoreCase: true);

    public static string StatusDe(Cliente c) => Normalizador.Texto(c.Status) == "inativo" ? "inativo" : "ativo";
    public static string OrigemDe(Cliente c) => Normalizador.Texto(c.Origem) == "portal_cliente" ? "portal_cliente" : "cadastro_painel";

    /// <summary>Tipo do pet por grupo: o site grava "Cachorro", o painel "Cão".</summary>
    public static string GrupoTipo(string? tipo) => Normalizador.Texto(tipo) switch
    {
        "cao" or "cachorro" or "cadela" or "dog" or "canino" => "Cão",
        "gato" or "gata" or "felino" or "cat" => "Gato",
        "" => "",
        _ => "Outro"
    };

    // ------------------------------------------------------------------ visibilidade

    private sealed record Visiveis(List<Pet> Pets, List<Agendamento> Agendamentos, List<Pedido> Pedidos);

    private async Task<Visiveis> VisiveisAsync(ContextoAdmin ctx)
    {
        bool Ver(string modulo) => ctx.Pode(modulo, AcaoPermissao.Visualizar);
        var pets = Ver(ModulosAdmin.Pets) ? await db.Pets.AsNoTracking().ToListAsync() : new();
        var petsVisiveis = await permissoes.PetsVisiveisAsync(ctx);
        if (petsVisiveis is not null) pets = pets.Where(p => petsVisiveis.Contains(p.Id)).ToList();
        var ags = Ver(ModulosAdmin.Agendamentos)
            ? (await db.Agendamentos.AsNoTracking().ToListAsync()).Where(a => ctx.VeUnidade(a.Unidade)).ToList() : new();
        var pedidos = Ver(ModulosAdmin.Pedidos)
            ? (await db.Pedidos.AsNoTracking().ToListAsync()).Where(p => ctx.VeUnidade(p.Unidade)).ToList() : new();
        return new Visiveis(pets, ags, pedidos);
    }

    private async Task<Dictionary<string, UsuarioCliente>> ContasAsync()
        => (await db.UsuariosClientes.AsNoTracking().ToListAsync())
            .GroupBy(u => u.ClienteId).ToDictionary(g => g.Key, g => g.OrderBy(u => u.CriadoEm).First());

    private static Dictionary<string, int> Contar<T>(IEnumerable<T> itens, Func<T, string?> cliente)
        => itens.GroupBy(i => cliente(i) ?? "").ToDictionary(g => g.Key, g => g.Count());

    // ------------------------------------------------------------------ clientes

    public sealed record FiltroClientes(string? Busca, string? Status, string? Origem);

    public async Task<object> ListarAsync(ContextoAdmin ctx, FiltroClientes filtro, int limite, int offset)
    {
        var clientes = await db.Clientes.AsNoTracking().ToListAsync();
        var contas = await ContasAsync();
        var vis = await VisiveisAsync(ctx);
        var qp = Contar(vis.Pets, p => p.ClienteId);
        var qa = Contar(vis.Agendamentos, a => a.ClienteId);
        var qd = Contar(vis.Pedidos, p => p.ClienteId);

        var status = Normalizador.Texto(filtro.Status);
        var origem = Normalizador.Texto(filtro.Origem);
        var filtrados = clientes.Where(c =>
                (status is not ("ativo" or "inativo") || StatusDe(c) == status) &&
                (origem is not ("portal_cliente" or "cadastro_painel") || OrigemDe(c) == origem) &&
                ListaPaginada.Combina(filtro.Busca, c.Nome, contas.GetValueOrDefault(c.Id)?.Email, c.Telefone, c.Endereco))
            // ThenBy(Id): ordem estavel entre paginas.
            .OrderBy(c => c.Nome, Nomes).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();

        var pagina = ListaPaginada.Cortar(filtrados, limite, offset);
        // Mesma conta que a tela fazia: "novo este mes" = conta do site criada no mes corrente.
        var agora = DateTime.Now;
        var ativos = clientes.Count(c => StatusDe(c) == "ativo");
        return new
        {
            total = pagina.Total,
            offset = pagina.Offset,
            limite = pagina.Limite,
            temMais = pagina.TemMais,
            itens = pagina.Itens.Select(c => FormatoPainel.Cliente(c, contas.GetValueOrDefault(c.Id),
                qp.GetValueOrDefault(c.Id), qa.GetValueOrDefault(c.Id), qd.GetValueOrDefault(c.Id))),
            // Cartoes de resumo: sempre sobre o total real, nao sobre o filtro (como a tela sempre mostrou).
            resumo = new
            {
                total = clientes.Count,
                ativos,
                inativos = clientes.Count - ativos,
                novosMes = clientes.Count(c => contas.GetValueOrDefault(c.Id) is { } u && u.CriadoEm.Month == agora.Month && u.CriadoEm.Year == agora.Year)
            }
        };
    }

    /// <summary>Um cliente com os pets e os agendamentos mais recentes (o que o modal de detalhe mostra).</summary>
    public async Task<object> DetalheAsync(ContextoAdmin ctx, string id)
    {
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new Exception("Cliente não encontrado.");
        var conta = (await ContasAsync()).GetValueOrDefault(cliente.Id);
        var vis = await VisiveisAsync(ctx);
        var pets = vis.Pets.Where(p => p.ClienteId == id).OrderBy(p => p.PetNome, Nomes).ToList();
        var ags = vis.Agendamentos.Where(a => a.ClienteId == id)
            .OrderByDescending(a => a.DataHora, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
        var qtdPedidos = vis.Pedidos.Count(p => p.ClienteId == id);

        return new
        {
            cliente = FormatoPainel.Cliente(cliente, conta, pets.Count, ags.Count, qtdPedidos),
            pets = pets.Select(FormatoPainel.Pet),
            agendamentos = new
            {
                total = ags.Count,
                itens = ags.Take(8).Select(a => new { id = a.Id, pet = a.Pet, status = StatusAgendamento.Exibir(a.Status), dataHora = a.DataHora })
            },
            // A tela esconde a secao em vez de mostrar "nenhum" quando o usuario nao pode ver.
            podeVerPets = ctx.Pode(ModulosAdmin.Pets, AcaoPermissao.Visualizar),
            podeVerAgendamentos = ctx.Pode(ModulosAdmin.Agendamentos, AcaoPermissao.Visualizar),
            podeVerPedidos = ctx.Pode(ModulosAdmin.Pedidos, AcaoPermissao.Visualizar),
            // 29/09: cartao fidelidade (conta TODOS os concluidos do cliente) e se este usuario pode marcar a entrega.
            fidelidade = await FidelidadeService.CartaoAsync(db, config, cliente.Id),
            podeResgatarFidelidade = ctx.Pode(ModulosAdmin.Clientes, AcaoPermissao.Editar)
        };
    }

    // ------------------------------------------------------------------ pets

    public async Task<object> ListarPetsAsync(ContextoAdmin ctx, string? busca, string? tipo, int limite, int offset)
    {
        var pets = (await VisiveisAsync(ctx)).Pets;
        var grupo = Normalizador.Texto(tipo);
        var filtrados = pets.Where(p =>
                (grupo.Length == 0 || Normalizador.Texto(GrupoTipo(p.Tipo)) == grupo) &&
                ListaPaginada.Combina(busca, p.PetNome, p.Dono, p.Raca, p.Telefone, p.Endereco, p.Tipo))
            .OrderBy(p => p.Dono, Nomes).ThenBy(p => p.PetNome, Nomes).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
        var pagina = ListaPaginada.Cortar(filtrados, limite, offset);
        return new
        {
            total = pagina.Total,
            offset = pagina.Offset,
            limite = pagina.Limite,
            temMais = pagina.TemMais,
            totalGeral = pets.Count,
            itens = pagina.Itens.Select(FormatoPainel.Pet)
        };
    }
}
