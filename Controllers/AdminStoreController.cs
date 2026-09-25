using System.Text.Json;
using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
public class AdminStoreController(LanePetsDbContext db, PermissaoService permissoes, RealtimeNotifier realtime) : ApiControllerBase
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
            List<Cliente> clientes = Ver(ModulosAdmin.Clientes) ? await db.Clientes.AsNoTracking().ToListAsync() : new();
            List<Servico> servicos = Ver(ModulosAdmin.Servicos) ? await db.Servicos.AsNoTracking().ToListAsync() : new();
            List<Produto> produtos = Ver(ModulosAdmin.Produtos) ? await db.Produtos.AsNoTracking().ToListAsync() : new();
            List<EntradaSaida> lancamentos = Ver(ModulosAdmin.Pagamentos) ? await db.EntradasESaidas.AsNoTracking().ToListAsync() : new();
            List<Pedido> pedidos = Ver(ModulosAdmin.Pedidos) ? await db.Pedidos.AsNoTracking().ToListAsync() : new();
            List<UsuarioCliente> usuarios = Ver(ModulosAdmin.Clientes) ? await db.UsuariosClientes.AsNoTracking().ToListAsync() : new();

            // E-mail e data de criacao da conta vivem em UsuariosClientes; o
            // painel precisa deles junto do cliente.
            var contaPorCliente = usuarios
                .GroupBy(u => u.ClienteId)
                .ToDictionary(g => g.Key, g => g.OrderBy(u => u.CriadoEm).First());

            var petsPorCliente = pets.GroupBy(p => p.ClienteId ?? "").ToDictionary(g => g.Key, g => g.Count());
            var agsPorCliente = agendamentos.GroupBy(a => a.ClienteId ?? "").ToDictionary(g => g.Key, g => g.Count());
            var pedidosPorCliente = pedidos.GroupBy(p => p.ClienteId ?? "").ToDictionary(g => g.Key, g => g.Count());

            return OkApi(new
            {
                agendamentos = agendamentos.OrderBy(a => a.DataHora).Select(a => ParaPainel(a)),
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

    /// <summary>
    /// Indicadores do painel. Cada numero e um COUNT/SUM feito no banco na hora
    /// da chamada — nada e guardado, nada e incrementado em memoria. Banco vazio
    /// devolve zero; e por isso que o painel nunca mostra numero inventado.
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] string token = "")
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(token, ModulosAdmin.Dashboard, AcaoPermissao.Visualizar);

            // O dashboard so mostra numero financeiro para quem pode ver
            // Pagamentos (regra 24). Sem a permissao, o bloco "financeiro"
            // volta zerado e marcado como restrito — o dado nao sai do banco.
            var podeFinanceiro = contexto.Pode(ModulosAdmin.Pagamentos, AcaoPermissao.Visualizar);

            var agendamentos = await db.Agendamentos.AsNoTracking().ToListAsync();
            var pedidos = await db.Pedidos.AsNoTracking().ToListAsync();
            List<EntradaSaida> lancamentos = podeFinanceiro ? await db.EntradasESaidas.AsNoTracking().ToListAsync() : new();
            var hoje = DateTime.Now.ToString("yyyy-MM-dd");

            var ativos = agendamentos.Where(a => Normalizador.Status(a.Status) != "Cancelado").ToList();
            var entradas = lancamentos.Where(e => Normalizador.Texto(e.Tipo) == "entrada").Sum(e => e.Valor);
            var saidas = lancamentos.Where(e => Normalizador.Texto(e.Tipo).StartsWith("said")).Sum(e => e.Valor);

            return OkApi(new
            {
                totais = new
                {
                    clientes = await db.Clientes.CountAsync(),
                    contasDeCliente = await db.UsuariosClientes.CountAsync(),
                    pets = await db.Pets.CountAsync(),
                    agendamentos = agendamentos.Count,
                    pedidos = pedidos.Count,
                    produtos = await db.Produtos.CountAsync(),
                    servicos = await db.Servicos.CountAsync(),
                    unidades = await db.Unidades.CountAsync(),
                    planosSeguro = await db.PlanosSeguro.CountAsync(),
                    seguros = await db.SolicitacoesSeguro.CountAsync(),
                    avaliacoes = await db.Depoimentos.CountAsync(),
                    pagamentos = ativos.Count(a => Normalizador.Texto(a.PagamentoStatus) == "pago") + pedidos.Count
                },
                agenda = new
                {
                    hoje = agendamentos.Count(a => Normalizador.Data(a.DataHora) == hoje),
                    pendentes = agendamentos.Count(a => Normalizador.Status(a.Status) == "Pendente"),
                    emAndamento = agendamentos.Count(a => Normalizador.Status(a.Status) == "Em andamento"),
                    entregues = agendamentos.Count(a => Normalizador.Status(a.Status) == "Entregue"),
                    cancelados = agendamentos.Count(a => Normalizador.Status(a.Status) == "Cancelado")
                },
                financeiro = new
                {
                    restrito = !podeFinanceiro,
                    receitaAgendamentos = podeFinanceiro ? ativos.Sum(a => a.Total) : 0m,
                    receitaPedidos = podeFinanceiro ? pedidos.Where(p => !string.Equals(p.Status, "Cancelado", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Total) : 0m,
                    entradasManuais = entradas,
                    despesas = saidas
                },
                calculadoEm = DateTime.UtcNow.ToString("O")
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

    private static object ParaPainel(Agendamento a) => new
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
        status = string.IsNullOrWhiteSpace(a.Status) ? "Pendente" : a.Status,
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
        pacote = Json(p.PacoteJson),
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
        controlaEstoque = p.ControlaEstoque
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

    /// <summary>"Franco"/"Caieiras" do banco viram os ids curtos das telas.</summary>
    private static string UnidadePainel(string? valor) => Normalizador.Unidade(valor) switch
    {
        "Franco" => "franco",
        "Caieiras" => "caieiras",
        _ => ""
    };

    private static readonly string[] SemTransporte =
        { "", "false", "0", "nao", "n", "cliente leva", "sem transporte", "nenhum" };

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

            var novosIds = new List<string>();
            int alterados = 0, apagados = 0;

            switch (Normalizador.Texto(colecao))
            {
                case "agendamentos":
                    foreach (var item in criados) novosIds.Add(await CriarAgendamento(item));
                    foreach (var item in atualizados) alterados += await AtualizarAgendamento(item);
                    apagados += await Remover(db.Agendamentos, removidos, a => a.Id);
                    break;

                case "pets":
                    foreach (var item in criados) novosIds.Add(await CriarPet(item));
                    foreach (var item in atualizados) alterados += await AtualizarPet(item);
                    apagados += await Remover(db.Pets, removidos, p => p.Id);
                    break;

                case "clientes":
                    foreach (var item in criados) novosIds.Add(await CriarCliente(item));
                    foreach (var item in atualizados) alterados += await AtualizarCliente(item);
                    apagados += await Remover(db.Clientes, removidos, c => c.Id);
                    break;

                case "servicos":
                    foreach (var item in criados) novosIds.Add(CriarServico(item));
                    foreach (var item in atualizados) alterados += await AtualizarServico(item);
                    apagados += await Remover(db.Servicos, removidos, s => s.Id);
                    break;

                case "produtos":
                    foreach (var item in criados) novosIds.Add(CriarProduto(item));
                    foreach (var item in atualizados) alterados += await AtualizarProduto(item);
                    apagados += await Remover(db.Produtos, removidos, p => p.Id);
                    break;

                case "entradasesaidas":
                    foreach (var item in criados) novosIds.Add(CriarLancamento(item));
                    foreach (var item in atualizados) alterados += await AtualizarLancamento(item);
                    apagados += await Remover(db.EntradasESaidas, removidos, e => e.Id);
                    break;

                default:
                    throw new Exception($"Colecao \"{colecao}\" nao e sincronizavel.");
            }

            await db.SaveChangesAsync();
            await realtime.NotificarAsync(colecao, "sincronizado", new { criados = novosIds.Count, alterados, apagados });

            return OkApi(new
            {
                colecao,
                novosIds,
                criados = novosIds.Count,
                alterados,
                apagados,
                message = "Alteracoes gravadas no banco."
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // ----------------------------------------------------------------- AGD
    private async Task<string> CriarAgendamento(JsonElement item)
    {
        var id = Id(item, "AGD");
        var dono = Texto(item, "dono");
        var telefone = Texto(item, "telefone");

        // Liga o agendamento ao cadastro real sempre que der: e o que faz o
        // agendamento criado no painel aparecer tambem na conta do cliente.
        var pet = await ResolverPet(Texto(item, "petId"), Texto(item, "pet"), dono);
        var clienteId = Texto(item, "clienteId");
        if (clienteId.Length == 0) clienteId = pet?.ClienteId ?? await ResolverClienteId(dono, telefone);

        db.Agendamentos.Add(new Agendamento
        {
            Id = id,
            Pet = Texto(item, "pet"),
            Dono = dono,
            Telefone = telefone,
            DataHora = Texto(item, "dataHora"),
            ServicosJson = Bruto(item, "servicos") ?? "[]",
            Total = Numero(item, "total"),
            Transporte = DescricaoTransporte(item),
            ValorTransporte = Numero(item, "valorTransporte"),
            Status = Preenchido(Texto(item, "status"), "Pendente"),
            PagamentoStatus = Preenchido(Texto(item, "pagamentoStatus"), "A pagar"),
            FormaPagamento = Texto(item, "formaPagamento"),
            Obs = Texto(item, "obs"),
            Unidade = UnidadeBanco(Texto(item, "unidade")),
            ClienteId = clienteId,
            PetId = pet?.Id ?? Texto(item, "petId")
        });
        return id;
    }

    private async Task<int> AtualizarAgendamento(JsonElement item)
    {
        var alvo = await db.Agendamentos.FindAsync(Texto(item, "id"));
        if (alvo is null) return 0;
        alvo.Pet = Texto(item, "pet");
        alvo.Dono = Texto(item, "dono");
        alvo.Telefone = Texto(item, "telefone");
        alvo.DataHora = Texto(item, "dataHora");
        alvo.ServicosJson = Bruto(item, "servicos") ?? alvo.ServicosJson;
        alvo.Total = Numero(item, "total");
        alvo.Transporte = DescricaoTransporte(item, alvo.Transporte);
        alvo.ValorTransporte = Numero(item, "valorTransporte");
        alvo.Status = Preenchido(Texto(item, "status"), alvo.Status);
        alvo.PagamentoStatus = Preenchido(Texto(item, "pagamentoStatus"), alvo.PagamentoStatus);
        alvo.FormaPagamento = Texto(item, "formaPagamento");
        alvo.Obs = Texto(item, "obs");
        var unidade = UnidadeBanco(Texto(item, "unidade"));
        if (unidade.Length > 0) alvo.Unidade = unidade;
        return 1;
    }

    /// <summary>Recupera a descricao do transporte sem perder a que ja estava gravada.</summary>
    private static string DescricaoTransporte(JsonElement item, string? atual = null)
    {
        var marcado = Booleano(item, "transporte") || Numero(item, "valorTransporte") > 0;
        // Sem transporte marcado a descricao antiga nao pode continuar valendo:
        // desmarcar o transporte na tela precisa apagar tambem a descricao.
        if (!marcado) return "Cliente leva";
        var descricao = Texto(item, "transporteDescricao");
        if (descricao.Length > 0 && !SemTransporte.Contains(Normalizador.Texto(descricao))
            && Normalizador.Texto(descricao) != "true") return descricao;
        return string.IsNullOrWhiteSpace(atual) || Normalizador.Texto(atual) == "cliente leva" ? "Busca e entrega" : atual!;
    }

    private async Task<Pet?> ResolverPet(string petId, string nomePet, string dono)
    {
        if (petId.Length > 0)
        {
            var porId = await db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == petId);
            if (porId is not null) return porId;
        }
        if (nomePet.Length == 0) return null;
        var chavePet = Normalizador.Texto(nomePet);
        var chaveDono = Normalizador.Texto(dono);
        return (await db.Pets.AsNoTracking().ToListAsync())
            .FirstOrDefault(p => Normalizador.Texto(p.PetNome) == chavePet && Normalizador.Texto(p.Dono) == chaveDono);
    }

    private async Task<string> ResolverClienteId(string nome, string telefone)
    {
        if (nome.Length == 0 && telefone.Length == 0) return "";
        var chaveNome = Normalizador.Texto(nome);
        var digitos = new string((telefone ?? "").Where(char.IsDigit).ToArray());
        var clientes = await db.Clientes.AsNoTracking().ToListAsync();
        var achado = clientes.FirstOrDefault(c => Normalizador.Texto(c.Nome) == chaveNome)
                     ?? (digitos.Length >= 8
                         ? clientes.FirstOrDefault(c => new string(c.Telefone.Where(char.IsDigit).ToArray()) == digitos)
                         : null);
        return achado?.Id ?? "";
    }

    // ----------------------------------------------------------------- PET
    private async Task<string> CriarPet(JsonElement item)
    {
        var id = Id(item, "PET");
        var dono = Texto(item, "dono");
        var telefone = Texto(item, "telefone");
        var endereco = Texto(item, "endereco");
        var clienteId = Texto(item, "clienteId");
        if (clienteId.Length == 0) clienteId = await ResolverClienteId(dono, telefone);

        // Pet cadastrado no painel para um dono que ainda nao existe: cria o
        // cliente junto, para nao nascer um pet orfao.
        if (clienteId.Length == 0 && dono.Length > 0)
        {
            var cliente = new Cliente
            {
                Id = NovoId("CLI"),
                Nome = dono,
                Telefone = telefone,
                Endereco = endereco,
                Origem = "cadastro_painel",
                Status = "ativo"
            };
            db.Clientes.Add(cliente);
            clienteId = cliente.Id;
        }

        db.Pets.Add(new Pet
        {
            Id = id,
            ClienteId = clienteId,
            Dono = dono,
            PetNome = Texto(item, "pet"),
            Tipo = Texto(item, "tipo"),
            Raca = Texto(item, "raca"),
            Telefone = telefone,
            Endereco = endereco,
            PacoteJson = Bruto(item, "pacote") ?? "",
            Unidade = UnidadeBanco(Texto(item, "unidade"))
        });
        return id;
    }

    private async Task<int> AtualizarPet(JsonElement item)
    {
        var alvo = await db.Pets.FindAsync(Texto(item, "id"));
        if (alvo is null) return 0;
        alvo.Dono = Texto(item, "dono");
        alvo.PetNome = Texto(item, "pet");
        alvo.Tipo = Texto(item, "tipo");
        alvo.Raca = Texto(item, "raca");
        alvo.Telefone = Texto(item, "telefone");
        alvo.Endereco = Texto(item, "endereco");
        alvo.PacoteJson = Bruto(item, "pacote") ?? alvo.PacoteJson;
        var unidade = UnidadeBanco(Texto(item, "unidade"));
        if (unidade.Length > 0) alvo.Unidade = unidade;
        return 1;
    }

    // ------------------------------------------------------------- CLIENTE
    private async Task<string> CriarCliente(JsonElement item)
    {
        var id = Id(item, "CLI");
        db.Clientes.Add(new Cliente
        {
            Id = id,
            Nome = Texto(item, "nome"),
            Telefone = Texto(item, "telefone"),
            Endereco = Texto(item, "endereco"),
            Observacoes = Texto(item, "observacoes"),
            Origem = Preenchido(Texto(item, "origem"), "cadastro_painel"),
            Status = Preenchido(Texto(item, "status"), "ativo")
        });
        await Task.CompletedTask;
        return id;
    }

    private async Task<int> AtualizarCliente(JsonElement item)
    {
        var alvo = await db.Clientes.FindAsync(Texto(item, "id"));
        if (alvo is null) return 0;
        var nome = Texto(item, "nome");
        if (nome.Length > 0) alvo.Nome = nome;
        alvo.Telefone = Texto(item, "telefone");
        alvo.Endereco = Texto(item, "endereco");
        alvo.Observacoes = Texto(item, "observacoes");
        alvo.Status = Preenchido(Texto(item, "status"), alvo.Status);

        // O nome do dono aparece copiado nos pets e nos agendamentos: mantem
        // as duas pontas coerentes, como ja acontece na area do cliente.
        foreach (var pet in await db.Pets.Where(p => p.ClienteId == alvo.Id).ToListAsync())
        {
            pet.Dono = alvo.Nome;
            pet.Telefone = alvo.Telefone;
        }
        foreach (var ag in await db.Agendamentos.Where(a => a.ClienteId == alvo.Id).ToListAsync())
        {
            ag.Dono = alvo.Nome;
            ag.Telefone = alvo.Telefone;
        }
        return 1;
    }

    // ------------------------------------------------------------- SERVICO
    private string CriarServico(JsonElement item)
    {
        var id = Id(item, "SRV");
        db.Servicos.Add(new Servico
        {
            Id = id,
            Nome = Texto(item, "nome"),
            Preco = Numero(item, "preco"),
            Porte = Texto(item, "porte"),
            AdicionaisJson = Bruto(item, "adicionais") ?? "[]",
            Pacote = Texto(item, "pacote"),
            Adicional = Texto(item, "adicional")
        });
        return id;
    }

    private async Task<int> AtualizarServico(JsonElement item)
    {
        var alvo = await db.Servicos.FindAsync(Texto(item, "id"));
        if (alvo is null) return 0;
        alvo.Nome = Texto(item, "nome");
        alvo.Preco = Numero(item, "preco");
        alvo.Porte = Texto(item, "porte");
        alvo.AdicionaisJson = Bruto(item, "adicionais") ?? alvo.AdicionaisJson;
        alvo.Pacote = Texto(item, "pacote");
        alvo.Adicional = Texto(item, "adicional");
        return 1;
    }

    // ------------------------------------------------------------- PRODUTO
    private string CriarProduto(JsonElement item)
    {
        var id = Id(item, "PRD");
        db.Produtos.Add(new Produto
        {
            Id = id,
            Codigo = Texto(item, "codigo"),
            Nome = Texto(item, "nome"),
            Categoria = Texto(item, "categoria"),
            ValorCompra = Numero(item, "valorCompra"),
            ValorVenda = Numero(item, "valorVenda"),
            Estoque = (int)Numero(item, "estoque"),
            EstoqueMinimo = (int)Numero(item, "estoqueMinimo"),
            ControlaEstoque = Booleano(item, "controlaEstoque")
        });
        return id;
    }

    private async Task<int> AtualizarProduto(JsonElement item)
    {
        var alvo = await db.Produtos.FindAsync(Texto(item, "id"));
        if (alvo is null) return 0;
        alvo.Codigo = Texto(item, "codigo");
        alvo.Nome = Texto(item, "nome");
        alvo.Categoria = Texto(item, "categoria");
        alvo.ValorCompra = Numero(item, "valorCompra");
        alvo.ValorVenda = Numero(item, "valorVenda");
        // Estoque so muda quando a tela realmente mandou o campo: assim uma
        // edicao de preco nao zera o estoque dado pela baixa de um pedido.
        if (item.TryGetProperty("estoque", out _)) alvo.Estoque = (int)Numero(item, "estoque");
        if (item.TryGetProperty("estoqueMinimo", out _)) alvo.EstoqueMinimo = (int)Numero(item, "estoqueMinimo");
        if (item.TryGetProperty("controlaEstoque", out _)) alvo.ControlaEstoque = Booleano(item, "controlaEstoque");
        return 1;
    }

    // ---------------------------------------------------------- LANCAMENTO
    private string CriarLancamento(JsonElement item)
    {
        var id = Id(item, "FIN");
        db.EntradasESaidas.Add(new EntradaSaida
        {
            Id = id,
            Data = Texto(item, "data"),
            Descricao = Texto(item, "descricao"),
            Tipo = Texto(item, "tipo"),
            Valor = Numero(item, "valor"),
            Unidade = UnidadeBanco(Texto(item, "unidade")),
            Origem = Preenchido(Texto(item, "origem"), "painel")
        });
        return id;
    }

    private async Task<int> AtualizarLancamento(JsonElement item)
    {
        var alvo = await db.EntradasESaidas.FindAsync(Texto(item, "id"));
        if (alvo is null) return 0;
        alvo.Data = Texto(item, "data");
        alvo.Descricao = Texto(item, "descricao");
        alvo.Tipo = Texto(item, "tipo");
        alvo.Valor = Numero(item, "valor");
        alvo.Unidade = UnidadeBanco(Texto(item, "unidade"));
        return 1;
    }

    // =======================================================================
    // AUXILIARES
    // =======================================================================

    private async Task<int> Remover<T>(DbSet<T> conjunto, List<string> ids, Func<T, string> chave) where T : class
    {
        if (ids.Count == 0) return 0;
        var alvos = (await conjunto.ToListAsync()).Where(x => ids.Contains(chave(x))).ToList();
        conjunto.RemoveRange(alvos);
        return alvos.Count;
    }

    private static string NovoId(string prefixo) => prefixo + "-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    /// <summary>Usa o id que a tela mandou; se veio vazio, gera um novo.</summary>
    private static string Id(JsonElement item, string prefixo)
    {
        var informado = Texto(item, "id");
        return informado.Length > 0 ? informado : NovoId(prefixo);
    }

    private static string Preenchido(string valor, string padrao) => valor.Length > 0 ? valor : padrao;

    /// <summary>
    /// Grava a unidade na mesma forma que o banco ja usa ("franco"/"caieiras"),
    /// que e tambem a que a area do cliente escreve. A leitura continua passando
    /// pelo Normalizador, entao registros antigos em qualquer capitalizacao
    /// continuam sendo reconhecidos.
    /// </summary>
    private static string UnidadeBanco(string? valor) => Normalizador.Texto(valor) switch
    {
        "franco" or "franco da rocha" => "franco",
        "caieiras" => "caieiras",
        _ => ""
    };

    private static List<JsonElement> Itens(JsonElement valor)
        => valor.ValueKind == JsonValueKind.Array
            ? valor.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList()
            : new List<JsonElement>();

    private static List<string> Ids(JsonElement valor)
        => valor.ValueKind == JsonValueKind.Array
            ? valor.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.ToString())
                   .Where(x => x.Length > 0).ToList()
            : new List<string>();

    private static string Texto(JsonElement objeto, string nome)
    {
        if (!objeto.TryGetProperty(nome, out var valor)) return "";
        return valor.ValueKind switch
        {
            JsonValueKind.String => valor.GetString()?.Trim() ?? "",
            JsonValueKind.Number => valor.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    private static decimal Numero(JsonElement objeto, string nome)
    {
        if (!objeto.TryGetProperty(nome, out var valor)) return 0m;
        if (valor.ValueKind == JsonValueKind.Number && valor.TryGetDecimal(out var d)) return d;
        var texto = (valor.ValueKind == JsonValueKind.String ? valor.GetString() : null) ?? "";
        texto = texto.Replace("R$", "").Trim();
        if (texto.Contains(',')) texto = texto.Replace(".", "").Replace(',', '.');
        return decimal.TryParse(texto, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    private static bool Booleano(JsonElement objeto, string nome)
    {
        if (!objeto.TryGetProperty(nome, out var valor)) return false;
        return valor.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => valor.TryGetDecimal(out var n) && n != 0,
            JsonValueKind.String => Normalizador.Texto(valor.GetString()) is "sim" or "true" or "1",
            _ => false
        };
    }

    private static string? Bruto(JsonElement objeto, string nome)
        => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind is JsonValueKind.Array or JsonValueKind.Object
            ? valor.GetRawText()
            : null;
}
