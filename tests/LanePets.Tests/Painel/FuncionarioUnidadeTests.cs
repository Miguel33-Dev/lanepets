using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// Funcionario preso a unidade (item 1 do roadmap). Regras: CONTEXTO §6.19 — perfil mais
/// restrito = teto no backend (Pode() + VeUnidade()), filtro por unidade antes de responder,
/// falha fechada. O funcionario daqui e de Caieiras; os registros "alheios" sao de Franco.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class FuncionarioUnidadeTests(LanePetsApp app)
{
    private const string Caieiras = "caieiras";

    private async Task<(string Id, System.Text.Json.JsonElement Dados)> Agendamento(string dia, string horario, string unidade)
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet("Cliente Unidade", "Pipoca");
        var (servicoId, _) = await cliente.Servico();
        var r = await cliente.Agendar(petId, servicoId, dia, horario, unidade);
        Assert.True(r.Codigo == 200, r.ToString());
        return (r.Texto("id"), r.Data);
    }

    [Fact]
    public async Task Ve_so_a_agenda_e_os_indicadores_da_propria_unidade()
    {
        var dia = Cenarios.NovaData();
        await Agendamento(dia, "10:00", Cenarios.Franco);
        var (idCaieiras, _) = await Agendamento(dia, "11:00", Cenarios.Caieiras);
        var painel = app.Api();
        var geral = await painel.LoginAdmin();
        var func = await painel.FuncionarioCom(geral, Caieiras, Permissao.So("dashboard"), Permissao.So("agendamentos"));

        var resumoGeral = await painel.Get($"/api/admin/resumo?token={geral}&de={dia}&ate={dia}&unidade=todas");
        Assert.Equal(2, resumoGeral.Data.GetProperty("indicadores").GetProperty("agendamentosPeriodo").GetInt32());

        var resumoFunc = await painel.Get($"/api/admin/resumo?token={func}&de={dia}&ate={dia}&unidade=todas");
        Assert.True(resumoFunc.Codigo == 200, resumoFunc.ToString());
        Assert.Equal(1, resumoFunc.Data.GetProperty("indicadores").GetProperty("agendamentosPeriodo").GetInt32());
        Assert.True(resumoFunc.Data.GetProperty("restrito").GetBoolean());          // funcionario nunca ve dinheiro

        var agenda = await painel.Get($"/api/agendamentos?token={func}&data={dia}");
        Assert.True(agenda.Codigo == 200, agenda.ToString());
        var item = Assert.Single(agenda.Data.GetProperty("agendamentos").EnumerateArray());
        Assert.Equal(idCaieiras, item.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Teto_do_perfil_barra_financeiro_mesmo_com_a_permissao_marcada()
    {
        var painel = app.Api();
        var geral = await painel.LoginAdmin();
        var func = await painel.FuncionarioCom(geral, Caieiras,
            Permissao.So("dashboard"), Permissao.Tudo("pagamentos"), Permissao.Tudo("relatorios"), Permissao.Tudo("produtos"));

        Assert.Equal(403, (await painel.Get($"/api/admin/pagamentos?token={func}")).Codigo);
        Assert.Equal(403, (await painel.Get($"/api/admin/relatorio?token={func}")).Codigo);
        Assert.Equal(403, (await painel.Get($"/api/admin/usuarios?token={func}")).Codigo);

        // Produtos: o teto deixa so consultar.
        Assert.Equal(200, (await painel.Get($"/api/admin/produtos?token={func}")).Codigo);
        var criar = await painel.Sync(func, "produtos", criados:
            [new { id = Api.NovoId("PRD"), codigo = Api.NovoId("C"), nome = "Produto do funcionario", valorVenda = 10, estoque = 1 }]);
        Assert.Equal(403, criar.Codigo);
    }

    [Fact]
    public async Task Nao_altera_agendamento_nem_pedido_de_outra_unidade_mas_altera_os_da_sua()
    {
        var dia = Cenarios.NovaData();
        var (idFranco, franco) = await Agendamento(dia, "10:00", Cenarios.Franco);
        var (idCaieiras, caieiras) = await Agendamento(dia, "11:00", Cenarios.Caieiras);
        var painel = app.Api();
        var geral = await painel.LoginAdmin();
        var func = await painel.FuncionarioCom(geral, Caieiras, Permissao.Tudo("agendamentos"), Permissao.Tudo("pedidos"));

        var alheio = await painel.Sync(func, "agendamentos", atualizados: [Cenarios.ItemPainel(franco, "Confirmado")]);
        Assert.Equal(403, alheio.Codigo);
        Assert.Equal("Solicitado", await app.NoBanco(db => Task.FromResult(db.Agendamentos.Single(a => a.Id == idFranco).Status)));

        var proprio = await painel.Sync(func, "agendamentos", atualizados: [Cenarios.ItemPainel(caieiras, "Confirmado")]);
        Assert.True(proprio.Codigo == 200, proprio.ToString());
        Assert.Equal("Confirmado", await app.NoBanco(db => Task.FromResult(db.Agendamentos.Single(a => a.Id == idCaieiras).Status)));

        // Pedido com retirada em Franco.
        var produto = await painel.ProdutoNaLoja(geral);
        var cliente = app.Api();
        await cliente.CadastrarCliente(nome: "Cliente Pedido Unidade");
        var pedido = await cliente.Pedir(produto, 1, Cenarios.Franco);
        Assert.True(pedido.Codigo == 200, pedido.ToString());
        var pedidoId = pedido.Texto("id");

        var status = await painel.Post($"/api/admin/pedidos/{pedidoId}/status", new { token = func, status = "Confirmado" });
        Assert.Equal(403, status.Codigo);
        Assert.Equal("Pendente", await app.NoBanco(db => Task.FromResult(db.Pedidos.Single(p => p.Id == pedidoId).Status)));
    }
}
