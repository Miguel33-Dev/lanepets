using LanePets.Services;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// 27/09: Pedidos da loja paginados e filtrados no servidor (ListaPaginada). Regras: pagina
/// padrao 50 / maximo 500, ordem estavel (nada repetido nem pulado), busca no padrao LaneBusca
/// (inclusive digitos do telefone), resumo da base visivel inteira e funcionario so ve a
/// unidade dele (§6.19).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class PaginacaoPedidosTests(LanePetsApp app)
{
    private static List<string> Ids(Resposta r)
        => r.Data.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();

    /// <summary>Um cliente com nome marcado faz <paramref name="quantos"/> pedidos (retirada em Franco).</summary>
    private async Task<(string Marca, string Token, List<string> Pedidos)> ClienteComPedidos(int quantos)
    {
        var marca = Guid.NewGuid().ToString("N")[..8];
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        var produto = await painel.ProdutoNaLoja(token, estoque: 50);
        var cliente = app.Api();
        await cliente.CadastrarCliente(nome: $"Cliente Pedido {marca}");
        var ids = new List<string>();
        for (var i = 0; i < quantos; i++)
        {
            var r = await cliente.Pedir(produto, 1);
            Assert.True(r.Codigo == 200, r.ToString());
            ids.Add(r.Texto("id"));
        }
        return (marca, token, ids);
    }

    [Fact]
    public async Task Pedidos_vem_em_paginas_sem_repetir_e_o_resumo_e_da_base_inteira()
    {
        var (marca, token, criados) = await ClienteComPedidos(3);
        var painel = app.Api();
        var filtro = $"/api/admin/pedidos?token={token}&busca={marca}";

        var p1 = await painel.Get(filtro + "&limite=2&offset=0");
        var p2 = await painel.Get(filtro + "&limite=2&offset=2");

        Assert.True(p1.Codigo == 200, p1.ToString());
        Assert.Equal(3, p1.Data.GetProperty("total").GetInt32());
        Assert.True(p1.Data.GetProperty("temMais").GetBoolean());
        Assert.False(p2.Data.GetProperty("temMais").GetBoolean());
        var ids = Ids(p1).Concat(Ids(p2)).ToList();
        Assert.Equal(3, ids.Distinct().Count());                                  // nada repetido nem pulado
        Assert.Equal(criados.OrderBy(x => x), ids.OrderBy(x => x));
        // Resumo = base visivel inteira (cartoes "Total registrado"), nao so o filtro/pagina.
        Assert.True(p1.Data.GetProperty("resumo").GetProperty("total").GetInt32() >= 3);

        // Sem limite: pagina padrao de 50; limite fora da faixa e ajustado, nunca erro.
        var padrao = await painel.Get(filtro);
        Assert.Equal(50, padrao.Data.GetProperty("limite").GetInt32());
        Assert.Equal(3, padrao.Data.GetProperty("itens").GetArrayLength());
        Assert.Equal(500, (await painel.Get(filtro + "&limite=99999")).Data.GetProperty("limite").GetInt32());
        Assert.Equal(0, (await painel.Get(filtro + "&offset=-5")).Data.GetProperty("offset").GetInt32());

        // Detalhe do cliente: filtro por clienteId traz so os pedidos dele.
        var clienteId = p1.Data.GetProperty("itens")[0].GetProperty("clienteId").GetString();
        var doCliente = await painel.Get($"/api/admin/pedidos?token={token}&clienteId={clienteId}&limite=8");
        Assert.Equal(3, doCliente.Data.GetProperty("total").GetInt32());
        // Data com fuso: o navegador converte para a hora local.
        Assert.EndsWith("Z", doCliente.Data.GetProperty("itens")[0].GetProperty("criadoEm").GetString());
    }

    [Fact]
    public async Task Filtros_de_status_unidade_telefone_e_periodo_rodam_no_servidor()
    {
        var (marca, token, criados) = await ClienteComPedidos(2);
        var painel = app.Api();
        var cancelado = await painel.Post($"/api/admin/pedidos/{criados[0]}/status", new { token, status = "Cancelado" });
        Assert.True(cancelado.Codigo == 200, cancelado.ToString());
        var baseUrl = $"/api/admin/pedidos?token={token}&busca={marca}";
        async Task<int> Total(string extra) => (await painel.Get(baseUrl + extra)).Data.GetProperty("total").GetInt32();

        Assert.Equal(1, await Total("&status=Cancelado"));
        Assert.Equal(1, await Total("&status=pendente"));                        // sem diferenca de maiuscula
        Assert.Equal(2, await Total("&unidade=franco"));
        Assert.Equal(0, await Total("&unidade=caieiras"));
        Assert.Equal(0, await Total("&unidade=nao-existe"));                     // unidade desconhecida nao vira "sem unidade"
        Assert.Equal(0, await Total("&unidade=sem-unidade"));
        Assert.Equal(2, await Total("%20" + Uri.EscapeDataString("98765-4321")));  // telefone pelos digitos
        var hoje = DateTime.Now.ToString("yyyy-MM-dd");
        var ontem = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
        Assert.Equal(2, await Total($"&de={hoje}&ate={hoje}"));
        Assert.Equal(0, await Total($"&ate={ontem}"));
    }

    [Fact]
    public async Task Funcionario_de_outra_unidade_nao_ve_os_pedidos_nem_no_resumo()
    {
        var (marca, geral, _) = await ClienteComPedidos(1);
        var painel = app.Api();
        var func = await painel.FuncionarioCom(geral, "caieiras", Permissao.So("pedidos"));

        var r = await painel.Get($"/api/admin/pedidos?token={func}&busca={marca}");

        Assert.True(r.Codigo == 200, r.ToString());
        Assert.Equal(0, r.Data.GetProperty("total").GetInt32());
        // Sem permissao de pedidos: 403, nunca lista vazia.
        var semPermissao = await painel.AdminCom(geral, Permissao.So("produtos"));
        Assert.Equal(403, (await painel.Get($"/api/admin/pedidos?token={semPermissao}")).Codigo);
    }

    [Theory]
    [InlineData("racao", true)]            // sem acento
    [InlineData("RAÇÃO rex", true)]        // varias palavras, maiuscula
    [InlineData("racao gato", false)]      // todas as palavras precisam aparecer
    [InlineData("999990000", true)]        // telefone comparado pelos digitos
    [InlineData("(12)", false)]            // menos de 3 digitos nao compara por digito
    [InlineData("", true)]
    public void Combina_segue_o_padrao_do_LaneBusca(string termo, bool esperado)
        => Assert.Equal(esperado, ListaPaginada.Combina(termo, "Ração Premium", "Rex", "(11) 99999-0000", "Joe"));
}
