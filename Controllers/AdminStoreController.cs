using System.Text.Json;
using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using LanePets.DTOs;
using Microsoft.EntityFrameworkCore;
using static LanePets.Services.SyncPainelService;   // Texto, Itens, Ids, SemTransporte (item 11.5)

namespace LanePets.Controllers;

/// <summary>
/// Estado completo do painel administrativo e gravacao das alteracoes feitas
/// nele — tudo sobre o lanepets.db, o mesmo banco que a area do cliente usa.
///
/// Por que este controller existe: as telas antigas do painel (agendamentos,
/// clientes, produtos, entradas e saidas, pacotes) guardavam tudo no
/// localStorage do navegador. Resultado: o que o cliente criava pela area
/// publica nunca chegava ao admin, e o que o admin cadastrava morria no
/// navegador de quem cadastrou.
///
/// Aqui ficam as duas pontas que faltavam:
///   GET  api/admin/estado          -> devolve TODAS as colecoes ja no formato
///                                     que as telas antigas esperam;
///   POST api/admin/sync/{colecao}  -> recebe apenas o que mudou (criados,
///                                     atualizados, removidos) e grava.
///
/// Nenhum endpoint anterior foi alterado e nenhuma tabela mudou de esquema.
/// </summary>
[Route("api/admin")]
public class AdminStoreController(LanePetsDbContext db, PermissaoService permissoes, RealtimeNotifier realtime, EventosService eventos, SyncPainelService sync, ClientesPainelService clientesPainel, IConfiguration config) : ApiControllerBase
{
    // =======================================================================
    // LEITURA — o painel inteiro em uma chamada
    // =======================================================================

