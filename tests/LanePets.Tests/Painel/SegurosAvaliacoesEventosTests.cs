using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// Log de eventos (CONTEXTO §6.25) nas acoes do painel que ainda nao registravam nada:
/// CRUD de planos de seguro, moderacao de avaliacoes e status de solicitacao de seguro.
/// Evento so depois de gravar; mudar para o mesmo status nao vira evento; recusa por
/// permissao (403) nao grava nada.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class SegurosAvaliacoesEventosTests(LanePetsApp app)
{
    private Task<List<LanePets.Models.EventoLog>> Eventos(string alvoId)
        => app.NoBanco(db => Task.FromResult(db.EventosLog.Where(e => e.AlvoId == alvoId).OrderBy(e => e.DataHora).ToList()));

    private static async Task<string> CriarPlano(Api api, string token, string nome, decimal valor = 29.9m)
    {
        var r = await api.Post("/api/admin/seguros", new { token, nome, descricao = "Plano do teste", valorMensal = valor });
        Assert.True(r.Codigo == 200, $"Criar plano falhou: {r}");
        return r.Texto("id");
    }

    [Fact]
    public async Task Criar_editar_desativar_e_excluir_plano_registram_evento()
    {
        var api = app.Api();
        var token = await api.LoginAdmin();
        var nome = "Plano Teste " + Guid.NewGuid().ToString("N")[..6];
        var id = await CriarPlano(api, token, nome);

        var editado = await api.Put($"/api/admin/seguros/{id}", new { token, nome, descricao = "Plano do teste", valorMensal = 39.9m });
        Assert.True(editado.Codigo == 200, $"Editar plano falhou: {editado}");
        var desativado = await api.Post($"/api/admin/seguros/{id}/ativo", new { token, ativo = false });
        Assert.True(desativado.Codigo == 200, $"Desativar plano falhou: {desativado}");
        // Desativar de novo nao muda nada: nao pode virar um segundo evento.
        await api.Post($"/api/admin/seguros/{id}/ativo", new { token, ativo = false });
        var excluido = await api.Enviar(HttpMethod.Delete, $"/api/admin/seguros/{id}?token={token}", null);
        Assert.True(excluido.Codigo == 200, $"Excluir plano falhou: {excluido}");

        var eventos = await Eventos(id);
        Assert.Equal(
            new[] { "Plano de seguro criado", "Plano de seguro alterado", "Plano de seguro desativado", "Plano de seguro excluído" },
            eventos.Select(e => e.Acao).ToArray());
        Assert.All(eventos, e => { Assert.Equal("seguro", e.Categoria); Assert.Equal("admin", e.Origem); Assert.Equal(Api.AdminEmail, e.Autor); });
        Assert.Contains("R$ 29,90 → R$ 39,90", eventos[1].Detalhes);
    }

    [Fact]
    public async Task Plano_recusado_por_permissao_nao_registra_evento()
    {
        var api = app.Api();
        var geral = await api.LoginAdmin();
        var id = await CriarPlano(api, geral, "Plano Teste 403 " + Guid.NewGuid().ToString("N")[..6]);
        var soVe = await api.AdminCom(geral, Permissao.So("seguros", visualizar: true));

        var r = await api.Post($"/api/admin/seguros/{id}/ativo", new { token = soVe, ativo = false });

        Assert.Equal(403, r.Codigo);
        Assert.Equal(new[] { "Plano de seguro criado" }, (await Eventos(id)).Select(e => e.Acao).ToArray());
    }

    [Fact]
    public async Task Avaliacao_enviada_e_moderada_registra_eventos()
    {
        var api = app.Api();
        await api.CadastrarCliente(nome: "Cliente Teste", pet: "Rex");
        var comentario = "Atendimento ótimo " + Guid.NewGuid().ToString("N");
        var enviada = await api.Post("/api/public/depoimentos", new { nome = "Cliente Teste", telefone = "(11) 98765-4321", pet = "Rex", avaliacao = 5, comentario });
        Assert.True(enviada.Codigo == 200, $"Enviar avaliação falhou: {enviada}");
        var depoimentoId = await app.NoBanco(db => Task.FromResult(db.Depoimentos.Single(d => d.Comentario == comentario).Id));

        var token = await api.LoginAdmin();
        var r = await api.Post($"/api/admin/depoimentos/{depoimentoId}/status", new { token, status = "Aprovado" });
        Assert.True(r.Codigo == 200, $"Moderar falhou: {r}");

        var eventos = await Eventos(depoimentoId);
        var aprovada = Assert.Single(eventos);
        Assert.Equal("Avaliação aprovada", aprovada.Acao);
        Assert.Equal("avaliacao", aprovada.Categoria);
        Assert.Contains("Pendente → Aprovado", aprovada.Detalhes);

        var envio = await app.NoBanco(db => Task.FromResult(
            db.EventosLog.Where(e => e.Acao == "Avaliação enviada pelo site" && e.Origem == "publico").ToList()));
        Assert.NotEmpty(envio);
        Assert.DoesNotContain(envio, e => e.Detalhes.Contains("98765"));
    }
}
