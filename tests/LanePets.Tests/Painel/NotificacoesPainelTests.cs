using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// 29/09: sino do painel (GET /api/admin/notificacoes). Mostra o que espera a equipe agir; some quando resolvido;
/// cada item so para quem pode ver o modulo. O banco e dividido com os outros testes: contagens por diferenca.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class NotificacoesPainelTests(LanePetsApp app)
{
    private static int Quantos(Resposta r, string chave)
        => r.Data.GetProperty("itens").EnumerateArray()
            .Where(i => i.GetProperty("chave").GetString() == chave)
            .Select(i => i.GetProperty("quantidade").GetInt32()).FirstOrDefault();

    [Fact]
    public async Task Agendamento_solicitado_entra_no_sino_e_sai_quando_a_equipe_confirma()
    {
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        var antes = Quantos(await painel.Get($"/api/admin/notificacoes?token={token}"), "agendamentos");

        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet("Tutor Sino", "Luna");
        var (servicoId, _) = await cliente.Servico();
        var agendamento = (await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), "11:00")).Data;

        var depois = await painel.Get($"/api/admin/notificacoes?token={token}");
        Assert.True(depois.Codigo == 200, depois.ToString());
        Assert.Equal(antes + 1, Quantos(depois, "agendamentos"));
        Assert.True(depois.Data.GetProperty("total").GetInt32() >= antes + 1);

        var sync = await painel.Sync(token, "agendamentos", atualizados: [Cenarios.ItemPainel(agendamento, "Confirmado")]);
        Assert.True(sync.Codigo == 200, sync.ToString());
        Assert.Equal(antes, Quantos(await painel.Get($"/api/admin/notificacoes?token={token}"), "agendamentos"));
    }

    [Fact]
    public async Task Sino_respeita_as_permissoes_e_exige_sessao()
    {
        var painel = app.Api();
        var geral = await painel.LoginAdmin();
        var soProdutos = await painel.AdminCom(geral, Permissao.So("produtos"));

        var r = await painel.Get($"/api/admin/notificacoes?token={soProdutos}");
        Assert.Equal(200, r.Codigo);
        Assert.All(r.Data.GetProperty("itens").EnumerateArray(), i => Assert.Equal("produtos", i.GetProperty("modulo").GetString()));

        Assert.Equal(401, (await app.Api().Get("/api/admin/notificacoes?token=invalido")).Codigo);
    }
}
