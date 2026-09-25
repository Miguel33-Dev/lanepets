using System.Text.Json;
using LanePets.Data;
using LanePets.DTOs;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static LanePets.Services.IndicadoresFinanceiros;

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
public class AdminSyncController(LanePetsDbContext db, PermissaoService permissoes, RealtimeNotifier realtime, EventosService eventos) : ApiControllerBase
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

            // Funcionario (item 1) so enxerga a agenda da propria unidade.
            var agendamentos = (await db.Agendamentos.AsNoTracking().ToListAsync())
                .Where(a => contexto.VeUnidade(a.Unidade)).ToList();
            List<EntradaSaida> lancamentos = podeFinanceiro ? await db.EntradasESaidas.AsNoTracking().ToListAsync() : new();
            var pedidos = (await db.Pedidos.AsNoTracking().ToListAsync()).Where(p => contexto.VeUnidade(p.Unidade)).ToList();
            var produtos = await db.Produtos.AsNoTracking().ToListAsync();
            var servicosCadastrados = await db.Servicos.AsNoTracking().ToListAsync();

            var filtro = (unidade ?? "todas").Trim().ToLowerInvariant();
            var periodo = Calcular(agendamentos, lancamentos, pedidos, de, ate, filtro);

            // Item 9: pagamentos lidos da ENTIDADE Pagamento (item 8), nao do
            // campo espelho do agendamento. So para quem pode ver Pagamentos;
            // sem a permissao o dado nem sai do banco.
            var pagamentos = podeFinanceiro
                ? (await db.Pagamentos.AsNoTracking().ToListAsync())
                    .Where(p => contexto.VeUnidade(p.Unidade) && NaUnidade(p.Unidade, filtro)).ToList()
                : new List<Pagamento>();
            var pagamentosPendentes = pagamentos.Where(p => p.Status == PagamentosService.Pendente).ToList();
            var reembolsosPendentes = pagamentos.Count(p => p.ReembolsoPendente);
            var (deAnterior, ateAnterior) = PeriodoAnterior(de, ate);
            var anterior = Calcular(agendamentos, lancamentos, pedidos, deAnterior, ateAnterior, filtro);

            var porUnidade = IdsPorUnidade().Select(u =>
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
            if (pagamentosPendentes.Count > 0) alertas.Add($"{pagamentosPendentes.Count} pagamento(s) pendente(s), somando {pagamentosPendentes.Sum(p => p.Valor):C}.");
            if (reembolsosPendentes > 0) alertas.Add($"{reembolsosPendentes} reembolso(s) pendente(s) em Financeiro > Pagamentos.");
            if (podeFinanceiro && periodo.Resultado < 0) alertas.Add("O resultado do periodo esta negativo. Revise despesas e recebimentos.");
            if (agendamentos.Any(a => string.IsNullOrWhiteSpace(Normalizador.Unidade(a.Unidade)))) alertas.Add("Ha agendamentos sem unidade definida; eles aparecem como \"Sem unidade / antigos\".");
            var semEstoque = produtos.Where(p => p.ControlaEstoque && p.Estoque <= p.EstoqueMinimo).OrderBy(p => p.Estoque).ToList();
            if (semEstoque.Count > 0) alertas.Add($"{semEstoque.Count} produto(s) no estoque minimo ou abaixo dele.");
            var avaliacoesPendentes = await db.Depoimentos.CountAsync(d => d.Status == "Pendente");
            if (avaliacoesPendentes > 0) alertas.Add($"{avaliacoesPendentes} avaliacao(oes) aguardando moderacao.");
            var segurosPendentes = await db.SolicitacoesSeguro.CountAsync(s => s.Status == "Pendente");
            if (segurosPendentes > 0) alertas.Add($"{segurosPendentes} solicitacao(oes) de seguro aguardando contato.");
            if (alertas.Count == 0) alertas.Add("Nenhum alerta importante para o periodo selecionado.");

            var totalClientes = await db.Clientes.CountAsync();
            var totalPets = await db.Pets.CountAsync();

            // Item 9: agenda de HOJE (unidade do filtro, sem cancelados).
            var hojeDia = DateTime.Now.ToString("yyyy-MM-dd");
            var agendaHoje = agendamentos
                .Where(a => Normalizador.Data(a.DataHora) == hojeDia && NaUnidade(a.Unidade, filtro)
                         && StatusAgendamento.Exibir(a.Status) != StatusAgendamento.Cancelado)
                .ToList();

            // Item 9: servicos mais realizados no periodo, por frequencia (o valor
            // de cada servico nao fica gravado separado, entao nao ha receita por servico).
            var nomesServicos = servicosCadastrados.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Nome);
            var contagemServicos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in periodo.Atendimentos)
                foreach (var nome in NomesDosServicos(a.ServicosJson, nomesServicos))
                    contagemServicos[nome] = contagemServicos.TryGetValue(nome, out var n) ? n + 1 : 1;
            var topServicos = contagemServicos.Select(kv => new { servico = kv.Key, quantidade = kv.Value })
                .OrderByDescending(s => s.quantidade).ThenBy(s => s.servico).Take(5).ToList();

            // Sem permissao de Pagamentos, nenhum valor em dinheiro sai daqui
            // (regra 6.31 do CONTEXTO). A tela mostra "Restrito".
            decimal M(decimal valor) => podeFinanceiro ? valor : 0m;

            return OkApi(new
            {
                periodo = new { de, ate, unidade = filtro },
                restrito = !podeFinanceiro,
                // Item 9: os 8 indicadores principais do topo do dashboard.
                indicadores = new
                {
                    clientes = totalClientes,
                    pets = totalPets,
                    agendamentosHoje = agendaHoje.Count,
                    agendaHoje = new
                    {
                        solicitados = agendaHoje.Count(a => StatusAgendamento.Exibir(a.Status) == StatusAgendamento.Solicitado),
                        confirmados = agendaHoje.Count(a => StatusAgendamento.Exibir(a.Status) == StatusAgendamento.Confirmado),
                        emAndamento = agendaHoje.Count(a => StatusAgendamento.Exibir(a.Status) == StatusAgendamento.EmAndamento),
                        concluidos = agendaHoje.Count(a => StatusAgendamento.Exibir(a.Status) == StatusAgendamento.Concluido)
                    },
                    agendamentosPeriodo = periodo.Atendimentos.Count,
                    faturamento = podeFinanceiro ? periodo.Receita : (decimal?)null,
                    estoqueBaixo = semEstoque.Count,
                    semEstoque = semEstoque.Count(p => p.Estoque <= 0),
                    pagamentosPendentes = podeFinanceiro ? pagamentosPendentes.Count : (int?)null,
                    valorPendente = podeFinanceiro ? pagamentosPendentes.Sum(p => p.Valor) : (decimal?)null,
                    reembolsosPendentes = podeFinanceiro ? reembolsosPendentes : (int?)null,
                    servicosMaisRealizados = topServicos
                },
                totais = new
                {
                    clientes = totalClientes,
                    pets = totalPets,
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
                    receita = M(periodo.Receita),
                    despesas = M(periodo.Despesas),
                    resultado = M(periodo.Resultado),
                    margem = M(periodo.Margem),
                    atendimentos = periodo.Atendimentos.Count,
                    ticketMedio = M(periodo.Ticket)
                },
                anterior = new
                {
                    de = deAnterior,
                    ate = ateAnterior,
                    receita = M(anterior.Receita),
                    despesas = M(anterior.Despesas),
                    resultado = M(anterior.Resultado),
                    atendimentos = anterior.Atendimentos.Count
                },
                operacional = new
                {
                    atendimentos = periodo.Atendimentos.Count,
                    pagos = periodo.PagamentosPagos,
                    pendentes = periodo.PagamentosPendentes,
                    transporteFaturado = M(periodo.Transporte),
                    cancelados = periodo.Cancelados,
                    pedidosDoPeriodo = periodo.Pedidos.Count,
                    receitaDePedidos = M(periodo.ReceitaPedidos)
                },
                categorias = podeFinanceiro
                    ? periodo.Categorias.Where(c => c.Value > 0).OrderByDescending(c => c.Value).Select(c => new { nome = c.Key, valor = c.Value }).ToList()
                    : new(),
                porUnidade = podeFinanceiro ? porUnidade : new(),
                mensal = podeFinanceiro ? SerieMensal(agendamentos, lancamentos, pedidos, filtro) : Array.Empty<object>(),
                estoqueBaixoTotal = semEstoque.Count,
                estoqueBaixo = semEstoque.Take(10).Select(p => new { p.Id, p.Codigo, p.Nome, p.Estoque, p.EstoqueMinimo, situacao = EstoqueService.Situacao(p), p.VisivelLoja }),
                alertas,
                atualizadoEm = DateTime.UtcNow.ToString("O")
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // -----------------------------------------------------------------------
    // RELATORIO FINANCEIRO — visao analitica dedicada, reaproveitando o
    // mesmo Calcular() do resumo. Nao duplica calculo nenhum: so acrescenta
    // as agregacoes que faltavam (formas de pagamento, serie adaptada ao
    // periodo, pedidos/pagamentos detalhados, top produtos e servicos).
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visao analitica do Relatorio Financeiro (ADM > Financeiro > Relatorio).
    ///
    /// Item 11.2 (26/09):
    ///   - Blocos de PAGAMENTO (resumo, formas de pagamento, "valor efetivamente recebido" dos pedidos)
    ///     leem a entidade Pagamento (item 8), nao mais o campo espelho Agendamento.PagamentoStatus.
    ///     Receita continua sendo por competencia (atendimento/pedido do periodo), igual ao Dashboard.
    ///   - Sem pagamentos:visualizar nenhum valor em dinheiro sai daqui (regra 6.31): os campos de
    ///     dinheiro vem null e as listas de dinheiro vem vazias. Antes so os lancamentos eram cortados
    ///     e a receita de agendamentos/pedidos ia para o navegador.
    ///   - Funcionario (se um dia tiver o modulo) so enxerga a propria unidade.
    /// </summary>
    [HttpGet("relatorio")]
    public async Task<IActionResult> Relatorio([FromQuery] string token = "", [FromQuery] string? de = null, [FromQuery] string? ate = null, [FromQuery] string? unidade = "todas")
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(token, ModulosAdmin.Relatorios, AcaoPermissao.Visualizar);
            var podeFinanceiro = contexto.Pode(ModulosAdmin.Pagamentos, AcaoPermissao.Visualizar);

            var agendamentos = (await db.Agendamentos.AsNoTracking().ToListAsync()).Where(a => contexto.VeUnidade(a.Unidade)).ToList();
            List<EntradaSaida> lancamentos = podeFinanceiro ? await db.EntradasESaidas.AsNoTracking().ToListAsync() : new();
            var pedidos = (await db.Pedidos.AsNoTracking().ToListAsync()).Where(p => contexto.VeUnidade(p.Unidade)).ToList();
            var servicosCadastrados = await db.Servicos.AsNoTracking().ToListAsync();

            var filtro = (unidade ?? "todas").Trim().ToLowerInvariant();
            var periodo = Calcular(agendamentos, lancamentos, pedidos, de, ate, filtro);
            var (deAnterior, ateAnterior) = PeriodoAnterior(de, ate);
            var anterior = (deAnterior != null && ateAnterior != null) ? Calcular(agendamentos, lancamentos, pedidos, deAnterior, ateAnterior, filtro) : null;

            // Pagamentos do periodo (entidade), mesmo filtro de unidade dos outros blocos.
            // Sem a permissao de Pagamentos o dado nem sai do banco.
            var pagamentos = podeFinanceiro
                ? (await db.Pagamentos.AsNoTracking().ToListAsync())
                    .Where(p => contexto.VeUnidade(p.Unidade) && NaUnidade(p.Unidade, filtro)
                             && Normalizador.Dentro(Normalizador.Data(p.DataReferencia), de, ate))
                    .ToList()
                : new List<Pagamento>();
            var aprovados = pagamentos.Where(p => p.Status == PagamentosService.Aprovado).ToList();

            decimal? M(decimal valor) => podeFinanceiro ? valor : null;
            decimal? Variacao(decimal atual, decimal baseAnterior) => baseAnterior == 0 ? null : Math.Round((atual - baseAnterior) / Math.Abs(baseAnterior) * 100, 1);

            var porUnidade = IdsPorUnidade().Select(u =>
            {
                var d = Calcular(agendamentos, lancamentos, pedidos, de, ate, u);
                return new
                {
                    id = u,
                    nome = NomeUnidade(u),
                    receita = M(d.Receita),
                    despesas = M(d.Despesas),
                    saldo = M(d.Resultado),
                    pedidos = d.Pedidos.Count,
                    atendimentos = d.Atendimentos.Count,
                    temMovimento = d.Receita != 0 || d.Despesas != 0 || d.Pedidos.Count != 0 || d.Atendimentos.Count != 0
                };
            }).Where(u => u.temMovimento).ToList();

            // Pedidos do periodo, incluindo cancelados (necessario para o resumo de status).
            var pedidosPeriodoTodos = pedidos.Where(p => Normalizador.Dentro(p.CriadoEm.ToString("yyyy-MM-dd"), de, ate) && NaUnidade(p.Unidade, filtro)).ToList();
            var pedidosResumo = new
            {
                total = pedidosPeriodoTodos.Count,
                pendentes = pedidosPeriodoTodos.Count(p => string.Equals(p.Status, "Pendente", StringComparison.OrdinalIgnoreCase)),
                confirmados = pedidosPeriodoTodos.Count(p => string.Equals(p.Status, "Confirmado", StringComparison.OrdinalIgnoreCase)),
                entregues = pedidosPeriodoTodos.Count(p => string.Equals(p.Status, "Entregue", StringComparison.OrdinalIgnoreCase)),
                cancelados = pedidosPeriodoTodos.Count(p => string.Equals(p.Status, "Cancelado", StringComparison.OrdinalIgnoreCase)),
                valorTotalPedidos = M(pedidosPeriodoTodos.Where(p => !string.Equals(p.Status, "Cancelado", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Total)),
                // Antes: soma dos pedidos nao cancelados. Agora: o que foi de fato aprovado no pagamento do pedido.
                valorRecebido = M(aprovados.Where(p => p.Origem == PagamentosService.OrigemPedido).Sum(p => p.Valor))
            };

            // Pagamentos (entidade): contagem por status + recebido e a receber, de todas as origens
            // (servicos, pedidos da loja e Seguro Pet).
            object? pagamentosResumo = podeFinanceiro ? new
            {
                pagos = aprovados.Count,
                pendentes = pagamentos.Count(p => p.Status == PagamentosService.Pendente),
                recusados = pagamentos.Count(p => p.Status == PagamentosService.Recusado),
                cancelados = pagamentos.Count(p => p.Status == PagamentosService.Cancelado),
                reembolsados = pagamentos.Count(p => p.Status == PagamentosService.Reembolsado),
                reembolsosPendentes = pagamentos.Count(p => p.ReembolsoPendente),
                totalRecebido = aprovados.Sum(p => p.Valor),
                totalPendente = pagamentos.Where(p => p.Status == PagamentosService.Pendente).Sum(p => p.Valor)
            } : null;

            // Formas de pagamento: pagamentos APROVADOS do periodo, agrupados pela forma registrada.
            var formas = aprovados
                .Where(p => !string.IsNullOrWhiteSpace(p.Forma) && !string.Equals(p.Forma.Trim(), "A combinar", StringComparison.OrdinalIgnoreCase))
                .GroupBy(p => p.Forma.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => (Forma: g.Key, Quantidade: g.Count(), Valor: g.Sum(p => p.Valor)))
                .ToList();
            var totalFormas = formas.Sum(f => f.Valor);
            var formasPagamento = formas.Select(f => new
            {
                forma = f.Forma,
                quantidade = f.Quantidade,
                valor = f.Valor,
                percentual = totalFormas == 0 ? 0 : Math.Round(f.Valor / totalFormas * 100, 1)
            }).OrderByDescending(f => f.valor).ToList();

            // Top produtos vendidos (pedidos nao cancelados do periodo) — e ranking por faturamento,
            // entao so vai para quem pode ver dinheiro.
            var topProdutos = podeFinanceiro
                ? periodo.Pedidos
                    .GroupBy(p => string.IsNullOrWhiteSpace(p.ProdutoNome) ? "(sem nome)" : p.ProdutoNome)
                    .Select(g => new { produto = g.Key, quantidade = g.Sum(p => p.Quantidade), receita = g.Sum(p => p.Total) })
                    .OrderByDescending(g => g.receita).Take(5).ToList()
                : new();

            // Top servicos realizados por frequencia — o valor de cada servico
            // dentro do agendamento nao fica gravado individualmente (so o
            // total do agendamento), entao nao inventamos receita por servico.
            var nomesServicos = servicosCadastrados.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Nome);
            var contagemServicos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in periodo.Atendimentos)
                foreach (var nome in NomesDosServicos(a.ServicosJson, nomesServicos))
                    contagemServicos[nome] = contagemServicos.TryGetValue(nome, out var n) ? n + 1 : 1;
            var topServicos = contagemServicos.Select(kv => new { servico = kv.Key, quantidade = kv.Value })
                .OrderByDescending(s => s.quantidade).Take(5).ToList();

            return OkApi(new
            {
                periodo = new { de, ate, unidade = filtro },
                restrito = !podeFinanceiro,
                cards = new
                {
                    entradas = M(periodo.Receita),
                    saidas = M(periodo.Despesas),
                    saldo = M(periodo.Resultado),
                    totalPagamentos = periodo.Atendimentos.Count + periodo.Pedidos.Count,
                    ticketMedio = M(periodo.Ticket)
                },
                variacao = (!podeFinanceiro || anterior is null) ? null : new
                {
                    entradas = Variacao(periodo.Receita, anterior.Receita),
                    saidas = Variacao(periodo.Despesas, anterior.Despesas),
                    saldo = Variacao(periodo.Resultado, anterior.Resultado)
                },
                categorias = podeFinanceiro
                    ? periodo.Categorias.Where(c => c.Value > 0).OrderByDescending(c => c.Value).Select(c => (object)new { nome = c.Key, valor = c.Value }).ToList()
                    : new List<object>(),
                formasPagamento,
                porUnidade,
                pedidosResumo,
                pagamentosResumo,
                topProdutos,
                topServicos,
                serie = podeFinanceiro ? SeriePeriodo(agendamentos, lancamentos, pedidos, de, ate, filtro) : new List<object>(),
                atualizadoEm = DateTime.UtcNow.ToString("O")
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

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
            return OkApi(produtos.Select(p => new { p.Id, p.Codigo, p.Nome, p.Categoria, p.ValorCompra, p.ValorVenda, p.Estoque, p.EstoqueMinimo, p.ControlaEstoque, p.VisivelLoja, situacaoEstoque = EstoqueService.Situacao(p), margem = p.ValorCompra == 0 ? 0 : Math.Round((p.ValorVenda - p.ValorCompra) / p.ValorCompra * 100, 1) }));
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
            var contextoEstoque = await permissoes.ExigirAsync(request.Token ?? "", ModulosAdmin.Produtos, AcaoPermissao.Editar);
            var produto = await db.Produtos.FindAsync(id) ?? throw new Exception("Produto nao encontrado.");
            var estoqueAnterior = produto.Estoque;
            if (request.Estoque < 0 || request.EstoqueMinimo < 0) throw new Exception("O estoque não pode ser negativo.");
            produto.EstoqueMinimo = request.EstoqueMinimo;
            produto.ControlaEstoque = request.ControlaEstoque;
            // Item 7: o saldo novo entra pelo livro como "ajuste".
            var alerta = EstoqueService.AjustarPara(db, produto, request.Estoque,
                string.IsNullOrWhiteSpace(request.Motivo) ? "Ajuste no painel" : request.Motivo!,
                contextoEstoque.Usuario.Id, contextoEstoque.Usuario.Email);
            if (!produto.ControlaEstoque) produto.Estoque = request.Estoque;
            await db.SaveChangesAsync();
            if (alerta is not null) await AlertarEstoqueAsync(alerta);
            await realtime.NotificarAsync("produtos", "estoque", new { produto.Id, produto.Nome, produto.Estoque });
            await eventos.RegistrarAsync(new("produto", "Estoque ajustado", "info", "admin", contextoEstoque.Usuario.Id, contextoEstoque.Usuario.Email,
                produto.Id, $"{produto.Nome}: {estoqueAnterior} → {produto.Estoque} (mínimo {produto.EstoqueMinimo})."), HttpContext);
            return OkApi(new { produto.Id, produto.Nome, produto.Estoque, produto.EstoqueMinimo, produto.ControlaEstoque, message = "Estoque atualizado." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    public record EstoqueRequest(string? Token, int Estoque, int EstoqueMinimo, bool ControlaEstoque, string? Motivo = null);

    // -----------------------------------------------------------------------
    // ITEM 7 — LIVRO DE ESTOQUE
    // -----------------------------------------------------------------------

    /// <summary>Entrada, saida ou ajuste manual de estoque (Produtos > Editar).</summary>
    [HttpPost("produtos/{id}/movimentacao")]
    public async Task<IActionResult> Movimentar(string id, [FromBody] MovimentacaoRequest request)
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(request.Token ?? "", ModulosAdmin.Produtos, AcaoPermissao.Editar);
            var produto = await db.Produtos.FindAsync(id) ?? throw new Exception("Produto não encontrado.");
            if (!produto.ControlaEstoque) throw new Exception("Ligue o controle de estoque deste produto antes de lançar movimentações.");
            var tipo = Normalizador.Texto(request.Tipo);
            if (!EstoqueService.TiposManuais.Contains(tipo)) throw new Exception("Tipo de movimentação inválido. Use entrada, saída ou ajuste.");
            var motivo = (request.Motivo ?? "").Trim();
            if (motivo.Length < 3) throw new Exception("Informe o motivo da movimentação (ex.: compra do fornecedor, avaria, inventário).");
            if (motivo.Length > 200) throw new Exception("O motivo pode ter no máximo 200 caracteres.");
            var antes = produto.Estoque;
            EstoqueService.Alerta? alerta;
            if (tipo == EstoqueService.Ajuste)
            {
                if (request.Quantidade < 0) throw new Exception("No ajuste, informe o saldo correto (0 ou mais).");
                if (request.Quantidade == produto.Estoque) throw new Exception($"O saldo já é {produto.Estoque}.");
                alerta = EstoqueService.AjustarPara(db, produto, request.Quantidade, motivo, contexto.Usuario.Id, contexto.Usuario.Email);
            }
            else
            {
                if (request.Quantidade < 1 || request.Quantidade > 100000) throw new Exception("Informe uma quantidade entre 1 e 100000.");
                alerta = EstoqueService.Movimentar(db, produto, tipo == EstoqueService.Entrada ? request.Quantidade : -request.Quantidade,
                    tipo, motivo, contexto.Usuario.Id, contexto.Usuario.Email);
            }
            await db.SaveChangesAsync();
            await realtime.NotificarAsync("produtos", "estoque", new { produto.Id, produto.Nome, produto.Estoque });
            await eventos.RegistrarAsync(new("estoque", $"Estoque: {EstoqueService.RotuloTipo(tipo).ToLowerInvariant()}", "info", "admin",
                contexto.Usuario.Id, contexto.Usuario.Email, produto.Id, $"{produto.Nome}: {antes} → {produto.Estoque} · {motivo}"), HttpContext);
            if (alerta is not null) await AlertarEstoqueAsync(alerta);
            return OkApi(new { produto.Id, produto.Nome, produto.Estoque, produto.EstoqueMinimo, situacaoEstoque = EstoqueService.Situacao(produto), message = "Movimentação registrada." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>Historico de movimentacoes (de um produto ou de todos), mais recentes primeiro.</summary>
    [HttpGet("estoque/movimentacoes")]
    public async Task<IActionResult> Movimentacoes([FromQuery] string token = "", [FromQuery] string? produtoId = null, [FromQuery] int limite = 50)
    {
        try
        {
            await permissoes.ExigirAsync(token, ModulosAdmin.Produtos, AcaoPermissao.Visualizar);
            limite = Math.Clamp(limite, 1, 500);
            var consulta = db.MovimentacoesEstoque.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(produtoId)) consulta = consulta.Where(m => m.ProdutoId == produtoId);
            var lista = (await consulta.ToListAsync()).OrderByDescending(m => m.DataHora).Take(limite);
            return OkApi(lista.Select(m => new
            {
                m.Id, dataHora = DateTime.SpecifyKind(m.DataHora, DateTimeKind.Utc).ToString("O"), m.ProdutoId, m.ProdutoNome,
                m.Tipo, tipoRotulo = EstoqueService.RotuloTipo(m.Tipo), m.Quantidade, m.SaldoAnterior, m.SaldoNovo,
                m.Motivo, m.PedidoId, m.Origem, m.Autor
            }));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>Muda o status de um pedido. Cancelar devolve o estoque baixado.</summary>
    [HttpPost("pedidos/{id}/status")]
    public async Task<IActionResult> StatusPedido(string id, [FromBody] StatusPedidoRequest request)
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(request.Token ?? "", ModulosAdmin.Pedidos, AcaoPermissao.Editar);
            var pedido = await db.Pedidos.FirstOrDefaultAsync(p => p.Id == id) ?? throw new Exception("Pedido não encontrado.");
            if (!contexto.VeUnidade(pedido.Unidade)) throw new AcessoNegadoException("Este pedido é de outra unidade.");
            var antes = PedidosService.Exibir(pedido.Status);
            var devolvido = await PedidosService.AlterarStatusAsync(db, pedido, request.Status, contexto.Usuario.Id, contexto.Usuario.Email, "admin");
            await db.SaveChangesAsync();
            await PagamentosService.ReconciliarAsync(db);   // item 8
            await realtime.NotificarAsync("pedidos", "status", new { pedido.Id, pedido.Status });
            await eventos.RegistrarAsync(new("pedido", pedido.Status == PedidosService.Cancelado ? "Pedido cancelado" : "Status do pedido alterado",
                pedido.Status == PedidosService.Cancelado ? "aviso" : "info", "admin", contexto.Usuario.Id, contexto.Usuario.Email, pedido.Id,
                $"{antes} → {pedido.Status} · {pedido.Quantidade}× {pedido.ProdutoNome}" + (devolvido > 0 ? $" · {devolvido} unidade(s) devolvida(s) ao estoque" : "")), HttpContext);
            return OkApi(new
            {
                pedido.Id, pedido.Status, devolvido,
                message = pedido.Status == PedidosService.Cancelado
                    ? devolvido > 0 ? $"Pedido cancelado. {devolvido} unidade(s) voltaram ao estoque." : "Pedido cancelado. Não havia baixa de estoque para devolver."
                    : $"Pedido marcado como {pedido.Status}."
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    private Task AlertarEstoqueAsync(EstoqueService.Alerta a) =>
        eventos.RegistrarAsync(new("estoque", a.Zerado ? "Produto sem estoque" : "Estoque baixo", "aviso", "sistema", "", "",
            a.ProdutoId, $"{a.ProdutoNome}: saldo {a.Saldo} (mínimo {a.Minimo})."), HttpContext);

    public record MovimentacaoRequest(string? Token, string? Tipo, int Quantidade, string? Motivo);
    public record StatusPedidoRequest(string? Token, string? Status);

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
            var contexto = await permissoes.ExigirAsync(token, ModulosAdmin.Pets, AcaoPermissao.Visualizar);
            var pacotes = await db.Pacotes.AsNoTracking().OrderByDescending(p => p.DataInicio).ToListAsync();
            return OkApi(pacotes.Where(p => contexto.VeUnidade(p.Unidade)).ToList());
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>Pedidos da loja com o nome do cliente resolvido (o admin nao tinha essa tela).</summary>
    [HttpGet("pedidos")]
    public async Task<IActionResult> Pedidos([FromQuery] string token = "")
    {
        try
        {
            var contextoPedidos = await permissoes.ExigirAsync(token, ModulosAdmin.Pedidos, AcaoPermissao.Visualizar);
            // Item 5: pedido tem unidade de retirada; funcionario ve so a dele.
            var pedidos = (await db.Pedidos.AsNoTracking().OrderByDescending(p => p.CriadoEm).ToListAsync())
                .Where(p => contextoPedidos.VeUnidade(p.Unidade)).ToList();
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
                unidade = Normalizador.IdUnidade(p.Unidade),
                unidadeNome = Normalizador.NomeUnidade(p.Unidade) is { Length: > 0 } nomeUnidade ? nomeUnidade : "Sem unidade",
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
            var contexto = await permissoes.ExigirAsync(ImportacaoService.Texto(corpo, "token"), ModulosAdmin.Configuracoes, AcaoPermissao.Criar);
            var simular = corpo.TryGetProperty("simular", out var s) && s.ValueKind == JsonValueKind.True;

            // Item 11.3: a regra da importacao mora no ImportacaoService.
            var relatorio = await ImportacaoService.ExecutarAsync(db, corpo, simular);

            if (!simular)
            {
                await realtime.NotificarAsync("importacao", "concluida", relatorio);
                await eventos.RegistrarAsync(new("painel", "Importação de dados do navegador", "info", "admin",
                    contexto.Usuario.Id, contexto.Usuario.Email, "", ImportacaoService.Resumir(relatorio)), HttpContext);
            }

            return OkApi(new ImportacaoResposta(simular, relatorio, simular ? "Simulacao concluida: nada foi gravado." : "Importacao concluida."));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
}
