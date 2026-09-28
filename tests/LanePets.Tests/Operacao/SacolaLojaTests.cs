using LanePets.Tests.Infra;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Tests.Operacao;

/// <summary>
/// 29/09 (melhoria pedida pelo Fabricio): sacola da loja — POST /api/cliente/pedidos/lote. Um pedido por produto,
/// gravados JUNTOS: se um item falha, nada e gravado (nem pedido, nem baixa de estoque, nem pagamento).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class SacolaLojaTests(LanePetsApp app)
{
    private Task<int> Estoque(string produtoId) => app.NoBanco(db => db.Produtos.AsNoTracking().Where(p => p.Id == produtoId).Select(p => p.Estoque).SingleAsync());

    private static Task<Resposta> Finalizar(Api api, params object[] itens)
        => api.Post("/api/cliente/pedidos/lote", new { itens, formaPagamento = "Pix", unidade = Cenarios.Franco });

    [Fact]
    public async Task Sacola_com_dois_produtos_vira_dois_pedidos_baixa_o_estoque_e_gera_dois_pagamentos()
    {
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        var racao = await painel.ProdutoNaLoja(token, estoque: 10, valorVenda: 40);
        var brinquedo = await painel.ProdutoNaLoja(token, estoque: 5, valorVenda: 15);
        var api = app.Api();
        await api.ClienteComPet();

        var r = await Finalizar(api, new { produtoId = racao, quantidade = 2 }, new { produtoId = brinquedo, quantidade = 3 });

        Assert.True(r.Codigo == 200, r.ToString());
        Assert.Equal(2 * 40m + 3 * 15m, r.Data.GetProperty("total").GetDecimal());
        Assert.Equal(2, r.Data.GetProperty("pedidos").GetArrayLength());
        Assert.Equal(8, await Estoque(racao));
        Assert.Equal(2, await Estoque(brinquedo));

        var ids = r.Data.GetProperty("pedidos").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();
        var pagamentos = await app.NoBanco(db => db.Pagamentos.AsNoTracking().Where(p => p.Origem == "pedido" && ids.Contains(p.OrigemId)).CountAsync());
        Assert.Equal(2, pagamentos);
        Assert.Equal(2, (await api.Get("/api/cliente/conta")).Data.GetProperty("pedidos").GetArrayLength());
    }

    [Fact]
    public async Task Um_item_sem_estoque_derruba_a_sacola_inteira_sem_gravar_nada()
    {
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        var tem = await painel.ProdutoNaLoja(token, estoque: 10);
        var pouco = await painel.ProdutoNaLoja(token, estoque: 1);
        var api = app.Api();
        await api.ClienteComPet();

        var r = await Finalizar(api, new { produtoId = tem, quantidade = 2 }, new { produtoId = pouco, quantidade = 3 });

        Assert.Equal(400, r.Codigo);
        Assert.Contains("apenas 1", r.Erro);
        Assert.Equal(10, await Estoque(tem));   // a baixa do primeiro item NAO ficou gravada
        Assert.Equal(1, await Estoque(pouco));
        Assert.Equal(0, (await api.Get("/api/cliente/conta")).Data.GetProperty("pedidos").GetArrayLength());
    }

    [Fact]
    public async Task Produto_repetido_soma_a_quantidade_e_confere_o_estoque_do_total()
    {
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        var produto = await painel.ProdutoNaLoja(token, estoque: 4);
        var api = app.Api();
        await api.ClienteComPet();

        var demais = await Finalizar(api, new { produtoId = produto, quantidade = 3 }, new { produtoId = produto, quantidade = 2 });
        Assert.Equal(400, demais.Codigo);
        Assert.Equal(4, await Estoque(produto));

        var ok = await Finalizar(api, new { produtoId = produto, quantidade = 1 }, new { produtoId = produto, quantidade = 2 });
        Assert.True(ok.Codigo == 200, ok.ToString());
        var pedido = Assert.Single(ok.Data.GetProperty("pedidos").EnumerateArray());
        Assert.Equal(3, pedido.GetProperty("quantidade").GetInt32());
        Assert.Equal(1, await Estoque(produto));
    }

    [Fact]
    public async Task Sacola_vazia_ou_sem_sessao_e_recusada()
    {
        var api = app.Api();
        await api.ClienteComPet();
        var vazia = await Finalizar(api);
        Assert.Equal(400, vazia.Codigo);
        Assert.Equal("Sua sacola está vazia.", vazia.Erro);

        Assert.Equal(401, (await Finalizar(app.Api(), new { produtoId = "X", quantidade = 1 })).Codigo);
    }
}
