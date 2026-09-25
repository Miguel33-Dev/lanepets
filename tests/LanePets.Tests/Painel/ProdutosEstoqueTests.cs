using System.Text.Json;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// Produtos e livro de estoque no painel (itens 6, 7 e 13 do roadmap).
/// Regras: CONTEXTO §6.14 (validacao de produto no backend), §6.28 (saldo so muda pelo
/// livro de estoque; edicao do produto nao mexe no saldo; alerta quando cruza o minimo;
/// produto oculto nao aparece na loja).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class ProdutosEstoqueTests(LanePetsApp app)
{
    // ------------------------------------------------------------------ apoio

    private static object Produto(string id, string nome = "Ração Teste 1kg", double valorVenda = 50, double valorCompra = 30,
        int estoque = 10, int estoqueMinimo = 2, bool controlaEstoque = true, bool visivelLoja = true)
        => new { id, codigo = id, nome, categoria = "Ração", valorVenda, valorCompra, estoque, estoqueMinimo, controlaEstoque, visivelLoja, descricao = "Produto criado pelo teste." };

    /// <summary>Cria o produto pelo painel (Geral) e devolve o token usado.</summary>
    private static async Task<string> Criar(Api api, object produto)
    {
        var token = await api.LoginAdmin();
        var r = await api.Sync(token, "produtos", criados: [produto]);
        Assert.True(r.Codigo == 200, $"Criar produto falhou: {r}");
        return token;
    }

    private Task<LanePets.Models.Produto?> NoBanco(string id)
        => app.NoBanco(db => db.Produtos.FindAsync(id).AsTask());

    private static async Task<JsonElement> DaLista(Api api, string token, string id)
    {
        var lista = await api.Get($"/api/admin/produtos?token={token}");
        Assert.Equal(200, lista.Codigo);
        return lista.Data.EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);
    }

    private static Task<Resposta> Movimentar(Api api, string token, string id, string tipo, int quantidade, string motivo = "Movimentação do teste")
        => api.Post($"/api/admin/produtos/{id}/movimentacao", new { token, tipo, quantidade, motivo });

    // ------------------------------------------------------------------ cadastro

    [Fact]
    public async Task Estoque_inicial_do_cadastro_entra_pelo_livro()
    {
        var api = app.Api();
        var id = Api.NovoId("PRD");
        var token = await Criar(api, Produto(id, estoque: 10));

        var produto = await DaLista(api, token, id);
        Assert.Equal(10, produto.GetProperty("estoque").GetInt32());
        Assert.Equal("ok", produto.GetProperty("situacaoEstoque").GetString());

        var livro = await api.Get($"/api/admin/estoque/movimentacoes?token={token}&produtoId={id}");
        var unica = Assert.Single(livro.Data.EnumerateArray());
        Assert.Equal("entrada", unica.GetProperty("tipo").GetString());
        Assert.Equal(10, unica.GetProperty("quantidade").GetInt32());
        Assert.Equal(0, unica.GetProperty("saldoAnterior").GetInt32());
        Assert.Equal(10, unica.GetProperty("saldoNovo").GetInt32());
    }

    [Theory]
    [InlineData("", 50, 30, 10, "Informe o nome do produto.")]
    [InlineData("Produto Grátis", 0, 30, 10, "maior que zero")]
    [InlineData("Produto Negativo", -5, 30, 10, "maior que zero")]
    [InlineData("Custo Negativo", 50, -1, 10, "não pode ser negativo")]
    [InlineData("Estoque Negativo", 50, 30, -1, "não pode ser negativo")]
    public async Task Produto_invalido_e_recusado_e_nada_e_gravado(string nome, double venda, double compra, int estoque, string trecho)
    {
        var api = app.Api();
        var id = Api.NovoId("PRD");

        var r = await api.Sync(await api.LoginAdmin(), "produtos", criados: [Produto(id, nome, venda, compra, estoque)]);

        Assert.Equal(400, r.Codigo);
        Assert.Equal("ERR-4000", r.CodigoErro);
        Assert.Contains(trecho, r.Erro);
        Assert.Null(await NoBanco(id));
    }

    [Fact]
    public async Task Lote_com_um_produto_invalido_nao_grava_nenhum()
    {
        var api = app.Api();
        var bom = Api.NovoId("PRD");
        var ruim = Api.NovoId("PRD");

        var r = await api.Sync(await api.LoginAdmin(), "produtos",
            criados: [Produto(bom, "Produto Bom"), Produto(ruim, "Produto Ruim", valorVenda: 0)]);

        Assert.Equal(400, r.Codigo);
        Assert.Null(await NoBanco(bom));
        Assert.Null(await NoBanco(ruim));
    }

    [Fact]
    public async Task Editar_o_produto_muda_preco_mas_nao_o_saldo()
    {
        var api = app.Api();
        var id = Api.NovoId("PRD");
        var token = await Criar(api, Produto(id, estoque: 10));

        // A tela manda o produto inteiro, inclusive um saldo velho que tinha em memoria.
        var r = await api.Sync(token, "produtos", atualizados: [Produto(id, "Ração Teste 1kg (nova)", valorVenda: 55, estoque: 999)]);

        Assert.Equal(200, r.Codigo);
        var produto = await NoBanco(id);
        Assert.Equal(10, produto!.Estoque);
        Assert.Equal(55, produto.ValorVenda);
        Assert.Equal("Ração Teste 1kg (nova)", produto.Nome);
    }

    [Fact]
    public async Task Produto_oculto_nao_aparece_na_loja_publica()
    {
        var api = app.Api();
        var visivel = Api.NovoId("PRD");
        var oculto = Api.NovoId("PRD");
        var token = await Criar(api, Produto(visivel, "Produto Visível"));
        await api.Sync(token, "produtos", criados: [Produto(oculto, "Produto Oculto", visivelLoja: false)]);

        var loja = await app.Api().Get("/api/public/produtos");

        var ids = loja.Data.EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();
        Assert.Contains(visivel, ids);
        Assert.DoesNotContain(oculto, ids);
    }

    // ------------------------------------------------------------------ livro de estoque

    [Fact]
    public async Task Entrada_saida_e_ajuste_mudam_o_saldo_e_ficam_no_livro()
    {
        var api = app.Api();
        var id = Api.NovoId("PRD");
        var token = await Criar(api, Produto(id, estoque: 10));

        var entrada = await Movimentar(api, token, id, "entrada", 5, "Compra do fornecedor");
        var saida = await Movimentar(api, token, id, "saída", 3, "Avaria no depósito");   // com acento: a API normaliza
        var ajuste = await Movimentar(api, token, id, "ajuste", 4, "Inventário do mês");

        Assert.Equal(15, entrada.Data.GetProperty("estoque").GetInt32());
        Assert.Equal(12, saida.Data.GetProperty("estoque").GetInt32());
        Assert.Equal(4, ajuste.Data.GetProperty("estoque").GetInt32());
        Assert.Equal(4, (await NoBanco(id))!.Estoque);

        var livro = (await api.Get($"/api/admin/estoque/movimentacoes?token={token}&produtoId={id}")).Data.EnumerateArray().ToList();
        Assert.Equal(4, livro.Count);   // inicial + 3
        Assert.Equal(new[] { 10, 5, -3, -8 }, livro.Select(m => m.GetProperty("quantidade").GetInt32()).Reverse());
        Assert.All(livro, m => Assert.Equal(Api.AdminEmail, m.GetProperty("autor").GetString()));
    }

    [Theory]
    [InlineData("saida", 11, "Movimentação do teste", "Saída maior que o saldo")]
    [InlineData("roubo", 1, "Movimentação do teste", "Tipo de movimentação inválido")]
    [InlineData("entrada", 0, "Movimentação do teste", "entre 1 e 100000")]
    [InlineData("entrada", 1, "ok", "motivo")]
    [InlineData("ajuste", 10, "Mesmo saldo", "O saldo já é 10")]
    public async Task Movimentacao_invalida_e_recusada_e_o_saldo_nao_muda(string tipo, int quantidade, string motivo, string trecho)
    {
        var api = app.Api();
        var id = Api.NovoId("PRD");
        var token = await Criar(api, Produto(id, estoque: 10));

        var r = await Movimentar(api, token, id, tipo, quantidade, motivo);

        Assert.Equal(400, r.Codigo);
        Assert.Contains(trecho, r.Erro, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, (await NoBanco(id))!.Estoque);
        Assert.Equal(1, await app.NoBanco(db => Task.FromResult(db.MovimentacoesEstoque.Count(m => m.ProdutoId == id))));
    }

    [Fact]
    public async Task Produto_sem_controle_de_estoque_nao_aceita_movimentacao()
    {
        var api = app.Api();
        var id = Api.NovoId("PRD");
        var token = await Criar(api, Produto(id, controlaEstoque: false));

        var r = await Movimentar(api, token, id, "entrada", 1);

        Assert.Equal(400, r.Codigo);
        Assert.Contains("Ligue o controle de estoque", r.Erro);
        Assert.Equal("livre", (await DaLista(api, token, id)).GetProperty("situacaoEstoque").GetString());
    }

    [Fact]
    public async Task Cruzar_o_minimo_marca_baixo_e_gera_um_alerta_so()
    {
        var api = app.Api();
        var id = Api.NovoId("PRD");
        var token = await Criar(api, Produto(id, estoque: 10, estoqueMinimo: 5));

        await Movimentar(api, token, id, "saida", 6);   // 10 -> 4: cruza o minimo
        await Movimentar(api, token, id, "saida", 1);   // 4 -> 3: ja estava baixo, sem alerta novo

        Assert.Equal("baixo", (await DaLista(api, token, id)).GetProperty("situacaoEstoque").GetString());
        var alertas = await app.NoBanco(db => Task.FromResult(db.EventosLog.Count(e => e.Acao == "Estoque baixo" && e.AlvoId == id)));
        Assert.Equal(1, alertas);

        await Movimentar(api, token, id, "saida", 3);   // 3 -> 0
        Assert.Equal("zero", (await DaLista(api, token, id)).GetProperty("situacaoEstoque").GetString());
    }

    // ------------------------------------------------------------------ permissoes

    [Fact]
    public async Task So_visualizar_produtos_ve_a_lista_mas_nao_movimenta_nem_cria()
    {
        var api = app.Api();
        var id = Api.NovoId("PRD");
        var geral = await Criar(api, Produto(id, estoque: 10));
        var leitor = await api.AdminCom(geral, Permissao.So("produtos", visualizar: true));
        var novo = Api.NovoId("PRD");

        var lista = await api.Get($"/api/admin/produtos?token={leitor}");
        var movimentar = await Movimentar(api, leitor, id, "saida", 1);
        var criar = await api.Sync(leitor, "produtos", criados: [Produto(novo)]);

        Assert.Equal(200, lista.Codigo);
        Assert.Equal(403, movimentar.Codigo);
        Assert.Equal(403, criar.Codigo);
        Assert.Equal(10, (await NoBanco(id))!.Estoque);
        Assert.Null(await NoBanco(novo));
    }

    [Fact]
    public async Task Sem_o_modulo_produtos_lista_e_livro_respondem_403()
    {
        var api = app.Api();
        var semProdutos = await api.AdminCom(await api.LoginAdmin(), Permissao.So("clientes"));

        Assert.Equal(403, (await api.Get($"/api/admin/produtos?token={semProdutos}")).Codigo);
        Assert.Equal(403, (await api.Get($"/api/admin/estoque/movimentacoes?token={semProdutos}")).Codigo);
    }
}
