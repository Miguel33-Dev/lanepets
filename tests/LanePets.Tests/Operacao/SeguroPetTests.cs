using System.Text.Json;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Operacao;

/// <summary>
/// Seguro Pet pela area do cliente (POST /api/cliente/seguros e /{id}/cancelar).
/// Regras: CONTEXTO §6.5 (cliente vem da sessao; pet precisa ser da conta), §6.10 (cancelar
/// nao apaga), §6.29 (um pagamento por contratacao; origem cancelada: Pendente → Cancelado),
/// valor vem do catalogo e do cartao so chegam os 4 ultimos digitos.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class SeguroPetTests(LanePetsApp app)
{
    private static async Task<(string Id, decimal Valor)> PlanoDoCatalogo(Api api)
    {
        var r = await api.Get("/api/public/seguros");
        Assert.True(r.Codigo == 200, r.ToString());
        var plano = r.Data.EnumerateArray().First();
        return (plano.GetProperty("id").GetString()!, plano.GetProperty("valorMensal").GetDecimal());
    }

    private Task<LanePets.Models.Pagamento?> PagamentoDo(string seguroId)
        => app.NoBanco(db => Task.FromResult(db.Pagamentos.FirstOrDefault(p => p.Origem == "seguro" && p.OrigemId == seguroId)));

    [Fact]
    public async Task Contratar_com_pix_grava_valor_do_catalogo_e_pagamento_pendente_sem_duplicar()
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet(nome: "Cliente Seguro", pet: "Toby");
        var (planoId, valor) = await PlanoDoCatalogo(cliente);

        var r = await cliente.Post("/api/cliente/seguros", new { planoId, petId, metodoPagamento = "PIX" });

        Assert.True(r.Codigo == 200, r.ToString());
        Assert.Equal("Pendente", r.Texto("status"));
        Assert.Equal(valor, r.Data.GetProperty("valor").GetDecimal());
        var pagamento = await PagamentoDo(r.Texto("id")) ?? throw new Xunit.Sdk.XunitException("Contratação sem pagamento.");
        Assert.Equal("Pendente", pagamento.Status);
        Assert.Equal(valor, pagamento.Valor);

        var deNovo = await cliente.Post("/api/cliente/seguros", new { planoId, petId, metodoPagamento = "PIX" });
        Assert.Equal(400, deNovo.Codigo);
        Assert.Contains("em andamento", deNovo.Erro);
    }

    [Theory]
    [InlineData("12", false)]
    [InlineData("4242 4242 4242 1234", true)]   // numero inteiro: so os 4 ultimos sao guardados
    [InlineData("1234", true)]
    public async Task Do_cartao_so_os_quatro_ultimos_digitos_sao_gravados(string cartaoFinal, bool aceita)
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet(nome: "Cliente Cartao", pet: "Duque");
        var (planoId, _) = await PlanoDoCatalogo(cliente);

        var r = await cliente.Post("/api/cliente/seguros", new { planoId, petId, metodoPagamento = "Cartão de crédito", cartaoFinal });

        if (aceita)
        {
            Assert.True(r.Codigo == 200, r.ToString());
            Assert.Equal("1234", r.Texto("cartaoFinal"));
            var gravado = await app.NoBanco(db => Task.FromResult(db.SolicitacoesSeguro.Single(s => s.PetId == petId).CartaoFinal));
            Assert.Equal("1234", gravado);
        }
        else
        {
            Assert.Equal(400, r.Codigo);
            var gravados = await app.NoBanco(db => Task.FromResult(db.SolicitacoesSeguro.Count(s => s.PetId == petId)));
            Assert.Equal(0, gravados);
        }
    }

    [Fact]
    public async Task Pet_de_outra_conta_e_recusado()
    {
        var dono = app.Api();
        var (_, petAlheio) = await dono.ClienteComPet(nome: "Dono Verdadeiro", pet: "Alheio");
        var intruso = app.Api();
        await intruso.CadastrarCliente(nome: "Outro Cliente", pet: "Meu");
        var (planoId, _) = await PlanoDoCatalogo(intruso);

        var r = await intruso.Post("/api/cliente/seguros", new { planoId, petId = petAlheio, metodoPagamento = "PIX" });

        Assert.Equal(400, r.Codigo);
        Assert.Contains("Pet nao localizado", r.Erro);
    }

    [Fact]
    public async Task Cancelar_mantem_o_registro_cancela_o_pagamento_e_so_o_dono_cancela()
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet(nome: "Cliente Cancela", pet: "Lola");
        var (planoId, _) = await PlanoDoCatalogo(cliente);
        var id = (await cliente.Post("/api/cliente/seguros", new { planoId, petId, metodoPagamento = "PIX" })).Texto("id");

        var outro = app.Api();
        await outro.CadastrarCliente(nome: "Cliente Alheio", pet: "Nada");
        var alheio = await outro.Post($"/api/cliente/seguros/{id}/cancelar");
        Assert.Equal(400, alheio.Codigo);

        var r = await cliente.Post($"/api/cliente/seguros/{id}/cancelar");
        Assert.True(r.Codigo == 200, r.ToString());
        Assert.Equal("Cancelada", r.Texto("status"));

        var registro = await app.NoBanco(db => Task.FromResult(db.SolicitacoesSeguro.SingleOrDefault(s => s.Id == id)));
        Assert.NotNull(registro);                       // cancelar != excluir
        Assert.NotNull(registro!.DataCancelamento);
        Assert.Equal("Cancelado", (await PagamentoDo(id))!.Status);

        var deNovo = await cliente.Post($"/api/cliente/seguros/{id}/cancelar");
        Assert.Equal(400, deNovo.Codigo);
        Assert.Contains("ja esta cancelado", deNovo.Erro);
    }
}