    /// <summary>
    /// Todas as colecoes do painel, no formato das telas antigas. E a unica
    /// fonte de dados do admin: nada mais vem do navegador.
    /// </summary>
    [HttpGet("estado")]
    public async Task<IActionResult> Estado([FromQuery] string token = "", [FromQuery] string? colecoes = null)
    {
        try
        {
            // Este endpoint devolve o painel inteiro de uma vez, entao a
            // permissao e aplicada COLECAO POR COLECAO: o que o usuario nao
            // pode ver simplesmente nao e lido do banco e volta vazio. Assim
            // "esconder no menu" e "nao receber o dado" passam a ser a mesma
            // coisa — nem abrindo o DevTools o dado aparece.
            var contexto = await permissoes.ResolverAsync(token);
            // 27/09: ?colecoes=produtos,pets -> so essas sao lidas do banco (as telas que ainda usam o
            // LaneStore pedem so o que mostram). Sem o parametro, tudo, como antes.
            var pedidas = string.IsNullOrWhiteSpace(colecoes) ? null
                : colecoes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(Normalizador.Texto).ToHashSet();
            bool Quer(string colecao) => pedidas is null || pedidas.Contains(Normalizador.Texto(colecao));
            bool Ver(string modulo) => contexto.Pode(modulo, AcaoPermissao.Visualizar);
            bool Ler(string colecao, string modulo) => Quer(colecao) && Ver(modulo);

            // As contagens por cliente precisam de pets/agendamentos/pedidos: quem pede clientes le os tres.
            var comClientes = Quer("clientes");
            List<Agendamento> agendamentos = (Quer("agendamentos") || comClientes) && Ver(ModulosAdmin.Agendamentos) ? await db.Agendamentos.AsNoTracking().ToListAsync() : new();
            List<Pet> pets = (Quer("pets") || comClientes) && Ver(ModulosAdmin.Pets) ? await db.Pets.AsNoTracking().ToListAsync() : new();

            // Funcionario (item 1): agendamentos e pets de outra unidade nao
            // saem do servidor. Para administradores os dois filtros nao cortam nada.
            agendamentos = agendamentos.Where(a => contexto.VeUnidade(a.Unidade)).ToList();
            var petsVisiveis = await permissoes.PetsVisiveisAsync(contexto);
            if (petsVisiveis is not null) pets = pets.Where(p => petsVisiveis.Contains(p.Id)).ToList();
            List<Cliente> clientes = Ler("clientes", ModulosAdmin.Clientes) ? await db.Clientes.AsNoTracking().ToListAsync() : new();
            List<Servico> servicos = Ler("servicos", ModulosAdmin.Servicos) ? await db.Servicos.AsNoTracking().ToListAsync() : new();
            List<Produto> produtos = Ler("produtos", ModulosAdmin.Produtos) ? await db.Produtos.AsNoTracking().ToListAsync() : new();
            List<EntradaSaida> lancamentos = Ler("entradasESaidas", ModulosAdmin.Pagamentos) ? await db.EntradasESaidas.AsNoTracking().ToListAsync() : new();
            List<Pedido> pedidos = (Quer("pedidos") || comClientes) && Ver(ModulosAdmin.Pedidos) ? await db.Pedidos.AsNoTracking().ToListAsync() : new();
            pedidos = pedidos.Where(p => contexto.VeUnidade(p.Unidade)).ToList();   // item 5: funcionario ve so a unidade dele
            List<UsuarioCliente> usuarios = Ler("clientes", ModulosAdmin.Clientes) ? await db.UsuariosClientes.AsNoTracking().ToListAsync() : new();

            // Totais da tela Cadastro: colecao nao lida vira uma contagem barata (mesmas permissoes).
            async Task<int> Contar<T>(string colecao, string modulo, List<T> lida, IQueryable<T> tabela) where T : class
                => Quer(colecao) ? lida.Count : Ver(modulo) ? await tabela.CountAsync() : 0;
            var totalClientes = await Contar("clientes", ModulosAdmin.Clientes, clientes, db.Clientes);
            var totalProdutos = await Contar("produtos", ModulosAdmin.Produtos, produtos, db.Produtos);
            var totalServicos = await Contar("servicos", ModulosAdmin.Servicos, servicos, db.Servicos);

            // E-mail e data de criacao da conta vivem em UsuariosClientes; o
            // painel precisa deles junto do cliente.
            var contaPorCliente = usuarios
                .GroupBy(u => u.ClienteId)
                .ToDictionary(g => g.Key, g => g.OrderBy(u => u.CriadoEm).First());

            var petsPorCliente = pets.GroupBy(p => p.ClienteId ?? "").ToDictionary(g => g.Key, g => g.Count());
            var agsPorCliente = agendamentos.GroupBy(a => a.ClienteId ?? "").ToDictionary(g => g.Key, g => g.Count());
            var pedidosPorCliente = pedidos.GroupBy(p => p.ClienteId ?? "").ToDictionary(g => g.Key, g => g.Count());
            var nomesResponsaveis = await ResponsaveisAgendamento.NomesAsync(db);   // item 4

            return OkApi(new
            {
                agendamentos = Quer("agendamentos") ? agendamentos.OrderBy(a => a.DataHora).Select(a => ParaPainel(a, nomesResponsaveis)) : null,
                pets = Quer("pets") ? pets.Select(p => ParaPainel(p)) : null,
                servicos = servicos.OrderBy(s => s.Nome).Select(s => ParaPainel(s)),
                produtos = produtos.OrderBy(p => p.Nome).Select(p => ParaPainel(p)),
                entradasESaidas = lancamentos.OrderByDescending(e => e.Data).Select(e => ParaPainel(e)),
                clientes = clientes.OrderBy(c => c.Nome).Select(c => FormatoPainel.Cliente(c, contaPorCliente.GetValueOrDefault(c.Id),
                    petsPorCliente.GetValueOrDefault(c.Id), agsPorCliente.GetValueOrDefault(c.Id), pedidosPorCliente.GetValueOrDefault(c.Id))),
                totais = new
                {
                    clientes = totalClientes,
                    contasDeCliente = usuarios.Count,
                    pets = pets.Count,
                    agendamentos = agendamentos.Count,
                    produtos = totalProdutos,
                    servicos = totalServicos,
                    pedidos = pedidos.Count,
                    lancamentos = lancamentos.Count
                },
                atualizadoEm = DateTime.UtcNow.ToString("O"),
                permissoes = PermissaoService.Mapear(contexto)
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>
    /// Lista de clientes do painel: todo mundo que existe no banco, tenha vindo do cadastro do
    /// site ou do cadastro feito pela equipe. 27/09: paginada e filtrada no servidor (a tela
    /// Clientes saiu do LaneStore) — busca (nome, e-mail, telefone, endereco no padrao LaneBusca),
    /// status (ativo|inativo), origem (portal_cliente|cadastro_painel), limite (padrao 12) e offset.
    /// Os itens tem o mesmo formato do /api/admin/estado, que e o que o sync recebe de volta.
    /// </summary>
    [HttpGet("clientes")]
    public async Task<IActionResult> Clientes([FromQuery] string token = "", [FromQuery] string? busca = null,
        [FromQuery] string? status = null, [FromQuery] string? origem = null,
        [FromQuery] int limite = ClientesPainelService.PorPaginaClientes, [FromQuery] int offset = 0)
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(token, ModulosAdmin.Clientes, AcaoPermissao.Visualizar);
            return OkApi(await clientesPainel.ListarAsync(contexto, new(busca, status, origem), limite, offset));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>Detalhe de um cliente: dados, pets e os 8 agendamentos mais recentes (so o que o usuario pode ver).</summary>
    [HttpGet("clientes/{id}")]
    public async Task<IActionResult> DetalheCliente(string id, [FromQuery] string token = "")
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(token, ModulosAdmin.Clientes, AcaoPermissao.Visualizar);
            return OkApi(await clientesPainel.DetalheAsync(contexto, id));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>
    /// 29/09: a equipe entregou o premio do cartao fidelidade na unidade — gasta um cartao completo.
    /// Exige clientes:editar; sem cartao completo responde 400 (FidelidadeService.SemPremio).
    /// </summary>
    [HttpPost("clientes/{id}/fidelidade/resgatar")]
    public async Task<IActionResult> ResgatarFidelidade(string id, [FromBody] TokenFidelidade corpo)
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(corpo.Token ?? "", ModulosAdmin.Clientes, AcaoPermissao.Editar);
            var cartao = await FidelidadeService.ResgatarAsync(db, config, id, contexto.Usuario);
            var nome = await db.Clientes.AsNoTracking().Where(c => c.Id == id).Select(c => c.Nome).FirstOrDefaultAsync() ?? "";
            await eventos.RegistrarAsync(new("cliente", "Prêmio de fidelidade entregue", "info", "admin", contexto.Usuario.Id, contexto.Usuario.Email, id,
                $"{cartao.Premio} · {nome} · {cartao.PremiosDisponiveis} prêmio(s) ainda disponível(is)"), HttpContext);
            await realtime.NotificarAsync("clientes", "fidelidade", new { id });
            return OkApi(new { fidelidade = cartao, message = $"Prêmio entregue: {cartao.Premio}." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    public record TokenFidelidade(string? Token);

    /// <summary>
    /// Pets do painel (tabela "Pets cadastrados" da tela Clientes), paginados: busca (pet, dono,
    /// raca, telefone, endereco, tipo), tipo por grupo (Cão / Gato / Outro), limite (padrao 50) e
    /// offset. Funcionario ve so os pets da unidade dele (PetsVisiveisAsync).
    /// </summary>
    [HttpGet("pets")]
    public async Task<IActionResult> PetsPainel([FromQuery] string token = "", [FromQuery] string? busca = null, [FromQuery] string? tipo = null,
        [FromQuery] int limite = ListaPaginada.Padrao, [FromQuery] int offset = 0)
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(token, ModulosAdmin.Pets, AcaoPermissao.Visualizar);
            return OkApi(await clientesPainel.ListarPetsAsync(contexto, busca, tipo, limite, offset));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // =======================================================================
    // CONVERSAO BANCO -> FORMATO DAS TELAS
    // As telas antigas usam nomes proprios de campo (pet, dono, dataHora...).
    // Traduzir aqui evita reescrever milhares de linhas de tela e mantem o
    // banco como unica fonte de verdade.
    // =======================================================================

    private static object ParaPainel(Agendamento a, IReadOnlyDictionary<string, string>? responsaveis = null) => FormatoPainel.Agendamento(a, responsaveis);

    private static object ParaPainel(Pet p) => FormatoPainel.Pet(p);

    private static object ParaPainel(Servico s) => FormatoPainel.Servico(s);

    private static object ParaPainel(Produto p) => new
    {
        id = p.Id,
        codigo = p.Codigo,
        nome = p.Nome,
        categoria = p.Categoria,
        valorCompra = p.ValorCompra,
        valorVenda = p.ValorVenda,
        estoque = p.Estoque,
        estoqueMinimo = p.EstoqueMinimo,
        controlaEstoque = p.ControlaEstoque,
        // Itens 6/7
        descricao = p.Descricao,
        fotoUrl = p.FotoUrl,
        visivelLoja = p.VisivelLoja,
        situacaoEstoque = EstoqueService.Situacao(p)
    };

    private static object ParaPainel(EntradaSaida e) => new
    {
        id = e.Id,
        data = e.Data,
        descricao = e.Descricao,
        tipo = e.Tipo,
        valor = e.Valor,
        unidade = UnidadePainel(e.Unidade),
        origem = e.Origem
    };

    /// <summary>
    /// Qualquer forma gravada da unidade ("Franco da Rocha", "franco"...) vira
    /// o id curto das telas. Desde o item 5 a lista de unidades e dinamica
    /// (Normalizador.RegistrarUnidades), entao unidade nova funciona aqui sem mudar codigo.
    /// </summary>
    private static string UnidadePainel(string? valor) => Normalizador.IdUnidade(valor);

    // =======================================================================
    // ESCRITA — somente o que mudou
    // =======================================================================

    public record SyncRequest(string? Token, JsonElement Criados, JsonElement Atualizados, JsonElement Removidos);

    /// <summary>
    /// Aplica no banco as alteracoes feitas em uma tela do painel.
    /// Recebe apenas o delta: registros criados, registros alterados e ids
    /// removidos. Nunca apaga nada que o cliente nao tenha pedido para apagar.
    /// </summary>
    /// <summary>
    /// De qual modulo cada colecao sincronizavel depende. Colecao desconhecida
    /// e recusada aqui, antes de qualquer escrita — nao existe caminho de
    /// gravacao sem modulo correspondente.
    /// </summary>
    private static string ModuloDaColecao(string colecao) => Normalizador.Texto(colecao) switch
    {
        "agendamentos" => ModulosAdmin.Agendamentos,
        "pets" => ModulosAdmin.Pets,
        "clientes" => ModulosAdmin.Clientes,
        "servicos" => ModulosAdmin.Servicos,
        "produtos" => ModulosAdmin.Produtos,
        "entradasesaidas" => ModulosAdmin.Pagamentos,
        _ => throw new Exception($"Colecao \"{colecao}\" nao e sincronizavel.")
    };

    [HttpPost("sync/{colecao}")]
    public async Task<IActionResult> Sincronizar(string colecao, [FromBody] SyncRequest request)
    {
        try
        {
            // Uma so rota grava seis colecoes, entao a permissao e resolvida
            // pela colecao E pela acao que o delta realmente contem: mandar
            // "removidos" sem permissao de excluir para em 403 mesmo que o
            // usuario possa criar e editar na mesma tela.
            var contexto = await permissoes.ResolverAsync(request.Token ?? "");
            var modulo = ModuloDaColecao(colecao);

            var criados = Itens(request.Criados);
            var atualizados = Itens(request.Atualizados);
            var removidos = Ids(request.Removidos);

            if (criados.Count > 0) await permissoes.ExigirAsync(request.Token ?? "", modulo, AcaoPermissao.Criar);
            if (atualizados.Count > 0) await permissoes.ExigirAsync(request.Token ?? "", modulo, AcaoPermissao.Editar);
            if (removidos.Count > 0) await permissoes.ExigirAsync(request.Token ?? "", modulo, AcaoPermissao.Excluir);
            if (criados.Count == 0 && atualizados.Count == 0 && removidos.Count == 0)
                await permissoes.ExigirAsync(request.Token ?? "", modulo, AcaoPermissao.Visualizar);

            // Item 11.5: unidade do Funcionario, capacidade, gravacao e pagamentos em SyncPainelService.
            var (novosIds, alterados, apagados) = await sync.AplicarAsync(contexto, Normalizador.Texto(colecao), criados, atualizados, removidos);

            await realtime.NotificarAsync(colecao, "sincronizado", new { criados = novosIds.Count, alterados, apagados });
            await RegistrarEventosDoSync(contexto, Normalizador.Texto(colecao), criados, novosIds, atualizados, removidos);

            return OkApi(new SyncResposta(colecao, novosIds, novosIds.Count, alterados, apagados, "Alteracoes gravadas no banco."));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // ------------------------------------------------------------ EVENTOS
    /// <summary>
    /// Item 15: o que o painel gravou vira evento, com quem fez. Ate 20 itens,
    /// um evento por registro (com o nome/descricao); acima disso (importacao
    /// em lote), um evento so com as contagens, para o log nao explodir.
    /// So roda DEPOIS do SaveChanges: nada e registrado se a gravacao falhou.
    /// </summary>
    private async Task RegistrarEventosDoSync(ContextoAdmin contexto, string colecao, List<JsonElement> criados, List<string> novosIds, List<JsonElement> atualizados, List<string> removidos)
    {
        var (categoria, rotulo) = colecao switch
        {
            "agendamentos" => ("agendamento", "Agendamento"),
            "pets" => ("pet", "Pet"),
            "clientes" => ("cliente", "Cliente"),
            "servicos" => ("servico", "Serviço"),
            "produtos" => ("produto", "Produto"),
            "entradasesaidas" => ("financeiro", "Lançamento financeiro"),
            _ => ("sistema", colecao)
        };
        var total = criados.Count + atualizados.Count + removidos.Count;
        if (total == 0) return;

        EventosService.Evento Novo(string acao, string alvo, string detalhes, string nivel = "info")
            => new(categoria, acao, nivel, "admin", contexto.Usuario.Id, contexto.Usuario.Email, alvo, detalhes);

        if (total > 20)
        {
            await eventos.RegistrarAsync(Novo($"{rotulo}: alteração em lote", "",
                $"{criados.Count} criado(s), {atualizados.Count} alterado(s), {removidos.Count} excluído(s)."), HttpContext);
            return;
        }

        static string Descrever(JsonElement item)
        {
            var partes = new[] { "nome", "pet", "descricao", "dono", "dataHora", "status", "valorVenda", "valor", "tipo" }
                .Select(campo => item.TryGetProperty(campo, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? $"{campo}: {v}" : null)
                .Where(p => p is not null);
            return string.Join(" · ", partes);
        }

        for (var i = 0; i < criados.Count; i++)
            await eventos.RegistrarAsync(Novo($"{rotulo} criado", i < novosIds.Count ? novosIds[i] : "", Descrever(criados[i])), HttpContext);
        foreach (var item in atualizados)
            await eventos.RegistrarAsync(Novo($"{rotulo} alterado", Texto(item, "id"), Descrever(item)), HttpContext);
        foreach (var id in removidos)
            await eventos.RegistrarAsync(Novo($"{rotulo} excluído", id, "", "aviso"), HttpContext);
    }
}
