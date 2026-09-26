using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// Cadastro de unidades (POST/PUT /api/admin/unidades, /{id}/ativa).
/// Regras: CONTEXTO §6.26 (unidades vem do banco; so desativa, nunca a ultima ativa;
/// capacidade = atendimentos por horario), §6.4 (403 sem permissao), §6.25 (evento).
/// O teste da ultima unidade DEVOLVE o estado no finally — as outras classes usam Franco.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class UnidadesTests(LanePetsApp app)
{
    private static string NomeNovo() => "Unidade Teste " + Guid.NewGuid().ToString("N")[..6];

    [Fact]
    public async Task Criar_editar_e_desativar_unidade_com_validacao_e_evento()
    {
        var api = app.Api();
        var token = await api.LoginAdmin();
        var nome = NomeNovo();

        var criada = await api.Post("/api/admin/unidades", new { token, nome, endereco = "Rua do Teste, 10", capacidade = 3 });
        Assert.True(criada.Codigo == 200, criada.ToString());
        var id = criada.Texto("id");

        var repetida = await api.Post("/api/admin/unidades", new { token, nome = nome.ToUpperInvariant(), capacidade = 1 });
        Assert.Equal(400, repetida.Codigo);
        Assert.Contains("Já existe uma unidade", repetida.Erro);

        var semCapacidade = await api.Put($"/api/admin/unidades/{id}", new { token, nome, capacidade = 0 });
        Assert.Equal(400, semCapacidade.Codigo);
        Assert.Contains("capacidade", semCapacidade.Erro);

        var editada = await api.Put($"/api/admin/unidades/{id}", new { token, nome, endereco = "Rua do Teste, 10", capacidade = 5 });
        Assert.True(editada.Codigo == 200, editada.ToString());
        Assert.Equal(5, await app.NoBanco(db => Task.FromResult(db.Unidades.Single(u => u.Id == id).Capacidade)));

        var desativada = await api.Post($"/api/admin/unidades/{id}/ativa", new { token, ativa = false });
        Assert.True(desativada.Codigo == 200, desativada.ToString());
        var publicas = await api.Get("/api/public/unidades");
        Assert.DoesNotContain(publicas.Data.EnumerateArray(), u => u.GetProperty("id").GetString() == id);

        var acoes = await app.NoBanco(db => Task.FromResult(db.EventosLog.Where(e => e.AlvoId == id).Select(e => e.Acao).ToList()));
        Assert.Contains("Unidade criada", acoes);
        Assert.Contains("Unidade alterada", acoes);
        Assert.Contains("Unidade desativada", acoes);
    }

    [Fact]
    public async Task Sem_permissao_de_criar_recebe_403_e_nada_e_gravado()
    {
        var api = app.Api();
        var geral = await api.LoginAdmin();
        var soVe = await api.AdminCom(geral, Permissao.So("unidades", visualizar: true));
        var nome = NomeNovo();

        var r = await api.Post("/api/admin/unidades", new { token = soVe, nome, capacidade = 1 });

        Assert.Equal(403, r.Codigo);
        Assert.False(await app.NoBanco(db => Task.FromResult(db.Unidades.Any(u => u.Nome == nome))));
    }

    [Fact]
    public async Task Nao_desativa_a_ultima_unidade_ativa()
    {
        var api = app.Api();
        var token = await api.LoginAdmin();
        var ativas = await app.NoBanco(db => Task.FromResult(db.Unidades.Where(u => u.Ativa).OrderBy(u => u.Id).Select(u => u.Id).ToList()));
        Assert.NotEmpty(ativas);
        var ultima = ativas[0];
        var desativadasAqui = new List<string>();
        try
        {
            foreach (var id in ativas.Skip(1))
            {
                var r = await api.Post($"/api/admin/unidades/{id}/ativa", new { token, ativa = false });
                Assert.True(r.Codigo == 200, r.ToString());
                desativadasAqui.Add(id);
            }

            var recusa = await api.Post($"/api/admin/unidades/{ultima}/ativa", new { token, ativa = false });

            Assert.Equal(400, recusa.Codigo);
            Assert.Contains("única unidade ativa", recusa.Erro);
            Assert.True(await app.NoBanco(db => Task.FromResult(db.Unidades.Single(u => u.Id == ultima).Ativa)));
        }
        finally
        {
            foreach (var id in desativadasAqui)
                await api.Post($"/api/admin/unidades/{id}/ativa", new { token, ativa = true });
        }
    }
}
