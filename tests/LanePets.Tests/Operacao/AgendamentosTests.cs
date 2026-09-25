using LanePets.Tests.Infra;

namespace LanePets.Tests.Operacao;

/// <summary>
/// Agendamento pela area do cliente e pelo painel (itens 4, 5 e 8 do roadmap).
/// Regras: CONTEXTO §6.26 (capacidade por horario; unidade comparada pelo id, nao pelo
/// texto), §6.27 (status oficiais; cliente cancela so Solicitado/Confirmado), §6.29
/// (pagamento acompanha a origem), §6.5 (identidade vem da sessao).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class AgendamentosTests(LanePetsApp app)
{
    private Task<LanePets.Models.Pagamento> PagamentoDe(string origemId)
        => app.NoBanco(db => Task.FromResult(db.Pagamentos.Single(p => p.OrigemId == origemId)));

    private async Task<string[]> Horarios(Api api, string unidade, string data)
    {
        var r = await api.Get($"/api/cliente/horarios?unidade={Uri.EscapeDataString(unidade)}&data={data}");
        Assert.True(r.Codigo == 200, r.ToString());
        return r.Data.EnumerateArray().Select(h => h.GetString()!).ToArray();
    }

    // ------------------------------------------------------------------ criar

    [Fact]
    public async Task Agendamento_do_cliente_nasce_Solicitado_com_pagamento_pendente()
    {
        var api = app.Api();
        var (_, petId) = await api.ClienteComPet();
        var (servicoId, preco) = await api.Servico();
        var data = Cenarios.NovaData();

        var r = await api.Agendar(petId, servicoId, data, "10:00");

        Assert.Equal(200, r.Codigo);
        Assert.Equal("Solicitado", r.Texto("status"));
        Assert.Equal($"{data}T10:00:00", r.Texto("dataHora"));
        Assert.Equal(preco, r.Data.GetProperty("total").GetDecimal());

        var id = r.Texto("id");
        var conta = await api.Get("/api/cliente/conta");
        Assert.Contains(conta.Data.GetProperty("agendamentos").EnumerateArray(), a => a.GetProperty("id").GetString() == id);

        var pagamento = await PagamentoDe(id);
        Assert.Equal("agendamento", pagamento.Origem);
        Assert.Equal("Pendente", pagamento.Status);
        Assert.Equal(preco, pagamento.Valor);
        Assert.Equal(Cenarios.FrancoId, pagamento.Unidade);   // gravado pelo nome, pagamento pelo id
    }

    [Fact]
    public async Task Busca_e_entrega_soma_a_taxa_de_transporte_nos_dois_sentidos()
    {
        var api = app.Api();
        var (_, petId) = await api.ClienteComPet();
        var (servicoId, preco) = await api.Servico();

        var r = await api.Agendar(petId, servicoId, Cenarios.NovaData(), "11:00", transporte: "Busca e entrega");

        Assert.Equal(200, r.Codigo);
        Assert.Equal(preco + 30m, r.Data.GetProperty("total").GetDecimal());
        Assert.Equal(30m, r.Data.GetProperty("valorTransporte").GetDecimal());
    }

    [Fact]
    public async Task Horario_marcado_some_dos_horarios_livres()
    {
        var api = app.Api();
        var (_, petId) = await api.ClienteComPet();
        var (servicoId, _) = await api.Servico();
        var data = Cenarios.NovaData();
        Assert.Contains("10:00", await Horarios(api, Cenarios.Franco, data));

        await api.Agendar(petId, servicoId, data, "10:00");

        Assert.DoesNotContain("10:00", await Horarios(api, Cenarios.Franco, data));
        Assert.Contains("11:00", await Horarios(api, Cenarios.Franco, data));
        Assert.Contains("10:00", await Horarios(api, Cenarios.Caieiras, data));   // outra unidade, outra agenda
    }

    [Fact]
    public async Task Capacidade_da_unidade_recusa_o_segundo_pet_no_mesmo_horario()
    {
        var (servicoId, _) = await app.Api().Servico();
        var data = Cenarios.NovaData();
        var ana = app.Api();
        var (_, petAna) = await ana.ClienteComPet("Ana Agenda", "Luna");
        var beto = app.Api();
        var (_, petBeto) = await beto.ClienteComPet("Beto Agenda", "Toby");

        var primeiro = await ana.Agendar(petAna, servicoId, data, "15:00");
        var segundo = await beto.Agendar(petBeto, servicoId, data, "15:00");
        var outraUnidade = await beto.Agendar(petBeto, servicoId, data, "15:00", Cenarios.Caieiras);

        Assert.Equal(200, primeiro.Codigo);
        Assert.Equal(400, segundo.Codigo);
        Assert.Contains("acabou de ser ocupado", segundo.Erro);
        Assert.Equal(200, outraUnidade.Codigo);
    }

    [Fact]
    public async Task Painel_nao_marca_por_cima_do_horario_que_o_cliente_marcou()
    {
        // Incidente de 24/09: o cliente grava "Franco da Rocha", o painel "franco", e a
        // comparacao por texto deixava os dois marcarem o mesmo horario.
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet();
        var (servicoId, preco) = await cliente.Servico();
        var data = Cenarios.NovaData();
        Assert.Equal(200, (await cliente.Agendar(petId, servicoId, data, "14:00")).Codigo);

        var painel = app.Api();
        var idPainel = Api.NovoId("AGD");
        var r = await painel.Sync(await painel.LoginAdmin(), "agendamentos", criados:
        [
            new { id = idPainel, pet = "Encaixe", dono = "Balcão", telefone = "(11) 4000-0000", dataHora = $"{data}T14:00", total = preco, status = "Confirmado", unidade = Cenarios.FrancoId }
        ]);

        Assert.Equal(400, r.Codigo);
        Assert.False(await app.NoBanco(db => Task.FromResult(db.Agendamentos.Any(a => a.Id == idPainel))));
    }

    [Theory]
    [InlineData("pet-de-ninguem", "2031-01-10", "10:00", "Pet não localizado")]
    [InlineData(null, "31/02/2031", "10:00", "data e horário válidos")]
    [InlineData(null, "2031-01-10", "25:99", "data e horário válidos")]
    public async Task Agendamento_invalido_e_recusado(string? petId, string data, string horario, string trecho)
    {
        var api = app.Api();
        var (_, meuPet) = await api.ClienteComPet();
        var (servicoId, _) = await api.Servico();

        var r = await api.Agendar(petId ?? meuPet, servicoId, data, horario);

        Assert.Equal(400, r.Codigo);
        Assert.Contains(trecho, r.Erro);
    }

    [Fact]
    public async Task Cliente_nao_agenda_para_o_pet_de_outro_cliente()
    {
        var dono = app.Api();
        var (_, petDoOutro) = await dono.ClienteComPet("Dono Real", "Fred");
        var intruso = app.Api();
        await intruso.ClienteComPet("Intruso", "Zeca");
        var (servicoId, _) = await intruso.Servico();

        var r = await intruso.Agendar(petDoOutro, servicoId, Cenarios.NovaData(), "09:00");

        Assert.Equal(400, r.Codigo);
        Assert.Equal("Pet não localizado.", r.Erro);
    }

    [Fact]
    public async Task Sem_sessao_nao_agenda()
    {
        var r = await app.Api().Agendar("qualquer", "qualquer", Cenarios.NovaData(), "10:00");

        Assert.Equal(401, r.Codigo);
    }

    // ------------------------------------------------------------------ cancelar

    [Fact]
    public async Task Cliente_cancela_Solicitado_libera_o_horario_e_cancela_o_pagamento()
    {
        var api = app.Api();
        var (_, petId) = await api.ClienteComPet();
        var (servicoId, _) = await api.Servico();
        var data = Cenarios.NovaData();
        var id = (await api.Agendar(petId, servicoId, data, "16:00")).Texto("id");

        var r = await api.Post($"/api/cliente/agendamentos/{id}/cancelar");

        Assert.Equal(200, r.Codigo);
        Assert.Equal("Cancelado", r.Texto("status"));
        Assert.Contains("16:00", await Horarios(api, Cenarios.Franco, data));
        var pagamento = await PagamentoDe(id);
        Assert.Equal("Cancelado", pagamento.Status);
        Assert.False(pagamento.ReembolsoPendente);

        var deNovo = await api.Post($"/api/cliente/agendamentos/{id}/cancelar");
        Assert.Equal(400, deNovo.Codigo);
        Assert.Equal("Este agendamento já está cancelado.", deNovo.Erro);
    }

    [Theory]
    [InlineData("Confirmado", true, "")]
    [InlineData("Em andamento", false, "já começou")]
    [InlineData("Concluído", false, "concluídos não podem")]
    public async Task Cliente_so_cancela_antes_do_atendimento_comecar(string statusNoPainel, bool podeCancelar, string trecho)
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet();
        var (servicoId, _) = await cliente.Servico();
        var agendamento = (await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), "13:00")).Data;
        var id = agendamento.GetProperty("id").GetString()!;

        var painel = app.Api();
        var sync = await painel.Sync(await painel.LoginAdmin(), "agendamentos", atualizados: [Cenarios.ItemPainel(agendamento, statusNoPainel)]);
        Assert.True(sync.Codigo == 200, sync.ToString());

        var r = await cliente.Post($"/api/cliente/agendamentos/{id}/cancelar");

        Assert.Equal(podeCancelar ? 200 : 400, r.Codigo);
        if (!podeCancelar) Assert.Contains(trecho, r.Erro);
    }

    [Fact]
    public async Task Painel_recusa_status_desconhecido_e_nao_muda_nada()
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet();
        var (servicoId, _) = await cliente.Servico();
        var agendamento = (await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), "17:00")).Data;
        var id = agendamento.GetProperty("id").GetString()!;

        var painel = app.Api();
        var r = await painel.Sync(await painel.LoginAdmin(), "agendamentos", atualizados: [Cenarios.ItemPainel(agendamento, "Sumiu")]);

        Assert.Equal(400, r.Codigo);
        Assert.Contains("inválido", r.Erro);
        Assert.Equal("Solicitado", await app.NoBanco(db => Task.FromResult(db.Agendamentos.Single(a => a.Id == id).Status)));
    }

    [Fact]
    public async Task Cliente_nao_cancela_agendamento_de_outro_cliente()
    {
        var dono = app.Api();
        var (_, petId) = await dono.ClienteComPet("Dono Agenda", "Amora");
        var (servicoId, _) = await dono.Servico();
        var id = (await dono.Agendar(petId, servicoId, Cenarios.NovaData(), "09:00")).Texto("id");
        var intruso = app.Api();
        await intruso.CadastrarCliente();

        var r = await intruso.Post($"/api/cliente/agendamentos/{id}/cancelar");

        Assert.Equal(400, r.Codigo);
        Assert.Equal("Solicitado", await app.NoBanco(db => Task.FromResult(db.Agendamentos.Single(a => a.Id == id).Status)));
    }
}
