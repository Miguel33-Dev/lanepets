using System.Text.Json;
using LanePets.Models;
using LanePets.Services;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Operacao;

/// <summary>
/// Cobranca mensal do Seguro Pet (26/09). Decisoes do Fabricio: a mensalidade de cada mes
/// e gerada sozinha; cada mes e um Pagamento (origem "seguro" + Competencia); mes em aberto
/// ha mais de <see cref="MensalidadesSeguro.DiasTolerancia"/> dias deixa o seguro
/// Inadimplente (sem cancelar) e pagar devolve a "Em dia". Regras: CONTEXTO §6.29.
///
/// O tempo passa "envelhecendo" o contrato no banco (data da contratacao para tras); a
/// leitura da conta/seguros roda a reconciliacao, que gera os meses que faltam.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class CobrancaMensalSeguroTests(LanePetsApp app)
{
    /// <summary>Cliente com pet e um seguro contratado por PIX. Devolve a api do cliente e o id do contrato.</summary>
    private async Task<(Api Cliente, string SeguroId)> SeguroContratado(string nome)
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet(nome: nome, pet: "Mel");
        var planos = await cliente.Get("/api/public/seguros");
        var planoId = planos.Data.EnumerateArray().First().GetProperty("id").GetString();
        var r = await cliente.Post("/api/cliente/seguros", new { planoId, petId, metodoPagamento = "PIX" });
        Assert.True(r.Codigo == 200, r.ToString());
        return (cliente, r.Texto("id"));
    }

    /// <summary>Leva a contratacao (e a 1a mensalidade) para o passado.</summary>
    private Task Envelhecer(string seguroId, int meses, int dias) => app.NoBanco(async db =>
    {
        var s = db.SolicitacoesSeguro.Single(x => x.Id == seguroId);
        s.CriadoEm = DateTime.UtcNow.AddMonths(-meses).AddDays(-dias);
        var primeira = db.Pagamentos.Single(p => p.Origem == "seguro" && p.OrigemId == seguroId);
        primeira.Competencia = MensalidadesSeguro.Competencia(s.CriadoEm);
        primeira.DataReferencia = MensalidadesSeguro.DataReferencia(s.CriadoEm);
        await db.SaveChangesAsync();
        return 0;
    });

    private Task<List<Pagamento>> Mensalidades(string seguroId) => app.NoBanco(db => Task.FromResult(
        db.Pagamentos.Where(p => p.Origem == "seguro" && p.OrigemId == seguroId).OrderBy(p => p.Competencia).ToList()));

    /// <summary>Bloco "cobranca" do contrato como a area do cliente recebe.</summary>
    private static async Task<JsonElement> Cobranca(Api cliente, string seguroId)
    {
        var r = await cliente.Get("/api/cliente/seguros");
        Assert.True(r.Codigo == 200, r.ToString());
        return r.Data.EnumerateArray().Single(s => s.GetProperty("id").GetString() == seguroId).GetProperty("cobranca");
    }

    private static async Task Aprovar(Api painel, string token, string pagamentoId)
    {
        var r = await painel.Post($"/api/admin/pagamentos/{pagamentoId}/status", new { token, status = "Aprovado" });
        Assert.True(r.Codigo == 200, r.ToString());
    }

    [Fact]
    public async Task Gera_uma_mensalidade_por_mes_sem_duplicar_e_fica_inadimplente_depois_da_tolerancia()
    {
        var (cliente, id) = await SeguroContratado("Cliente Mensal");
        await Envelhecer(id, meses: 2, dias: 5);

        var cobranca = await Cobranca(cliente, id);
        await Cobranca(cliente, id);                       // segunda leitura: nada novo

        var meses = await Mensalidades(id);
        Assert.Equal(3, meses.Count);
        Assert.Equal(3, meses.Select(m => m.Competencia).Distinct().Count());
        Assert.All(meses, m => Assert.Equal("Pendente", m.Status));
        Assert.All(meses, m => Assert.Contains(MensalidadesSeguro.CompetenciaTexto(m.Competencia), m.Descricao));
        Assert.Equal("Inadimplente", cobranca.GetProperty("situacao").GetString());
        Assert.Equal(3, cobranca.GetProperty("emAberto").GetInt32());
        Assert.True(cobranca.GetProperty("diasEmAtraso").GetInt32() > MensalidadesSeguro.DiasTolerancia);

        // Inadimplente nao cancela o contrato.
        var contrato = await app.NoBanco(db => Task.FromResult(db.SolicitacoesSeguro.Single(s => s.Id == id)));
        Assert.NotEqual("Cancelada", contrato.Status);
    }

    [Fact]
    public async Task Pagar_as_mensalidades_devolve_o_seguro_a_em_dia_e_espelha_Pago()
    {
        var (cliente, id) = await SeguroContratado("Cliente Regulariza");
        await Envelhecer(id, meses: 1, dias: 20);
        await Cobranca(cliente, id);

        var painel = app.Api();
        var token = await painel.LoginAdmin();
        foreach (var m in await Mensalidades(id)) await Aprovar(painel, token, m.Id);

        var cobranca = await Cobranca(cliente, id);
        Assert.Equal("Em dia", cobranca.GetProperty("situacao").GetString());
        Assert.Equal(0, cobranca.GetProperty("emAberto").GetInt32());
        Assert.Equal("Pago", await app.NoBanco(db => Task.FromResult(db.SolicitacoesSeguro.Single(s => s.Id == id).PagamentoStatus)));

        // O painel ve a mesma situacao na lista de contratos.
        var lista = await painel.Get($"/api/admin/seguros/solicitacoes?token={token}");
        var noPainel = lista.Data.EnumerateArray().Single(s => s.GetProperty("id").GetString() == id);
        Assert.Equal("Em dia", noPainel.GetProperty("cobranca").GetProperty("situacao").GetString());
    }

    [Fact]
    public async Task Mensalidade_vencida_dentro_da_tolerancia_fica_aguardando_pagamento()
    {
        var (cliente, id) = await SeguroContratado("Cliente Tolerancia");
        await Envelhecer(id, meses: 1, dias: 3);
        await Cobranca(cliente, id);
        var painel = app.Api();
        await Aprovar(painel, await painel.LoginAdmin(), (await Mensalidades(id)).First().Id);   // paga o 1o mes

        var cobranca = await Cobranca(cliente, id);

        Assert.Equal("Aguardando pagamento", cobranca.GetProperty("situacao").GetString());
        Assert.Equal(1, cobranca.GetProperty("emAberto").GetInt32());
        // A nova mensalidade nasce Pendente: nao herda o "Pago" do mes anterior.
        Assert.Equal("Pendente", (await Mensalidades(id)).Last().Status);
    }

    [Fact]
    public async Task Cancelar_cancela_o_mes_em_aberto_reembolso_so_do_mes_em_curso_e_para_de_gerar()
    {
        var (cliente, id) = await SeguroContratado("Cliente Cancela Mensal");
        await Envelhecer(id, meses: 2, dias: 5);
        await Cobranca(cliente, id);
        var meses = await Mensalidades(id);
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        await Aprovar(painel, token, meses[0].Id);          // mes 1 pago
        await Aprovar(painel, token, meses[2].Id);          // mes 3 (em curso) pago; mes 2 em aberto

        var r = await cliente.Post($"/api/cliente/seguros/{id}/cancelar");
        Assert.True(r.Codigo == 200, r.ToString());

        var depois = await Mensalidades(id);
        Assert.Equal(3, depois.Count);
        Assert.Equal("Aprovado", depois[0].Status);
        Assert.False(depois[0].ReembolsoPendente);          // mes ja usufruido nao vira reembolso
        Assert.Equal("Cancelado", depois[1].Status);
        Assert.Equal("Aprovado", depois[2].Status);
        Assert.True(depois[2].ReembolsoPendente);           // mes em curso: equipe decide o reembolso

        var cobranca = await Cobranca(cliente, id);
        Assert.Equal("Encerrado", cobranca.GetProperty("situacao").GetString());
        Assert.Equal(3, (await Mensalidades(id)).Count);
    }

    [Fact]
    public void Contrato_cancelado_para_de_gerar_na_data_do_cancelamento()
    {
        var inicio = new DateTime(2026, 1, 31, 12, 0, 0);
        var seguro = new SolicitacaoSeguro { ClienteId = "C1", Valor = 19.9m, CriadoEm = inicio };
        var seisMesesDepois = inicio.AddMonths(6);

        var ativo = MensalidadesSeguro.Devidas(seguro, seisMesesDepois);
        Assert.Equal(7, ativo.Count);
        Assert.Equal(new DateTime(2026, 2, 28, 12, 0, 0), ativo[1].Vencimento);   // mes curto: ultimo dia
        Assert.Equal(new DateTime(2026, 3, 31, 12, 0, 0), ativo[2].Vencimento);   // contado da contratacao

        seguro.Status = "Cancelada";
        seguro.DataCancelamento = inicio.AddMonths(2).AddDays(1);
        Assert.Equal(new[] { "2026-01", "2026-02", "2026-03" }, MensalidadesSeguro.Devidas(seguro, seisMesesDepois).Select(m => m.Competencia));

        seguro.DataCancelamento = null;                     // cancelamento antigo, sem data
        Assert.Single(MensalidadesSeguro.Devidas(seguro, seisMesesDepois));
    }

    [Fact]
    public async Task Pagamento_antigo_sem_competencia_vira_o_primeiro_mes_sem_duplicar()
    {
        var (cliente, id) = await SeguroContratado("Cliente Legado");
        var original = (await Mensalidades(id)).Single();
        await app.NoBanco(async db =>
        {
            db.Pagamentos.Single(p => p.Id == original.Id).Competencia = "";
            await db.SaveChangesAsync();
            return 0;
        });

        await Cobranca(cliente, id);

        var meses = await Mensalidades(id);
        var unico = Assert.Single(meses);
        Assert.Equal(original.Id, unico.Id);
        Assert.Equal(original.Competencia, unico.Competencia);
    }
}
