using LanePets.Tests.Infra;

namespace LanePets.Tests.Operacao;

/// <summary>
/// Pedidos da loja (itens 5, 6, 7 e 8 do roadmap): cliente pede, estoque baixa pelo livro,
/// pagamento nasce pendente; cancelar devolve o estoque e cancela o pagamento.
/// Regras: CONTEXTO §6.28 (Pendente → Confirmado → Entregue + Cancelado final; cliente
/// cancela so Pendente; produto oculto nao aceita pedido) e §6.26 (unidade de retirada).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class PedidosTests(LanePetsApp app)
{
    private Task<int> Estoque(string produtoId)
        => app.NoBanco(db => Task.FromResult(db.Produtos.Single(p => p.Id == produtoId).Estoque));

    private Task<LanePets.Models.Pagamento> PagamentoDe(string origemId)
        => app.NoBanco(db => Task.FromResult(db.Pagamentos.Single(p => p.OrigemId == origemId)));

    private static Task<Resposta> StatusNoPainel(Api api, string token, string pedidoId, string status)
        => api.Post($"/api/admin/pedidos/{pedidoId}/status", new { token, status });

    [Fact]
    public async Task Pedido_baixa_o_estoque_pelo_livro_e_cria_pagamento_pendente()
    {
        var painel = app.Api();
        var produto = await painel.ProdutoNaLoja(await painel.LoginAdmin(), estoque: 10, valorVenda: 25);
        var cliente = app.Api();
        await cliente.CadastrarCliente();

        var r = await cliente.Pedir(produto, 3);

        Assert.Equal(200, r.Codigo);
        Assert.Equal("Pendente", r.Texto("status"));
        Assert.Equal(75m, r.Data.GetProperty("total").GetDecimal());
        Assert.Equal(7, r.Data.GetProperty("estoqueRestante").GetInt32());
        Assert.Equal(Cenarios.FrancoId, r.Texto("unidade"));
        Assert.Equal(7, await Estoque(produto));

        var pedidoId = r.Texto("id");
        var venda = await app.NoBanco(db => Task.FromResult(db.MovimentacoesEstoque.Single(m => m.PedidoId == pedidoId)));
        Assert.Equal("venda", venda.Tipo);
        Assert.Equal(-3, venda.Quantidade);

        var pagamento = await PagamentoDe(pedidoId);
        Assert.Equal("pedido", pagamento.Origem);
        Assert.Equal("Pendente", pagamento.Status);
        Assert.Equal(75m, pagamento.Valor);
    }

    [Fact]
    public async Task Pedido_maior_que_o_estoque_e_recusado_sem_baixar_nada()
    {
        var painel = app.Api();
        var produto = await painel.ProdutoNaLoja(await painel.LoginAdmin(), estoque: 2);
        var cliente = app.Api();
        await cliente.CadastrarCliente();

        var r = await cliente.Pedir(produto, 3);

        Assert.Equal(400, r.Codigo);
        Assert.Contains("Temos apenas 2 unidade(s)", r.Erro);
        Assert.Equal(2, await Estoque(produto));
        Assert.False(await app.NoBanco(db => Task.FromResult(db.Pedidos.Any(p => p.ProdutoId == produto))));
    }

    [Theory]
    [InlineData(0, Cenarios.Franco, true, "entre 1 e 99")]
    [InlineData(100, Cenarios.Franco, true, "entre 1 e 99")]
    [InlineData(1, null, true, "Escolha a unidade")]
    [InlineData(1, "Unidade Fantasma", true, "Escolha a unidade")]
    [InlineData(1, Cenarios.Franco, false, "não está disponível na loja")]
    public async Task Pedido_invalido_e_recusado(int quantidade, string? unidade, bool visivel, string trecho)
    {
        var painel = app.Api();
        var produto = await painel.ProdutoNaLoja(await painel.LoginAdmin(), visivelLoja: visivel);
        var cliente = app.Api();
        await cliente.CadastrarCliente();

        var r = await cliente.Pedir(produto, quantidade, unidade);

        Assert.Equal(400, r.Codigo);
        Assert.Contains(trecho, r.Erro);
        Assert.Equal(10, await Estoque(produto));
    }

    [Fact]
    public async Task Cliente_cancela_pedido_pendente_e_o_estoque_volta()
    {
        var painel = app.Api();
        var produto = await painel.ProdutoNaLoja(await painel.LoginAdmin(), estoque: 10);
        var cliente = app.Api();
        await cliente.CadastrarCliente();
        var pedidoId = (await cliente.Pedir(produto, 4)).Texto("id");

        var r = await cliente.Post($"/api/cliente/pedidos/{pedidoId}/cancelar");

        Assert.Equal(200, r.Codigo);
        Assert.Equal(10, await Estoque(produto));
        var devolucao = await app.NoBanco(db => Task.FromResult(db.MovimentacoesEstoque.Single(m => m.PedidoId == pedidoId && m.Tipo == "cancelamento")));
        Assert.Equal(4, devolucao.Quantidade);
        Assert.Equal("Cancelado", (await PagamentoDe(pedidoId)).Status);
    }

    [Fact]
    public async Task Depois_de_confirmado_so_o_painel_cancela_e_o_estoque_volta_uma_vez()
    {
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        var produto = await painel.ProdutoNaLoja(token, estoque: 10);
        var cliente = app.Api();
        await cliente.CadastrarCliente();
        var pedidoId = (await cliente.Pedir(produto, 3)).Texto("id");

        var confirmar = await StatusNoPainel(painel, token, pedidoId, "Confirmado");
        var clienteCancela = await cliente.Post($"/api/cliente/pedidos/{pedidoId}/cancelar");
        var painelCancela = await StatusNoPainel(painel, token, pedidoId, "Cancelado");
        var depois = await StatusNoPainel(painel, token, pedidoId, "Entregue");

        Assert.Equal(200, confirmar.Codigo);
        Assert.Equal(400, clienteCancela.Codigo);
        Assert.Contains("já foi confirmado", clienteCancela.Erro);
        Assert.Equal(200, painelCancela.Codigo);
        Assert.Equal(3, painelCancela.Data.GetProperty("devolvido").GetInt32());
        Assert.Equal(400, depois.Codigo);
        Assert.Contains("já foi cancelado", depois.Erro);
        Assert.Equal(10, await Estoque(produto));
    }

    [Fact]
    public async Task Painel_recusa_status_de_pedido_desconhecido()
    {
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        var produto = await painel.ProdutoNaLoja(token);
        var cliente = app.Api();
        await cliente.CadastrarCliente();
        var pedidoId = (await cliente.Pedir(produto, 1)).Texto("id");

        var r = await StatusNoPainel(painel, token, pedidoId, "Extraviado");

        Assert.Equal(400, r.Codigo);
        Assert.Contains("Status inválido", r.Erro);
    }

    [Fact]
    public async Task So_visualizar_pedidos_nao_muda_status()
    {
        var painel = app.Api();
        var geral = await painel.LoginAdmin();
        var produto = await painel.ProdutoNaLoja(geral);
        var cliente = app.Api();
        await cliente.CadastrarCliente();
        var pedidoId = (await cliente.Pedir(produto, 2)).Texto("id");
        var leitor = await painel.AdminCom(geral, Permissao.So("pedidos", visualizar: true));

        var r = await StatusNoPainel(painel, leitor, pedidoId, "Cancelado");

        Assert.Equal(403, r.Codigo);
        Assert.Equal(8, await Estoque(produto));
    }

    [Fact]
    public async Task Cliente_nao_cancela_pedido_de_outro_cliente()
    {
        var painel = app.Api();
        var produto = await painel.ProdutoNaLoja(await painel.LoginAdmin());
        var dono = app.Api();
        await dono.CadastrarCliente();
        var pedidoId = (await dono.Pedir(produto, 1)).Texto("id");
        var intruso = app.Api();
        await intruso.CadastrarCliente();

        var r = await intruso.Post($"/api/cliente/pedidos/{pedidoId}/cancelar");

        Assert.Equal(400, r.Codigo);
        Assert.Equal("Pedido não localizado na sua conta.", r.Erro);
        Assert.Equal(9, await Estoque(produto));
    }
}
