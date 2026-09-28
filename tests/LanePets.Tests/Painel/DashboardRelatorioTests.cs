using System.Text.Json;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// Dashboard (GET /api/admin/resumo) e Relatorio Financeiro (GET /api/admin/relatorio).
/// Regras: CONTEXTO §6.16 (mesmo Calcular nos dois), §6.31 (dinheiro so com
/// pagamentos:visualizar) e financeiro.md. Cada teste usa um dia so dele (Cenarios.NovaData),
/// entao o periodo filtrado contem exatamente o que o teste criou.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class DashboardRelatorioTests(LanePetsApp app)
{
    /// <summary>Agendamento do cliente na unidade pedida, num dia exclusivo. Devolve o dia e o agendamento.</summary>
    private async Task<(string Dia, JsonElement Agendamento)> AtendimentoNoDia(string unidade = Cenarios.Franco)
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet("Cliente Dashboard", "Farofa");
        var (servicoId, _) = await cliente.Servico();
        var dia = Cenarios.NovaData();
        var r = await cliente.Agendar(petId, servicoId, dia, "10:00", unidade);
        Assert.True(r.Codigo == 200, r.ToString());
        return (dia, r.Data);
    }

    private static async Task<JsonElement> Resumo(Api api, string token, string dia, string unidade = "franco")
    {
        var r = await api.Get($"/api/admin/resumo?token={token}&de={dia}&ate={dia}&unidade={unidade}");
        Assert.True(r.Codigo == 200, r.ToString());
        return r.Data;
    }

    private static async Task<JsonElement> Relatorio(Api api, string token, string dia, string unidade = "franco")
    {
        var r = await api.Get($"/api/admin/relatorio?token={token}&de={dia}&ate={dia}&unidade={unidade}");
        Assert.True(r.Codigo == 200, r.ToString());
        return r.Data;
    }

    [Fact]
    public async Task Resumo_conta_o_atendimento_do_periodo_e_o_faturamento_na_unidade_certa()
    {
        var (dia, agendamento) = await AtendimentoNoDia();
        var total = agendamento.GetProperty("total").GetDecimal();
        var painel = app.Api();
        var token = await painel.LoginAdmin();

        var franco = await Resumo(painel, token, dia);
        Assert.False(franco.GetProperty("restrito").GetBoolean());
        Assert.Equal(1, franco.GetProperty("indicadores").GetProperty("agendamentosPeriodo").GetInt32());
        Assert.Equal(total, franco.GetProperty("indicadores").GetProperty("faturamento").GetDecimal());
        Assert.Equal(total, franco.GetProperty("financeiro").GetProperty("receita").GetDecimal());

        // O mesmo dia filtrado pela outra unidade nao ve o atendimento de Franco.
        var caieiras = await Resumo(painel, token, dia, "caieiras");
        Assert.Equal(0, caieiras.GetProperty("indicadores").GetProperty("agendamentosPeriodo").GetInt32());
        Assert.Equal(0m, caieiras.GetProperty("indicadores").GetProperty("faturamento").GetDecimal());
    }

    [Fact]
    public async Task Resumo_sem_permissao_de_pagamentos_nao_revela_dinheiro()
    {
        var (dia, _) = await AtendimentoNoDia();
        var painel = app.Api();
        var geral = await painel.LoginAdmin();
        var soDashboard = await painel.AdminCom(geral, Permissao.So("dashboard"));

        var d = await Resumo(painel, soDashboard, dia);

        Assert.True(d.GetProperty("restrito").GetBoolean());
        var ind = d.GetProperty("indicadores");
        Assert.Equal(1, ind.GetProperty("agendamentosPeriodo").GetInt32());          // contagem operacional continua
        foreach (var campo in new[] { "faturamento", "pagamentosPendentes", "valorPendente", "reembolsosPendentes" })
            Assert.Equal(JsonValueKind.Null, ind.GetProperty(campo).ValueKind);
        // §6.31: dinheiro vem null (nunca 0) tambem nos blocos financeiro, anterior e operacional.
        foreach (var campo in new[] { "receita", "despesas", "resultado", "margem", "ticketMedio" })
            Assert.Equal(JsonValueKind.Null, d.GetProperty("financeiro").GetProperty(campo).ValueKind);
        foreach (var campo in new[] { "receita", "despesas", "resultado" })
            Assert.Equal(JsonValueKind.Null, d.GetProperty("anterior").GetProperty(campo).ValueKind);
        foreach (var campo in new[] { "transporteFaturado", "receitaDePedidos" })
            Assert.Equal(JsonValueKind.Null, d.GetProperty("operacional").GetProperty(campo).ValueKind);
        Assert.Equal(1, d.GetProperty("financeiro").GetProperty("atendimentos").GetInt32());   // contagem continua
        Assert.Empty(d.GetProperty("categorias").EnumerateArray());
        Assert.Empty(d.GetProperty("porUnidade").EnumerateArray());
    }

    [Fact]
    public async Task Relatorio_le_os_pagamentos_da_entidade_e_acompanha_a_aprovacao()
    {
        var (dia, agendamento) = await AtendimentoNoDia();
        var total = agendamento.GetProperty("total").GetDecimal();
        var painel = app.Api();
        var token = await painel.LoginAdmin();

        var antes = await Relatorio(painel, token, dia);
        Assert.Equal(total, antes.GetProperty("cards").GetProperty("entradas").GetDecimal());
        Assert.Equal(1, antes.GetProperty("pagamentosResumo").GetProperty("pendentes").GetInt32());
        Assert.Equal(0, antes.GetProperty("pagamentosResumo").GetProperty("pagos").GetInt32());

        var pagamentoId = await app.NoBanco(db => Task.FromResult(
            db.Pagamentos.Single(p => p.Origem == "agendamento" && p.OrigemId == agendamento.GetProperty("id").GetString()).Id));
        var aprovado = await painel.Post($"/api/admin/pagamentos/{pagamentoId}/status", new { token, status = "Aprovado" });
        Assert.True(aprovado.Codigo == 200, aprovado.ToString());

        var depois = await Relatorio(painel, token, dia);
        var pags = depois.GetProperty("pagamentosResumo");
        Assert.Equal(1, pags.GetProperty("pagos").GetInt32());
        Assert.Equal(0, pags.GetProperty("pendentes").GetInt32());
        Assert.Equal(total, pags.GetProperty("totalRecebido").GetDecimal());
        var forma = Assert.Single(depois.GetProperty("formasPagamento").EnumerateArray());
        Assert.Equal("Pix", forma.GetProperty("forma").GetString());
        Assert.Equal(total, forma.GetProperty("valor").GetDecimal());
    }

    [Fact]
    public async Task Relatorio_exige_o_modulo_e_sem_pagamentos_devolve_dinheiro_nulo()
    {
        var (dia, _) = await AtendimentoNoDia();
        var painel = app.Api();
        var geral = await painel.LoginAdmin();

        var semModulo = await painel.AdminCom(geral, Permissao.So("dashboard"));
        Assert.Equal(403, (await painel.Get($"/api/admin/relatorio?token={semModulo}&de={dia}&ate={dia}")).Codigo);

        var soRelatorio = await painel.AdminCom(geral, Permissao.So("relatorios"));
        var d = await Relatorio(painel, soRelatorio, dia);

        Assert.True(d.GetProperty("restrito").GetBoolean());
        foreach (var campo in new[] { "entradas", "saidas", "saldo", "ticketMedio" })
            Assert.Equal(JsonValueKind.Null, d.GetProperty("cards").GetProperty(campo).ValueKind);
        Assert.Equal(1, d.GetProperty("cards").GetProperty("totalPagamentos").GetInt32());    // contagem continua
        Assert.Equal(JsonValueKind.Null, d.GetProperty("pagamentosResumo").ValueKind);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("variacao").ValueKind);
        Assert.Empty(d.GetProperty("formasPagamento").EnumerateArray());
        Assert.Empty(d.GetProperty("serie").EnumerateArray());
        Assert.Empty(d.GetProperty("topProdutos").EnumerateArray());
    }
}
