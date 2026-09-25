using System.Text.Json;
using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;

/// <summary>
/// Leitura consolidada para o painel administrativo, calculada no servidor a
/// partir do lanepets.db — o mesmo banco que a area do cliente grava.
///
/// Por que este controller existe: as telas do painel liam o localStorage do
/// navegador, entao nada que o cliente fazia chegava ao admin. Aqui ficam os
/// numeros do dashboard e as listas que faltavam na API (produtos com estoque,
/// lancamentos financeiros, pacotes e pedidos), mais a importacao do que ainda
/// so existia no navegador. Nenhum endpoint antigo foi alterado.
/// </summary>
[Route("api/admin")]
public class AdminSyncController(LanePetsDbContext db, PermissaoService permissoes, RealtimeNotifier realtime) : ApiControllerBase
{
    // -----------------------------------------------------------------------
    // DASHBOARD
    // -----------------------------------------------------------------------

    /// <summary>Todos os indicadores do painel, calculados sobre os dados reais.</summary>
    [HttpGet("resumo")]
    public async Task<IActionResult> Resumo([FromQuery] string token = "", [FromQuery] string? de = null, [FromQuery] string? ate = null, [FromQuery] string? unidade = "todas")
    {
        try
        {
            // O resumo cruza agenda com dinheiro, entao exige o Dashboard e,
            // para a parte financeira, tambem Pagamentos.
            var contexto = await permissoes.ExigirAsync(token, ModulosAdmin.Dashboard, AcaoPermissao.Visualizar);
            var podeFinanceiro = contexto.Pode(ModulosAdmin.Pagamentos, AcaoPermissao.Visualizar);

            var agendamentos = await db.Agendamentos.AsNoTracking().ToListAsync();
            List<EntradaSaida> lancamentos = podeFinanceiro ? await db.EntradasESaidas.AsNoTracking().ToListAsync() : new();
            var pedidos = await db.Pedidos.AsNoTracking().ToListAsync();
            var produtos = await db.Produtos.AsNoTracking().ToListAsync();

            var filtro = (unidade ?? "todas").Trim().ToLowerInvariant();
            var periodo = Calcular(agendamentos, lancamentos, pedidos, de, ate, filtro);
            var (deAnterior, ateAnterior) = PeriodoAnterior(de, ate);
            var anterior = Calcular(agendamentos, lancamentos, pedidos, deAnterior, ateAnterior, filtro);

            var porUnidade = new[] { "franco", "caieiras", "sem-unidade" }.Select(u =>
            {
                var d = Calcular(agendamentos, lancamentos, pedidos, de, ate, u);
                return new
                {
                    id = u,
                    nome = NomeUnidade(u),
                    receita = d.Receita,
                    despesas = d.Despesas,
                    resultado = d.Resultado,
                    margem = d.Margem,
                    atendimentos = d.Atendimentos.Count,
                    ticket = d.Ticket
                };
            }).ToList();

            var alertas = new List<string>();
            if (periodo.PagamentosPendentes > 0) alertas.Add($"Existem {periodo.PagamentosPendentes} atendimentos com pagamento nao marcado como pago.");
            if (periodo.Resultado < 0) alertas.Add("O resultado do periodo esta negativo. Revise despesas e recebimentos.");
            if (agendamentos.Any(a => string.IsNullOrWhiteSpace(Normalizador.Unidade(a.Unidade)))) alertas.Add("Ha agendamentos sem unidade definida; eles aparecem como \"Sem unidade / antigos\".");
            var semEstoque = produtos.Where(p => p.ControlaEstoque && p.Estoque <= p.EstoqueMinimo).OrderBy(p => p.Estoque).ToList();
            if (semEstoque.Count > 0) alertas.Add($"{semEstoque.Count} produto(s) no estoque minimo ou abaixo dele.");
            var avaliacoesPendentes = await db.Depoimentos.CountAsync(d => d.Status == "Pendente");
            if (avaliacoesPendentes > 0) alertas.Add($"{avaliacoesPendentes} avaliacao(oes) aguardando moderacao.");
            var segurosPendentes = await db.SolicitacoesSeguro.CountAsync(s => s.Status == "Pendente");
            if (segurosPendentes > 0) alertas.Add($"{segurosPendentes} solicitacao(oes) de seguro aguardando contato.");
            if (alertas.Count == 0) alertas.Add("Nenhum alerta importante para o periodo selecionado.");

            return OkApi(new
            {
                periodo = new { de, ate, unidade = filtro },
                totais = new
                {
                    clientes = await db.Clientes.CountAsync(),
                    pets = await db.Pets.CountAsync(),
                    agendamentos = agendamentos.Count,
                    agendamentosAtivos = agendamentos.Count(a => Normalizador.Status(a.Status) != "Cancelado"),
                    agendamentosCancelados = agendamentos.Count(a => Normalizador.Status(a.Status) == "Cancelado"),
                    pedidos = pedidos.Count,
                    produtos = produtos.Count,
                    servicos = await db.Servicos.CountAsync(),
                    planosSeguro = await db.PlanosSeguro.CountAsync(p => p.Ativo),
                    solicitacoesSeguro = await db.SolicitacoesSeguro.CountAsync(),
                    // Contagens reais da tabela de contratos: nada fixo na tela.
                    segurosAtivos = await db.SolicitacoesSeguro.CountAsync(s => s.Status != "Cancelada"),
                    segurosCancelados = await db.SolicitacoesSeguro.CountAsync(s => s.Status == "Cancelada"),
                    segurosPagamentosPendentes = await db.SolicitacoesSeguro.CountAsync(s => s.Status != "Cancelada" && s.PagamentoStatus == "Pendente"),
                    avaliacoes = await db.Depoimentos.CountAsync(),
                    avaliacoesPendentes,
                    contasDeCliente = await db.UsuariosClientes.CountAsync()
                },
                financeiro = new
                {
                    receita = periodo.Receita,
                    despesas = periodo.Despesas,
                    resultado = periodo.Resultado,
                    margem = periodo.Margem,
                    atendimentos = periodo.Atendimentos.Count,
                    ticketMedio = periodo.Ticket
                },
                anterior = new
                {
                    de = deAnterior,
                    ate = ateAnterior,
                    receita = anterior.Receita,
                    despesas = anterior.Despesas,
                    resultado = anterior.Resultado,
                    atendimentos = anterior.Atendimentos.Count
                },
                operacional = new
                {
                    atendimentos = periodo.Atendimentos.Count,
                    pagos = periodo.PagamentosPagos,
                    pendentes = periodo.PagamentosPendentes,
                    transporteFaturado = periodo.Transporte,
                    cancelados = periodo.Cancelados,
                    pedidosDoPeriodo = periodo.Pedidos.Count,
                    receitaDePedidos = periodo.ReceitaPedidos
                },
                categorias = periodo.Categorias.Where(c => c.Value > 0).OrderByDescending(c => c.Value).Select(c => new { nome = c.Key, valor = c.Value }),
                porUnidade,
                mensal = SerieMensal(agendamentos, lancamentos, pedidos, filtro),
                estoqueBaixo = semEstoque.Take(10).Select(p => new { p.Id, p.Codigo, p.Nome, p.Estoque, p.EstoqueMinimo }),
                alertas,
                atualizadoEm = DateTime.UtcNow.ToString("O")
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    private sealed record Consolidado(
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
    private static Consolidado Calcular(List<Agendamento> agendamentos, List<EntradaSaida> lancamentos, List<Pedido> pedidos, string? de, string? ate, string filtroUnidade)
    {
        bool DaUnidade(string? valor)
        {
            if (filtroUnidade is "todas" or "") return true;
            var norm = Normalizador.Unidade(valor);
            return filtroUnidade switch
            {
                "franco" => norm == "Franco",
                "caieiras" => norm == "Caieiras",
                "sem-unidade" => norm == "",
                _ => true
            };
        }

        var doPeriodo = agendamentos
            .Where(a => Normalizador.Dentro(Normalizador.Data(a.DataHora), de, ate) && DaUnidade(a.Unidade))
            .ToList();

        var cancelados = doPeriodo.Count(a => Normalizador.Status(a.Status) == "Cancelado");
        var ativos = doPeriodo.Where(a => Normalizador.Status(a.Status) != "Cancelado").ToList();

        var lancamentosDoPeriodo = lancamentos
            .Where(e => Normalizador.Dentro(Normalizador.Data(e.Data), de, ate) && DaUnidade(e.Unidade))
            .ToList();

        // Pedidos da loja nao tem unidade propria: entram em "todas" e em "sem-unidade".
        var pedidosDoPeriodo = (filtroUnidade is "todas" or "" or "sem-unidade")
            ? pedidos.Where(p => Normalizador.Dentro(p.CriadoEm.ToString("yyyy-MM-dd"), de, ate)
                                 && !string.Equals(p.Status, "Cancelado", StringComparison.OrdinalIgnoreCase)).ToList()
            : new List<Pedido>();

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

    private static (string? De, string? Ate) PeriodoAnterior(string? de, string? ate)
    {
        if (!DateTime.TryParse(de, out var inicio) || !DateTime.TryParse(ate, out var fim)) return (null, null);
        var dias = (fim - inicio).Days + 1;
        var fimAnterior = inicio.AddDays(-1);
        return (fimAnterior.AddDays(-dias + 1).ToString("yyyy-MM-dd"), fimAnterior.ToString("yyyy-MM-dd"));
    }

    private static object SerieMensal(List<Agendamento> agendamentos, List<EntradaSaida> lancamentos, List<Pedido> pedidos, string filtroUnidade)
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

    private static string NomeUnidade(string id) => id switch
    {
        "franco" => "Franco da Rocha",
        "caieiras" => "Caieiras",
        _ => "Sem unidade / antigos"
    };

    // -----------------------------------------------------------------------
    // LISTAS QUE FALTAVAM NA API
    // -----------------------------------------------------------------------

    [HttpGet("produtos")]
    public async Task<IActionResult> Produtos([FromQuery] string token = "")
    {
        try
        {
            await permissoes.ExigirAsync(token, ModulosAdmin.Produtos, AcaoPermissao.Visualizar);
            var produtos = await db.Produtos.AsNoTracking().OrderBy(p => p.Nome).ToListAsync();
            return OkApi(produtos.Select(p => new { p.Id, p.Codigo, p.Nome, p.Categoria, p.ValorCompra, p.ValorVenda, p.Estoque, p.EstoqueMinimo, p.ControlaEstoque, margem = p.ValorCompra == 0 ? 0 : Math.Round((p.ValorVenda - p.ValorCompra) / p.ValorCompra * 100, 1) }));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>Ajusta o estoque de um produto. Unica escrita desta fase: sem ela o
    /// controle de estoque nao teria como ser ligado por quem usa o painel.</summary>
    [HttpPost("produtos/{id}/estoque")]
    public async Task<IActionResult> AjustarEstoque(string id, [FromBody] EstoqueRequest request)
    {
        try
        {
            // Ajustar estoque altera o produto: exige Produtos > Editar.
            await permissoes.ExigirAsync(request.Token ?? "", ModulosAdmin.Produtos, AcaoPermissao.Editar);
            var produto = await db.Produtos.FindAsync(id) ?? throw new Exception("Produto nao encontrado.");
            if (request.Estoque < 0 || request.EstoqueMinimo < 0) throw new Exception("O estoque nao pode ser negativo.");
            produto.Estoque = request.Estoque;
            produto.EstoqueMinimo = request.EstoqueMinimo;
            produto.ControlaEstoque = request.ControlaEstoque;
            await db.SaveChangesAsync();
            await realtime.NotificarAsync("produtos", "estoque", new { produto.Id, produto.Nome, produto.Estoque });
            return OkApi(new { produto.Id, produto.Nome, produto.Estoque, produto.EstoqueMinimo, produto.ControlaEstoque, message = "Estoque atualizado." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    public record EstoqueRequest(string? Token, int Estoque, int EstoqueMinimo, bool ControlaEstoque);

    [HttpGet("entradas-saidas")]
    public async Task<IActionResult> Lancamentos([FromQuery] string token = "", [FromQuery] string? de = null, [FromQuery] string? ate = null)
    {
        try
        {
            await permissoes.ExigirAsync(token, ModulosAdmin.Pagamentos, AcaoPermissao.Visualizar);
            var linhas = await db.EntradasESaidas.AsNoTracking().ToListAsync();
            return OkApi(linhas
                .Where(e => string.IsNullOrEmpty(de) && string.IsNullOrEmpty(ate) || Normalizador.Dentro(Normalizador.Data(e.Data), de, ate))
                .OrderByDescending(e => e.Data)
                .Select(e => new { e.Id, e.Data, e.Descricao, e.Tipo, e.Valor, e.Unidade, e.Origem }));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpGet("pacotes")]
    public async Task<IActionResult> Pacotes([FromQuery] string token = "")
    {
        try
        {
            await permissoes.ExigirAsync(token, ModulosAdmin.Pets, AcaoPermissao.Visualizar);
            return OkApi(await db.Pacotes.AsNoTracking().OrderByDescending(p => p.DataInicio).ToListAsync());
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>Pedidos da loja com o nome do cliente resolvido (o admin nao tinha essa tela).</summary>
    [HttpGet("pedidos")]
    public async Task<IActionResult> Pedidos([FromQuery] string token = "")
    {
        try
        {
            await permissoes.ExigirAsync(token, ModulosAdmin.Pedidos, AcaoPermissao.Visualizar);
            var pedidos = await db.Pedidos.AsNoTracking().OrderByDescending(p => p.CriadoEm).ToListAsync();
            var clientes = await db.Clientes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c);
            return OkApi(pedidos.Select(p => new
            {
                p.Id,
                p.ClienteId,
                cliente = clientes.TryGetValue(p.ClienteId, out var c) ? c.Nome : "(cliente removido)",
                telefone = clientes.TryGetValue(p.ClienteId, out var c2) ? c2.Telefone : "",
                p.ProdutoId,
                p.ProdutoNome,
                p.Quantidade,
                p.Total,
                p.FormaPagamento,
                p.Status,
                criadoEm = p.CriadoEm.ToString("O")
            }));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // -----------------------------------------------------------------------
    // IMPORTACAO DO QUE SO EXISTIA NO NAVEGADOR
    // -----------------------------------------------------------------------

    /// <summary>
    /// Recebe o conteudo do localStorage do painel e grava no banco apenas o
    /// que ainda nao existe la. Nada e sobrescrito e nada e apagado: registros
    /// ja presentes sao contados como "ja existiam".
    /// Com simular = true nada e gravado — serve para conferir antes.
    /// </summary>
    [HttpPost("importar")]
    public async Task<IActionResult> Importar([FromBody] JsonElement corpo)
    {
        try
        {
            // A importacao grava em varias colecoes de uma vez, entao so quem
            // tem Configurações pode dispara-la.
            await permissoes.ExigirAsync(Texto(corpo, "token"), ModulosAdmin.Configuracoes, AcaoPermissao.Criar);
            var simular = corpo.TryGetProperty("simular", out var s) && s.ValueKind == JsonValueKind.True;
            var relatorio = new Dictionary<string, object>();

            relatorio["servicos"] = await ImportarServicos(Lista(corpo, "servicos"), simular);
            relatorio["produtos"] = await ImportarProdutos(Lista(corpo, "produtos"), simular);
            relatorio["pets"] = await ImportarPets(Lista(corpo, "pets"), simular);
            relatorio["agendamentos"] = await ImportarAgendamentos(Lista(corpo, "agendamentos"), simular);
            relatorio["entradasESaidas"] = await ImportarLancamentos(Lista(corpo, "entradasESaidas"), simular);

            if (!simular)
            {
                await db.SaveChangesAsync();
                await realtime.NotificarAsync("importacao", "concluida", relatorio);
            }

            return OkApi(new { simulacao = simular, relatorio, message = simular ? "Simulacao concluida: nada foi gravado." : "Importacao concluida." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    private sealed record ResultadoImportacao(int Novos, int JaExistiam, int Ignorados);

    private async Task<ResultadoImportacao> ImportarServicos(List<JsonElement> linhas, bool simular)
    {
        var existentes = (await db.Servicos.AsNoTracking().ToListAsync())
            .Select(x => Chave(x.Nome, x.Porte)).ToHashSet();
        int novos = 0, repetidos = 0, ignorados = 0;
        foreach (var linha in linhas)
        {
            var nome = Texto(linha, "nome");
            if (nome.Length == 0) { ignorados++; continue; }
            var porte = Texto(linha, "porte");
            if (!existentes.Add(Chave(nome, porte))) { repetidos++; continue; }
            novos++;
            if (simular) continue;
            db.Servicos.Add(new Servico
            {
                Id = NovoId("SRV"),
                Nome = nome,
                Preco = Numero(linha, "preco"),
                Porte = porte,
                AdicionaisJson = Bruto(linha, "adicionais") ?? "[]",
                Pacote = Booleano(linha, "pacote") ? "Sim" : "",
                Adicional = Texto(linha, "adicional")
            });
        }
        return new ResultadoImportacao(novos, repetidos, ignorados);
    }

    private async Task<ResultadoImportacao> ImportarProdutos(List<JsonElement> linhas, bool simular)
    {
        var existentes = (await db.Produtos.AsNoTracking().ToListAsync())
            .Select(x => Chave(x.Codigo)).ToHashSet();
        int novos = 0, repetidos = 0, ignorados = 0;
        foreach (var linha in linhas)
        {
            var codigo = Texto(linha, "codigo");
            var nome = Texto(linha, "nome");
            if (codigo.Length == 0 && nome.Length == 0) { ignorados++; continue; }
            if (!existentes.Add(Chave(codigo.Length > 0 ? codigo : nome))) { repetidos++; continue; }
            novos++;
            if (simular) continue;
            db.Produtos.Add(new Produto
            {
                Id = NovoId("PRD"),
                Codigo = codigo,
                Nome = nome,
                Categoria = Texto(linha, "categoria"),
                ValorCompra = Numero(linha, "valorCompra"),
                ValorVenda = Numero(linha, "valorVenda"),
                Estoque = (int)Numero(linha, "estoque"),
                EstoqueMinimo = (int)Numero(linha, "estoqueMinimo"),
                ControlaEstoque = Numero(linha, "estoque") > 0
            });
        }
        return new ResultadoImportacao(novos, repetidos, ignorados);
    }

    private async Task<ResultadoImportacao> ImportarPets(List<JsonElement> linhas, bool simular)
    {
        var existentes = (await db.Pets.AsNoTracking().ToListAsync())
            .Select(x => Chave(x.Dono, x.PetNome)).ToHashSet();
        var clientes = await db.Clientes.ToListAsync();
        int novos = 0, repetidos = 0, ignorados = 0;
        foreach (var linha in linhas)
        {
            var dono = Texto(linha, "dono");
            var pet = Texto(linha, "pet");
            if (pet.Length == 0) { ignorados++; continue; }
            if (!existentes.Add(Chave(dono, pet))) { repetidos++; continue; }
            novos++;
            if (simular) continue;

            var telefone = Texto(linha, "telefone");
            var endereco = Texto(linha, "endereco");
            var cliente = clientes.FirstOrDefault(c => Normalizador.Texto(c.Nome) == Normalizador.Texto(dono));
            if (cliente is null && dono.Length > 0)
            {
                cliente = new Cliente { Id = NovoId("CLI"), Nome = dono, Telefone = telefone, Endereco = endereco, Origem = "importacao_painel", Status = "ativo" };
                db.Clientes.Add(cliente);
                clientes.Add(cliente);
            }

            db.Pets.Add(new Pet
            {
                Id = NovoId("PET"),
                ClienteId = cliente?.Id ?? "",
                Dono = dono,
                PetNome = pet,
                Tipo = Texto(linha, "tipo"),
                Raca = Texto(linha, "raca"),
                Telefone = telefone,
                Endereco = endereco,
                PacoteJson = Bruto(linha, "pacote") ?? "",
                Unidade = Texto(linha, "unidade")
            });
        }
        return new ResultadoImportacao(novos, repetidos, ignorados);
    }

    private async Task<ResultadoImportacao> ImportarAgendamentos(List<JsonElement> linhas, bool simular)
    {
        var existentes = (await db.Agendamentos.AsNoTracking().ToListAsync())
            .Select(x => Chave(x.Dono, x.Pet, Normalizador.Data(x.DataHora), (x.DataHora ?? "").Length >= 16 ? x.DataHora[11..16] : "")).ToHashSet();
        int novos = 0, repetidos = 0, ignorados = 0;
        foreach (var linha in linhas)
        {
            var dataHora = Texto(linha, "dataHora");
            var pet = Texto(linha, "pet");
            if (dataHora.Length == 0 || pet.Length == 0) { ignorados++; continue; }
            var dono = Texto(linha, "dono");
            if (!existentes.Add(Chave(dono, pet, Normalizador.Data(dataHora), dataHora.Length >= 16 ? dataHora[11..16] : ""))) { repetidos++; continue; }
            novos++;
            if (simular) continue;
            db.Agendamentos.Add(new Agendamento
            {
                Id = NovoId("AGD"),
                Pet = pet,
                Dono = dono,
                Telefone = Texto(linha, "telefone"),
                DataHora = dataHora,
                ServicosJson = Bruto(linha, "servicos") ?? "[]",
                Total = Numero(linha, "total"),
                Transporte = Texto(linha, "transporte"),
                ValorTransporte = Numero(linha, "valorTransporte"),
                Status = Texto(linha, "status") is { Length: > 0 } st ? st : "Pendente",
                PagamentoStatus = Texto(linha, "pagamentoStatus"),
                FormaPagamento = Texto(linha, "formaPagamento"),
                Obs = Texto(linha, "obs"),
                Unidade = Texto(linha, "unidade")
            });
        }
        return new ResultadoImportacao(novos, repetidos, ignorados);
    }

    private async Task<ResultadoImportacao> ImportarLancamentos(List<JsonElement> linhas, bool simular)
    {
        var existentes = (await db.EntradasESaidas.AsNoTracking().ToListAsync())
            .Select(x => Chave(Normalizador.Data(x.Data), x.Descricao, x.Tipo, x.Valor.ToString("0.00"))).ToHashSet();
        int novos = 0, repetidos = 0, ignorados = 0;
        foreach (var linha in linhas)
        {
            var data = Texto(linha, "data");
            var valor = Numero(linha, "valor");
            if (data.Length == 0) { ignorados++; continue; }
            var descricao = Texto(linha, "descricao");
            var tipo = Texto(linha, "tipo");
            if (!existentes.Add(Chave(Normalizador.Data(data), descricao, tipo, valor.ToString("0.00")))) { repetidos++; continue; }
            novos++;
            if (simular) continue;
            db.EntradasESaidas.Add(new EntradaSaida
            {
                Id = NovoId("FIN"),
                Data = data,
                Descricao = descricao,
                Tipo = tipo,
                Valor = valor,
                Unidade = Texto(linha, "unidade"),
                Origem = "importacao_painel"
            });
        }
        return new ResultadoImportacao(novos, repetidos, ignorados);
    }

    // -----------------------------------------------------------------------
    // AUXILIARES DE LEITURA DO JSON (o localStorage guarda numeros como texto
    // em alguns registros antigos, entao cada campo e lido com tolerancia)
    // -----------------------------------------------------------------------

    private static string NovoId(string prefixo) => prefixo + "-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    private static string Chave(params string?[] partes) => string.Join("|", partes.Select(Normalizador.Texto));

    private static List<JsonElement> Lista(JsonElement corpo, string nome)
        => corpo.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.Array
            ? valor.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList()
            : new List<JsonElement>();

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
        => objeto.TryGetProperty(nome, out var valor) && (valor.ValueKind == JsonValueKind.True || (valor.ValueKind == JsonValueKind.String && Normalizador.Texto(valor.GetString()) is "sim" or "true"));

    private static string? Bruto(JsonElement objeto, string nome)
        => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind is JsonValueKind.Array or JsonValueKind.Object
            ? valor.GetRawText()
            : null;
}
