using System.Text.Json;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// 27/09: as telas que ainda usam o LaneStore (Produtos, Entradas e Saidas, Cadastro, Pacotes) pedem
/// so as colecoes que mostram (GET /api/admin/estado?colecoes=...), e as entradas automaticas dos
/// agendamentos sao somadas no servidor com o criterio do Dashboard (cancelado nao entra).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class EstadoParcialTests(LanePetsApp app)
{
    [Fact]
    public async Task Estado_com_colecoes_le_so_o_pedido_e_mantem_as_contagens()
    {
        var api = app.Api();
        var token = await api.LoginAdmin();
        await app.Api().CadastrarCliente();   // garante ao menos um cliente

        var parcial = await api.Get($"/api/admin/estado?token={token}&colecoes=produtos");
        Assert.True(parcial.Codigo == 200, parcial.ToString());
        Assert.True(parcial.Data.GetProperty("produtos").GetArrayLength() > 0);
        Assert.Equal(JsonValueKind.Null, parcial.Data.GetProperty("agendamentos").ValueKind);
        Assert.Equal(JsonValueKind.Null, parcial.Data.GetProperty("pets").ValueKind);
        Assert.Equal(0, parcial.Data.GetProperty("clientes").GetArrayLength());
        var clientesNoBanco = await app.NoBanco(db => Task.FromResult(db.Clientes.Count()));
        Assert.Equal(clientesNoBanco, parcial.Data.GetProperty("totais").GetProperty("clientes").GetInt32());   // contagem barata

        var cadastro = await api.Get($"/api/admin/estado?token={token}&colecoes=pets,servicos");
        Assert.Equal(JsonValueKind.Array, cadastro.Data.GetProperty("pets").ValueKind);
        Assert.True(cadastro.Data.GetProperty("servicos").GetArrayLength() > 0);
        Assert.Equal(0, cadastro.Data.GetProperty("produtos").GetArrayLength());

        // Sem o parametro: tudo, como antes.
        var tudo = await api.Get($"/api/admin/estado?token={token}");
        Assert.Equal(JsonValueKind.Array, tudo.Data.GetProperty("agendamentos").ValueKind);
        Assert.Equal(clientesNoBanco, tudo.Data.GetProperty("clientes").GetArrayLength());
    }

    [Fact]
    public async Task Entradas_automaticas_somam_so_agendamentos_nao_cancelados_do_periodo()
    {
        // Dois clientes no mesmo dia (horarios diferentes); um deles cancela.
        var site = app.Api();
        var (_, petId) = await site.ClienteComPet("Cliente Entradas", "Tico");
        var (servicoId, preco) = await site.Servico();
        var outro = app.Api();
        var (_, petOutro) = await outro.ClienteComPet("Cliente Cancela", "Teco");
        var dia = Cenarios.NovaData();
        var fica = await site.Agendar(petId, servicoId, dia, "10:00");
        var cancela = await outro.Agendar(petOutro, servicoId, dia, "11:00");
        Assert.True(fica.Codigo == 200 && cancela.Codigo == 200, fica + " / " + cancela);
        var cancelou = await outro.Post($"/api/cliente/agendamentos/{cancela.Texto("id")}/cancelar");
        Assert.True(cancelou.Codigo == 200, cancelou.ToString());

        var api = app.Api();
        var token = await api.LoginAdmin();
        var r = await api.Get($"/api/admin/entradas-saidas/automaticas?token={token}&de={dia}&ate={dia}");

        Assert.True(r.Codigo == 200, r.ToString());
        Assert.Equal(1, r.Data.GetProperty("agendamentos").GetInt32());
        var linha = Assert.Single(r.Data.GetProperty("itens").EnumerateArray());
        Assert.Equal(dia, linha.GetProperty("data").GetString());
        Assert.Equal("franco", linha.GetProperty("unidade").GetString());
        Assert.Equal(preco, linha.GetProperty("valor").GetDecimal());
        Assert.StartsWith("Serviços: ", linha.GetProperty("descricao").GetString());

        var semDinheiro = await api.AdminCom(token, Permissao.So("agendamentos"));
        Assert.Equal(403, (await api.Get($"/api/admin/entradas-saidas/automaticas?token={semDinheiro}")).Codigo);
    }
}
