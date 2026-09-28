using System.Text.Json;
using LanePets.Tests.Infra;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Tests.Operacao;

/// <summary>
/// 29/09 (melhoria pedida pelo Fabricio): o cliente escolhe VARIOS servicos num agendamento so, no mesmo horario.
/// `servicoIds` e o campo novo; `servicoId` sozinho continua funcionando (telas e testes antigos).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class AgendamentoVariosServicosTests(LanePetsApp app)
{
    private static async Task<List<(string Id, string Nome, decimal Preco)>> Servicos(Api api, int quantos)
    {
        var catalogo = await api.Get("/api/cliente/catalogo");
        return catalogo.Data.GetProperty("servicos").EnumerateArray()
            .Where(s => s.GetProperty("preco").GetDecimal() > 0)
            .Take(quantos)
            .Select(s => (s.GetProperty("id").GetString()!, s.GetProperty("nome").GetString()!, s.GetProperty("preco").GetDecimal()))
            .ToList();
    }

    private static Task<Resposta> Agendar(Api api, string petId, object servicoIds, string data, string horario = "10:00")
        => api.Post("/api/cliente/agendamentos", new
        {
            petId, servicoIds, unidade = Cenarios.Franco, data, horario,
            transporte = "Cliente leva", formaPagamento = "Pix", observacao = "Varios servicos"
        });

    [Fact]
    public async Task Dois_servicos_viram_um_agendamento_com_o_total_somado_e_um_pagamento()
    {
        var api = app.Api();
        var (_, petId) = await api.ClienteComPet();
        var s = await Servicos(api, 2);

        var r = await Agendar(api, petId, new[] { s[0].Id, s[1].Id }, Cenarios.NovaData());

        Assert.True(r.Codigo == 200, r.ToString());
        var total = s[0].Preco + s[1].Preco;
        Assert.Equal(total, r.Data.GetProperty("total").GetDecimal());
        var id = r.Texto("id");

        var gravado = await app.NoBanco(db => db.Agendamentos.AsNoTracking().SingleAsync(a => a.Id == id));
        var itens = JsonDocument.Parse(gravado.ServicosJson).RootElement.EnumerateArray().ToList();
        Assert.Equal(new[] { s[0].Id, s[1].Id }, itens.Select(i => i.GetProperty("id").GetString()));
        Assert.Equal(new[] { s[0].Nome, s[1].Nome }, itens.Select(i => i.GetProperty("nome").GetString()));
        Assert.Equal(0, gravado.ValorTransporte);

        var pagamentos = await app.NoBanco(db => db.Pagamentos.AsNoTracking().Where(p => p.Origem == "agendamento" && p.OrigemId == id).ToListAsync());
        var pagamento = Assert.Single(pagamentos);
        Assert.Equal(total, pagamento.Valor);
    }

    [Fact]
    public async Task Servico_repetido_conta_uma_vez_e_transporte_soma_por_cima()
    {
        var api = app.Api();
        var (_, petId) = await api.ClienteComPet();
        var s = (await Servicos(api, 1))[0];

        var r = await api.Post("/api/cliente/agendamentos", new
        {
            petId, servicoIds = new[] { s.Id, s.Id }, unidade = Cenarios.Franco, data = Cenarios.NovaData(), horario = "11:00",
            transporte = "Busca e entrega", formaPagamento = "Pix"
        });

        Assert.True(r.Codigo == 200, r.ToString());
        Assert.Equal(s.Preco + 30m, r.Data.GetProperty("total").GetDecimal());   // 2 pernas de R$ 15
        var gravado = await app.NoBanco(db => db.Agendamentos.AsNoTracking().SingleAsync(a => a.Id == r.Texto("id")));
        Assert.Single(JsonDocument.Parse(gravado.ServicosJson).RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Sem_servico_com_servico_inexistente_ou_acima_do_limite_e_recusado_sem_gravar()
    {
        var api = app.Api();
        var (_, petId) = await api.ClienteComPet();
        var s = await Servicos(api, 7);
        var data = Cenarios.NovaData();

        var vazio = await Agendar(api, petId, Array.Empty<string>(), data);
        var inexistente = await Agendar(api, petId, new[] { s[0].Id, "SRV-NAO-EXISTE" }, data);
        var demais = await Agendar(api, petId, s.Select(x => x.Id).ToArray(), data);

        Assert.Equal(400, vazio.Codigo);
        Assert.Equal("Escolha pelo menos um serviço.", vazio.Erro);
        Assert.Equal(400, inexistente.Codigo);
        Assert.Equal("Serviço não localizado.", inexistente.Erro);
        Assert.Equal(400, demais.Codigo);
        Assert.Contains("no máximo 6", demais.Erro);
        Assert.Equal(0, (await api.Get("/api/cliente/conta")).Data.GetProperty("agendamentos").GetArrayLength());
    }

    [Fact]
    public async Task Servico_id_sozinho_continua_funcionando()
    {
        var api = app.Api();
        var (_, petId) = await api.ClienteComPet();
        var (servicoId, preco) = await api.Servico();

        var r = await api.Agendar(petId, servicoId, Cenarios.NovaData(), "13:00");

        Assert.True(r.Codigo == 200, r.ToString());
        Assert.Equal(preco, r.Data.GetProperty("total").GetDecimal());
    }
}
