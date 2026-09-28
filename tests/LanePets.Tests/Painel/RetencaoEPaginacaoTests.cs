using LanePets.Models;
using LanePets.Services;
using LanePets.Tests.Infra;
using Microsoft.Extensions.DependencyInjection;

namespace LanePets.Tests.Painel;

/// <summary>
/// 26/09: paginacao no servidor da lista de Pagamentos (resumo continua do filtro inteiro) e
/// retencao do Log de eventos (12 meses, decisao do Fabricio).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class RetencaoEPaginacaoTests(LanePetsApp app)
{
    [Fact]
    public async Task Pagamentos_vem_em_paginas_sem_repetir_e_o_resumo_conta_o_filtro_inteiro()
    {
        var marca = Guid.NewGuid().ToString("N")[..8];
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet($"Cliente Pagina {marca}", "Bolota");
        var (servicoId, _) = await cliente.Servico();
        for (var i = 0; i < 3; i++)
            Assert.Equal(200, (await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), "10:00")).Codigo);
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        var filtro = $"/api/admin/pagamentos?token={token}&busca={marca}";

        var p1 = await painel.Get(filtro + "&limite=2&offset=0");
        var p2 = await painel.Get(filtro + "&limite=2&offset=2");

        Assert.True(p1.Codigo == 200, p1.ToString());
        Assert.Equal(3, p1.Data.GetProperty("total").GetInt32());
        Assert.Equal(3, p1.Data.GetProperty("resumo").GetProperty("total").GetInt32());   // resumo nao e so da pagina
        Assert.Equal(3, p1.Data.GetProperty("resumo").GetProperty("pendentes").GetInt32());
        Assert.True(p1.Data.GetProperty("temMais").GetBoolean());
        Assert.False(p2.Data.GetProperty("temMais").GetBoolean());
        var ids = p1.Data.GetProperty("itens").EnumerateArray().Concat(p2.Data.GetProperty("itens").EnumerateArray())
            .Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Equal(3, ids.Count);
        Assert.Equal(3, ids.Distinct().Count());                                           // nada repetido nem pulado

        // Sem limite: pagina padrao de 50; limite fora da faixa e ajustado, nunca erro.
        var padrao = await painel.Get(filtro);
        Assert.Equal(50, padrao.Data.GetProperty("limite").GetInt32());
        Assert.Equal(3, padrao.Data.GetProperty("itens").GetArrayLength());
        Assert.Equal(500, (await painel.Get(filtro + "&limite=99999")).Data.GetProperty("limite").GetInt32());
    }

    [Fact]
    public async Task Retencao_apaga_so_evento_com_mais_de_12_meses_e_registra_a_limpeza()
    {
        var marca = Guid.NewGuid().ToString("N")[..8];
        var agora = DateTime.UtcNow;
        await app.NoBanco(async db =>
        {
            db.EventosLog.Add(new EventoLog { Id = "EVT-VELHO-" + marca, DataHora = agora.AddMonths(-13), Categoria = "painel", Acao = "Teste retencao", Detalhes = marca });
            db.EventosLog.Add(new EventoLog { Id = "EVT-NOVO-" + marca, DataHora = agora.AddMonths(-11), Categoria = "painel", Acao = "Teste retencao", Detalhes = marca });
            await db.SaveChangesAsync();
            return 0;
        });

        var eventos = app.Services.GetRequiredService<EventosService>();
        var apagados = await app.NoBanco(db => RetencaoEventos.ExecutarAsync(db, eventos, agora));

        Assert.True(apagados >= 1, $"Esperava apagar ao menos 1, apagou {apagados}.");
        var restantes = await app.NoBanco(db => Task.FromResult(db.EventosLog.Where(e => e.Detalhes == marca).Select(e => e.Id).ToList()));
        Assert.Equal(new[] { "EVT-NOVO-" + marca }, restantes);
        var limpeza = await app.NoBanco(db => Task.FromResult(db.EventosLog.Where(e => e.Acao == "Eventos antigos removidos").ToList()));
        Assert.Contains(limpeza, e => e.Categoria == "sistema" && e.Detalhes.Contains("12 meses"));

        // Segunda passada no mesmo instante: nada mais para apagar.
        Assert.Equal(0, await app.NoBanco(db => RetencaoEventos.ExecutarAsync(db, eventos, agora)));
    }
}
