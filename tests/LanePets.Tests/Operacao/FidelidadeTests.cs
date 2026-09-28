using LanePets.Services;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Operacao;

/// <summary>
/// 29/09: cartao fidelidade. Selo = atendimento Concluido; 10 selos = 1 premio; a equipe marca a entrega
/// (clientes:editar) e so o resgate e gravado. Nada de selo por agendamento que nao foi concluido.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class FidelidadeTests(LanePetsApp app)
{
    private async Task<string> Concluir(Api cliente, string petId, string servicoId, string token, Api painel)
    {
        var ag = (await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), "16:00")).Data;
        var s = await painel.Sync(token, "agendamentos", atualizados: [Cenarios.ItemPainel(ag, "Concluído")]);
        Assert.True(s.Codigo == 200, s.ToString());
        return ag.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Selo_so_vem_de_atendimento_concluido_e_sem_cartao_completo_nao_resgata()
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet("Tutor Selos", "Selinho");
        var (servicoId, _) = await cliente.Servico();
        var painel = app.Api();
        var token = await painel.LoginAdmin();

        await Concluir(cliente, petId, servicoId, token, painel);
        await Concluir(cliente, petId, servicoId, token, painel);
        await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), "16:00");            // Solicitado: nao conta

        var f = (await cliente.Get("/api/cliente/conta")).Data.GetProperty("fidelidade");
        Assert.Equal(FidelidadeService.SelosPadrao, f.GetProperty("selos").GetInt32());
        Assert.Equal(2, f.GetProperty("concluidos").GetInt32());
        Assert.Equal(2, f.GetProperty("selosNoCartao").GetInt32());
        Assert.Equal(0, f.GetProperty("premiosDisponiveis").GetInt32());
        Assert.Equal(8, f.GetProperty("faltamParaProximo").GetInt32());

        var clienteId = (await cliente.Get("/api/cliente/conta")).Data.GetProperty("id").GetString();
        var r = await painel.Post($"/api/admin/clientes/{clienteId}/fidelidade/resgatar", new { token });
        Assert.Equal(400, r.Codigo);
        Assert.Equal(FidelidadeService.SemPremio, r.Erro);
    }

    [Fact]
    public async Task Cartao_completo_vira_premio_que_a_equipe_entrega_uma_vez()
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet("Tutor Fiel", "Fiel");
        var (servicoId, _) = await cliente.Servico();
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        for (var i = 0; i < FidelidadeService.SelosPadrao; i++) await Concluir(cliente, petId, servicoId, token, painel);

        var conta = (await cliente.Get("/api/cliente/conta")).Data;
        var clienteId = conta.GetProperty("id").GetString()!;
        Assert.Equal(1, conta.GetProperty("fidelidade").GetProperty("premiosDisponiveis").GetInt32());

        var detalhe = await painel.Get($"/api/admin/clientes/{clienteId}?token={token}");
        Assert.True(detalhe.Data.GetProperty("podeResgatarFidelidade").GetBoolean());
        Assert.Equal(1, detalhe.Data.GetProperty("fidelidade").GetProperty("premiosDisponiveis").GetInt32());

        // Quem so VE clientes nao marca a entrega.
        var soVer = await painel.AdminCom(token, Permissao.So("clientes"));
        Assert.Equal(403, (await painel.Post($"/api/admin/clientes/{clienteId}/fidelidade/resgatar", new { token = soVer })).Codigo);

        var ok = await painel.Post($"/api/admin/clientes/{clienteId}/fidelidade/resgatar", new { token });
        Assert.True(ok.Codigo == 200, ok.ToString());
        var depois = (await cliente.Get("/api/cliente/conta")).Data.GetProperty("fidelidade");
        Assert.Equal(0, depois.GetProperty("premiosDisponiveis").GetInt32());
        Assert.Equal(1, depois.GetProperty("resgatados").GetInt32());
        Assert.Equal(0, depois.GetProperty("selosNoCartao").GetInt32());
        Assert.Equal(400, (await painel.Post($"/api/admin/clientes/{clienteId}/fidelidade/resgatar", new { token })).Codigo);   // uma vez so
    }
}
