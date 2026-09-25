using LanePets.Tests.Infra;

namespace LanePets.Tests.Operacao;

/// <summary>
/// Pagamentos (item 8 do roadmap): um Pagamento por origem, transicoes validas, espelho
/// "Pago" no agendamento, reembolso pendente quando a origem e cancelada depois de paga,
/// cliente so le. Regras: CONTEXTO §6.29 e §6.31.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class PagamentosTests(LanePetsApp app)
{
    private Task<LanePets.Models.Pagamento> PagamentoDe(string origemId)
        => app.NoBanco(db => Task.FromResult(db.Pagamentos.Single(p => p.OrigemId == origemId)));

    private static Task<Resposta> Status(Api api, string token, string pagamentoId, string status, string? observacao = null)
        => api.Post($"/api/admin/pagamentos/{pagamentoId}/status", new { token, status, observacao });

    /// <summary>Cliente com um agendamento marcado; devolve a api do cliente e o id do agendamento.</summary>
    private async Task<(Api Cliente, string AgendamentoId)> AgendamentoMarcado(string horario = "10:00")
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet("Cliente Pagamento", "Bento");
        var (servicoId, _) = await cliente.Servico();
        var r = await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), horario);
        Assert.True(r.Codigo == 200, r.ToString());
        return (cliente, r.Texto("id"));
    }

    [Fact]
    public async Task Aprovar_o_pagamento_espelha_Pago_no_agendamento()
    {
        var (_, agendamentoId) = await AgendamentoMarcado();
        var pagamento = await PagamentoDe(agendamentoId);
        var painel = app.Api();
        var token = await painel.LoginAdmin();

        var r = await Status(painel, token, pagamento.Id, "Aprovado", "Pix conferido");

        Assert.Equal(200, r.Codigo);
        Assert.Equal("Aprovado", r.Texto("status"));
        Assert.Equal("Pago", await app.NoBanco(db => Task.FromResult(db.Agendamentos.Single(a => a.Id == agendamentoId).PagamentoStatus)));

        var lista = await painel.Get($"/api/admin/pagamentos?token={token}&status=Aprovado&busca={agendamentoId}");
        var item = Assert.Single(lista.Data.GetProperty("itens").EnumerateArray());
        Assert.Equal("Pix conferido", item.GetProperty("observacao").GetString());
    }

    [Theory]
    [InlineData("Reembolsado", "Não é possível passar de Pendente para Reembolsado")]
    [InlineData("Pendente", "já está Pendente")]
    [InlineData("Pago na hora", "Status inválido")]
    public async Task Transicao_invalida_e_recusada(string para, string trecho)
    {
        var (_, agendamentoId) = await AgendamentoMarcado();
        var pagamento = await PagamentoDe(agendamentoId);
        var painel = app.Api();

        var r = await Status(painel, await painel.LoginAdmin(), pagamento.Id, para);

        Assert.Equal(400, r.Codigo);
        Assert.Contains(trecho, r.Erro);
        Assert.Equal("Pendente", (await PagamentoDe(agendamentoId)).Status);
    }

    [Fact]
    public async Task Cancelar_depois_de_pago_deixa_reembolso_pendente_ate_marcar_Reembolsado()
    {
        var (cliente, agendamentoId) = await AgendamentoMarcado();
        var pagamentoId = (await PagamentoDe(agendamentoId)).Id;
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        Assert.Equal(200, (await Status(painel, token, pagamentoId, "Aprovado")).Codigo);

        Assert.Equal(200, (await cliente.Post($"/api/cliente/agendamentos/{agendamentoId}/cancelar")).Codigo);

        var aposCancelar = await PagamentoDe(agendamentoId);
        Assert.Equal("Aprovado", aposCancelar.Status);           // o dinheiro entrou: nao some sozinho
        Assert.True(aposCancelar.ReembolsoPendente);

        var voltar = await Status(painel, token, pagamentoId, "Pendente");
        Assert.Equal(400, voltar.Codigo);
        Assert.Contains("Marque como Reembolsado", voltar.Erro);

        var reembolsar = await Status(painel, token, pagamentoId, "Reembolsado");
        Assert.Equal(200, reembolsar.Codigo);
        Assert.False(reembolsar.Data.GetProperty("reembolsoPendente").GetBoolean());

        var final = await Status(painel, token, pagamentoId, "Aprovado");
        Assert.Equal(400, final.Codigo);
        Assert.Contains("não muda mais", final.Erro);
    }

    [Fact]
    public async Task Recusado_pode_voltar_para_Pendente()
    {
        var (_, agendamentoId) = await AgendamentoMarcado();
        var pagamentoId = (await PagamentoDe(agendamentoId)).Id;
        var painel = app.Api();
        var token = await painel.LoginAdmin();

        Assert.Equal(200, (await Status(painel, token, pagamentoId, "Recusado")).Codigo);
        Assert.Equal(200, (await Status(painel, token, pagamentoId, "Pendente")).Codigo);
        Assert.Equal("Pendente", (await PagamentoDe(agendamentoId)).Status);
    }

    [Fact]
    public async Task Cliente_ve_o_status_do_pagamento_na_conta()
    {
        var (cliente, agendamentoId) = await AgendamentoMarcado();
        var pagamentoId = (await PagamentoDe(agendamentoId)).Id;
        var painel = app.Api();
        await Status(painel, await painel.LoginAdmin(), pagamentoId, "Aprovado");

        var conta = await cliente.Get("/api/cliente/conta");

        var meu = Assert.Single(conta.Data.GetProperty("pagamentos").EnumerateArray(), p => p.GetProperty("origemId").GetString() == agendamentoId);
        Assert.Equal("Aprovado", meu.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cliente_nao_ve_pagamento_de_outro_cliente()
    {
        var (_, agendamentoId) = await AgendamentoMarcado();
        var outro = app.Api();
        await outro.CadastrarCliente();

        var conta = await outro.Get("/api/cliente/conta");

        Assert.DoesNotContain(conta.Data.GetProperty("pagamentos").EnumerateArray(), p => p.GetProperty("origemId").GetString() == agendamentoId);
    }

    [Fact]
    public async Task Pagamentos_exigem_o_modulo_e_a_acao_certos()
    {
        var (_, agendamentoId) = await AgendamentoMarcado();
        var pagamentoId = (await PagamentoDe(agendamentoId)).Id;
        var painel = app.Api();
        var geral = await painel.LoginAdmin();
        var semModulo = await painel.AdminCom(geral, Permissao.So("clientes"));
        var leitor = await painel.AdminCom(geral, Permissao.So("pagamentos", visualizar: true));

        Assert.Equal(403, (await painel.Get($"/api/admin/pagamentos?token={semModulo}")).Codigo);
        Assert.Equal(200, (await painel.Get($"/api/admin/pagamentos?token={leitor}")).Codigo);
        Assert.Equal(403, (await Status(painel, leitor, pagamentoId, "Aprovado")).Codigo);
        Assert.Equal("Pendente", (await PagamentoDe(agendamentoId)).Status);
    }
}
