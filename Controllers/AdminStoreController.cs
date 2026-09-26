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
public class AdminStoreController(LanePetsDbContext db, PermissaoService permissoes, RealtimeNotifier realtime, EventosService eventos, SyncPainelService sync) : ApiControllerBase
{
    // =======================================================================
    // LEITURA — o painel inteiro em uma chamada
    // =======================================================================

    /// <summary>
    /// Todas as colecoes do painel, no formato das telas antigas. E a unica
    /// fonte de dados do admin: nada mais vem do navegador.
    /// </summary>
    [HttpGet("estado")]
    public async Task<IActionResult> Estado([FromQuery] string token = "")
    {
        try
        {
            // Este endpoint devolve o painel inteiro de uma vez, entao a
            // permissao e aplicada COLECAO POR COLECAO: o que o usuario nao
            // pode ver simplesmente nao e lido do banco e volta vazio. Assim
            // "esconder no menu" e "nao receber o dado" passam a ser a mesma
            // coisa — nem abrindo o DevTools o dado aparece.
            var contexto = await permissoes.ResolverAsync(token);
            bool Ver(string modulo) => contexto.Pode(modulo, AcaoPermissao.Visualizar);

            List<Agendamento> agendamentos = Ver(ModulosAdmin.Agendamentos) ? await db.Agendamentos.AsNoTracking().ToListAsync() : new();
            List<Pet> pets = Ver(ModulosAdmin.Pets) ? await db.Pets.AsNoTracking().ToListAsync() : new();

            // Funcionario (item 1): agendamentos e pets de outra unidade nao
            // saem do servidor. Para administradores os dois filtros nao cortam nada.
            agendamentos = agendamentos.Where(a => contexto.VeUnidade(a.Unidade)).ToList();
            var petsVisiveis = await permissoes.PetsVisiveisAsync(contexto);
            if (petsVisiveis is not null) pets = pets.Where(p => petsVisiveis.Contains(p.Id)).ToList();
            List<Cliente> clientes = Ver(ModulosAdmin.Clientes) ? await db.Clientes.AsNoTracking().ToListAsync() : new();
            List<Servico> servicos = Ver(ModulosAdmin.Servicos) ? await db.Servicos.AsNoTracking().ToListAsync() : new();
            List<Produto> produtos = Ver(ModulosAdmin.Produtos) ? await db.Produtos.AsNoTracking().ToListAsync() : new();
            List<EntradaSaida> lancamentos = Ver(ModulosAdmin.Pagamentos) ? await db.EntradasESaidas.AsNoTracking().ToListAsync() : new();
            List<Pedido> pedidos = Ver(ModulosAdmin.Pedidos) ? await db.Pedidos.AsNoTracking().ToListAsync() : new();
            pedidos = pedidos.Where(p => contexto.VeUnidade(p.Unidade)).ToList();   // item 5: funcionario ve so a unidade dele
            List<UsuarioCliente> usuarios = Ver(ModulosAdmin.Clientes) ? await db.UsuariosClientes.AsNoTracking().ToListAsync() : new();

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
                agendamentos = agendamentos.OrderBy(a => a.DataHora).Select(a => ParaPainel(a, nomesResponsaveis)),
                pets = pets.Select(p => ParaPainel(p)),
                servicos = servicos.OrderBy(s => s.Nome).Select(s => ParaPainel(s)),
                produtos = produtos.OrderBy(p => p.Nome).Select(p => ParaPainel(p)),
                entradasESaidas = lancamentos.OrderByDescending(e => e.Data).Select(e => ParaPainel(e)),
                clientes = clientes.OrderBy(c => c.Nome).Select(c => new
                {
                    id = c.Id,
                    nome = c.Nome,
                    telefone = c.Telefone,
                    endereco = c.Endereco,
                    observacoes = c.Observacoes,
                    origem = string.IsNullOrWhiteSpace(c.Origem) ? "cadastro_painel" : c.Origem,
                    status = string.IsNullOrWhiteSpace(c.Status) ? "ativo" : c.Status,
                    email = contaPorCliente.TryGetValue(c.Id, out var u) ? u.Email : "",
                    temConta = contaPorCliente.ContainsKey(c.Id),
                    criadoEm = contaPorCliente.TryGetValue(c.Id, out var u2) ? u2.CriadoEm.ToString("O") : "",
                    qtdPets = petsPorCliente.TryGetValue(c.Id, out var qp) ? qp : 0,
                    qtdAgendamentos = agsPorCliente.TryGetValue(c.Id, out var qa) ? qa : 0,
                    qtdPedidos = pedidosPorCliente.TryGetValue(c.Id, out var qd) ? qd : 0
                }),
                totais = new
                {
                    clientes = clientes.Count,
                    contasDeCliente = usuarios.Count,
                    pets = pets.Count,
                    agendamentos = agendamentos.Count,
                    produtos = produtos.Count,
                    servicos = servicos.Count,
                    pedidos = pedidos.Count,
                    lancamentos = lancamentos.Count
                },
                atualizadoEm = DateTime.UtcNow.ToString("O"),
                permissoes = PermissaoService.Mapear(contexto)
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>Lista de clientes do painel: todo mundo que existe no banco,
    /// tenha vindo do cadastro do site ou do cadastro feito pela equipe.</summary>
    [HttpGet("clientes")]
    public async Task<IActionResult> Clientes([FromQuery] string token = "")
    {
        try
        {
            await permissoes.ExigirAsync(token, ModulosAdmin.Clientes, AcaoPermissao.Visualizar);
            var clientes = await db.Clientes.AsNoTracking().OrderBy(c => c.Nome).ToListAsync();
            var usuarios = await db.UsuariosClientes.AsNoTracking().ToListAsync();
            var pets = await db.Pets.AsNoTracking().ToListAsync();
            var agendamentos = await db.Agendamentos.AsNoTracking().ToListAsync();
            var pedidos = await db.Pedidos.AsNoTracking().ToListAsync();

            var conta = usuarios.GroupBy(u => u.ClienteId).ToDictionary(g => g.Key, g => g.OrderBy(u => u.CriadoEm).First());

            return OkApi(clientes.Select(c => new
            {
                id = c.Id,
                nome = c.Nome,
                telefone = c.Telefone,
                endereco = c.Endereco,
                email = conta.TryGetValue(c.Id, out var u) ? u.Email : "",
                temConta = conta.ContainsKey(c.Id),
                criadoEm = conta.TryGetValue(c.Id, out var u2) ? u2.CriadoEm.ToString("O") : "",
                origem = string.IsNullOrWhiteSpace(c.Origem) ? "cadastro_painel" : c.Origem,
                status = string.IsNullOrWhiteSpace(c.Status) ? "ativo" : c.Status,
                observacoes = c.Observacoes,
                qtdPets = pets.Count(p => p.ClienteId == c.Id),
                qtdAgendamentos = agendamentos.Count(a => a.ClienteId == c.Id),
                qtdPedidos = pedidos.Count(p => p.ClienteId == c.Id),
                pets = pets.Where(p => p.ClienteId == c.Id).Select(p => new { id = p.Id, pet = p.PetNome, tipo = p.Tipo, raca = p.Raca })
            }));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // =======================================================================
    // CONVERSAO BANCO -> FORMATO DAS TELAS
    // As telas antigas usam nomes proprios de campo (pet, dono, dataHora...).
    // Traduzir aqui evita reescrever milhares de linhas de tela e mantem o
    // banco como unica fonte de verdade.
    // =======================================================================

    private static object ParaPainel(Agendamento a, IReadOnlyDictionary<string, string>? responsaveis = null) => new
    {
        id = a.Id,
        unidade = UnidadePainel(a.Unidade),
        pet = a.Pet,
        dono = a.Dono,
        telefone = a.Telefone,
        dataHora = a.DataHora,
        servicos = Json(a.ServicosJson),
        total = a.Total,
        // A tela trabalha com um "sim/nao" de transporte; o banco guarda a
        // descricao ("Busca e entrega", "Cliente leva"). Os dois vao juntos.
        transporte = TemTransporte(a.Transporte, a.ValorTransporte),
        transporteDescricao = a.Transporte,
        valorTransporte = a.ValorTransporte,
        status = StatusAgendamento.Exibir(a.Status),
        responsavelId = a.ResponsavelId,
        responsavelNome = a.ResponsavelId.Length > 0 && responsaveis is not null && responsaveis.TryGetValue(a.ResponsavelId, out var resp) ? resp : "",
        pagamentoStatus = string.IsNullOrWhiteSpace(a.PagamentoStatus) ? "A pagar" : a.PagamentoStatus,
        formaPagamento = a.FormaPagamento,
        obs = a.Obs,
        clienteId = a.ClienteId,
        petId = a.PetId
    };

    private static object ParaPainel(Pet p) => new
    {
        id = p.Id,
        dono = p.Dono,
        pet = p.PetNome,
        tipo = p.Tipo,
        raca = p.Raca,
        telefone = p.Telefone,
        endereco = p.Endereco,
        unidade = UnidadePainel(p.Unidade),
        pacote = JsonObjeto(p.PacoteJson),
        clienteId = p.ClienteId,
        // Campos da ficha que a area do cliente ja gravava e que o painel
        // precisa para mostrar o pet nos detalhes do agendamento. Acrescimo
        // SOMENTE de leitura: AtualizarPet nao escreve nenhum deles, entao
        // salvar um pet pelo painel continua sem poder apaga-los.
        fotoUrl = p.FotoUrl,
        sexo = p.Sexo,
        dataNascimento = p.DataNascimento,
        peso = p.Peso,
        cor = p.Cor,
        porte = p.Porte,
        observacoes = p.Observacoes,
        necessidadesEspeciais = p.NecessidadesEspeciais
    };

    private static object ParaPainel(Servico s) => new
    {
        id = s.Id,
        nome = s.Nome,
        preco = s.Preco,
        porte = s.Porte,
        adicionais = Json(s.AdicionaisJson),
        pacote = s.Pacote,
        adicional = s.Adicional
    };

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

    /// <summary>
    /// A tela trabalha com um sim/nao. O banco tem historico gravado de duas
    /// formas: como descricao ("Busca e entrega") e, nos registros importados
    /// do navegador, como o texto "True"/"False". As duas sao aceitas aqui.
    /// </summary>
    private static bool TemTransporte(string? descricao, decimal valor)
    {
        if (valor > 0) return true;
        var texto = Normalizador.Texto(descricao);
        return !SemTransporte.Contains(texto);
    }

    /// <summary>Pacote do pet: sempre objeto (vazio = {}). Devolver [] fazia o painel
    /// marcar "ativo" num array, que o JSON.stringify descarta — o pacote nunca gravava.</summary>
    private static object JsonObjeto(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return new { };
        try
        {
            var valor = JsonSerializer.Deserialize<JsonElement>(bruto);
            return valor.ValueKind == JsonValueKind.Object ? valor : new { };
        }
        catch { return new { }; }
    }

    private static object Json(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return Array.Empty<object>();
        try { return JsonSerializer.Deserialize<JsonElement>(bruto); }
        catch { return Array.Empty<object>(); }
    }

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
