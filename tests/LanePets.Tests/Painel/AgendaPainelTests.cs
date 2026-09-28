using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// 27/09: tela Agendamentos sem o /api/admin/estado (GET /api/admin/agenda). Aba = unidade, filtros,
/// ordem e pagina no servidor; indicadores e abas sobre a base inteira; detalhe com pet e cliente so
/// para quem pode ver; horarios lotados pela capacidade. Regras: §6.19, §6.26, §6.27.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class AgendaPainelTests(LanePetsApp app)
{
    private static int Total(Resposta r) => r.Data.GetProperty("total").GetInt32();

    /// <summary>Um cliente do site (nome marcado) agenda N vezes em Franco, em datas crescentes.</summary>
    private async Task<(string Marca, List<string> Ids, List<string> Datas)> Agendados(int quantos)
    {
        var marca = Guid.NewGuid().ToString("N")[..8];
        var site = app.Api();
        var (_, petId) = await site.ClienteComPet($"Tutor {marca}", "Faísca");
        var (servicoId, _) = await site.Servico();
        var ids = new List<string>();
        var datas = new List<string>();
        for (var i = 0; i < quantos; i++)
        {
            var data = Cenarios.NovaData();
            var r = await site.Agendar(petId, servicoId, data, "10:00");
            Assert.True(r.Codigo == 200, r.ToString());
            ids.Add(r.Texto("id"));
            datas.Add(data);
        }
        return (marca, ids, datas);
    }

    [Fact]
    public async Task Agenda_da_unidade_vem_paginada_ordenada_e_filtrada_no_servidor()
    {
        var (marca, ids, datas) = await Agendados(3);
        var api = app.Api();
        var token = await api.LoginAdmin();
        var url = $"/api/admin/agenda?token={token}&unidade=franco&busca={marca}";

        var p1 = await api.Get(url + "&limite=2&offset=0");
        var p2 = await api.Get(url + "&limite=2&offset=2");

        Assert.True(p1.Codigo == 200, p1.ToString());
        Assert.Equal(3, Total(p1));
        Assert.True(p1.Data.GetProperty("temMais").GetBoolean());
        var vistos = p1.Data.GetProperty("itens").EnumerateArray().Concat(p2.Data.GetProperty("itens").EnumerateArray())
            .Select(i => i.GetProperty("agendamento").GetProperty("id").GetString() ?? "").ToList();
        Assert.Equal(ids, vistos);                                                       // data crescente, sem repetir
        var desc = await api.Get(url + "&ordem=dataHora&dir=desc");
        Assert.Equal(ids[2], desc.Data.GetProperty("itens")[0].GetProperty("agendamento").GetProperty("id").GetString());
        Assert.Equal(12, desc.Data.GetProperty("limite").GetInt32());                    // pagina da tela

        var primeiro = p1.Data.GetProperty("itens")[0];
        Assert.Equal("franco", primeiro.GetProperty("agendamento").GetProperty("unidade").GetString());   // formato do sync
        Assert.Equal("Solicitado", primeiro.GetProperty("agendamento").GetProperty("status").GetString());
        Assert.Equal("Cachorro", primeiro.GetProperty("extra").GetProperty("petTipo").GetString());

        // Indicadores e abas sobre a base inteira, nao so o filtro.
        Assert.True(p1.Data.GetProperty("kpis").GetProperty("Solicitado").GetInt32() >= 3);
        Assert.True(p1.Data.GetProperty("unidades").GetProperty("franco").GetProperty("total").GetInt32() >= 3);
        Assert.Contains(p1.Data.GetProperty("servicosUsados").EnumerateArray(), s => s.GetString()!.Length > 0);

        // Janela do calendario, status, outra aba.
        Assert.Equal(1, Total(await api.Get(url + $"&janelaDe={datas[1]}&janelaAte={datas[1]}&limite=500")));
        Assert.Equal(2, Total(await api.Get(url + $"&de={datas[1]}")));
        Assert.Equal(0, Total(await api.Get(url + "&status=Confirmado")));
        Assert.Equal(3, Total(await api.Get(url + "&status=solicitado")));
        Assert.Equal(3, Total(await api.Get(url + "&responsavel=__sem")));
        Assert.Equal(0, Total(await api.Get($"/api/admin/agenda?token={token}&unidade=caieiras&busca={marca}")));
    }

    [Fact]
    public async Task Detalhe_traz_pet_e_cliente_so_para_quem_pode_ver_e_funcionario_so_a_unidade_dele()
    {
        var (_, ids, _) = await Agendados(1);
        var api = app.Api();
        var geral = await api.LoginAdmin();

        var d = await api.Get($"/api/admin/agenda/{ids[0]}?token={geral}");
        Assert.True(d.Codigo == 200, d.ToString());
        Assert.Equal("Faísca", d.Data.GetProperty("pet").GetProperty("pet").GetString());
        Assert.Contains("@exemplo.com", d.Data.GetProperty("cliente").GetProperty("email").GetString());

        var soAgenda = await api.AdminCom(geral, Permissao.So("agendamentos"));
        var restrito = await api.Get($"/api/admin/agenda/{ids[0]}?token={soAgenda}");
        Assert.True(restrito.Codigo == 200, restrito.ToString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, restrito.Data.GetProperty("pet").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, restrito.Data.GetProperty("cliente").ValueKind);

        var func = await api.FuncionarioCom(geral, "caieiras", Permissao.So("agendamentos"));
        Assert.Equal(403, (await api.Get($"/api/admin/agenda/{ids[0]}?token={func}")).Codigo);
        var abaAlheia = await api.Get($"/api/admin/agenda?token={func}&unidade=franco");
        Assert.Equal(0, abaAlheia.Data.GetProperty("totalUnidade").GetInt32());

        var semAgenda = await api.AdminCom(geral, Permissao.So("produtos"));
        Assert.Equal(403, (await api.Get($"/api/admin/agenda?token={semAgenda}&unidade=franco")).Codigo);
    }

    [Fact]
    public async Task Horario_lotado_pela_capacidade_e_catalogo_de_servicos_para_quem_agenda()
    {
        var (_, _, datas) = await Agendados(1);
        var api = app.Api();
        var geral = await api.LoginAdmin();

        var lotados = await api.Get($"/api/admin/agenda/lotados?token={geral}&unidade=franco&data={datas[0]}");
        Assert.True(lotados.Codigo == 200, lotados.ToString());
        Assert.Contains(lotados.Data.GetProperty("lotados").EnumerateArray(), h => h.GetString() == "10:00");   // capacidade 1
        var outroDia = await api.Get($"/api/admin/agenda/lotados?token={geral}&unidade=caieiras&data={datas[0]}");
        Assert.Equal(0, outroDia.Data.GetProperty("lotados").GetArrayLength());

        var soAgenda = await api.AdminCom(geral, Permissao.So("agendamentos"));
        var servicos = await api.Get($"/api/admin/agenda/servicos?token={soAgenda}");
        Assert.True(servicos.Codigo == 200, servicos.ToString());
        Assert.True(servicos.Data.GetArrayLength() > 0);
        Assert.True(servicos.Data[0].TryGetProperty("porte", out _));
    }
}
