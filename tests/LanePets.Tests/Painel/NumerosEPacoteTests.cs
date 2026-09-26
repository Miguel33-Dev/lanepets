using System.Text.Json;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// (1) Faixa de números da home — GET /api/public/numeros (CONTEXTO §6.11: nada inventado).
/// (2) Pacote do pet sobrevivendo ao sync do painel — bug de 25/09: o estado devolvia
///     pacote vazio como [] e marcar "Cliente pacote" nunca gravava.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class NumerosEPacoteTests(LanePetsApp app)
{
    private static async Task<JsonElement> Numeros(Api api)
    {
        var r = await api.Get("/api/public/numeros");
        Assert.True(r.Codigo == 200, r.ToString());
        return r.Data;
    }

    [Fact]
    public async Task Numeros_da_home_contam_so_atendimento_concluido_e_avaliacao_aprovada()
    {
        var site = app.Api();
        var antes = await Numeros(site);

        // Agendamento Solicitado nao conta como pet atendido.
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet(nome: "Cliente Numeros", pet: "Bolinha");
        var (servicoId, _) = await cliente.Servico();
        var agendamento = (await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), "10:00")).Data;
        Assert.Equal(antes.GetProperty("petsAtendidos").GetInt32(), (await Numeros(site)).GetProperty("petsAtendidos").GetInt32());

        // Concluido pelo painel: +1 pet.
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        var sync = await painel.Sync(token, "agendamentos", atualizados: [Cenarios.ItemPainel(agendamento, "Concluído")]);
        Assert.True(sync.Codigo == 200, sync.ToString());
        var depois = await Numeros(site);
        Assert.Equal(antes.GetProperty("petsAtendidos").GetInt32() + 1, depois.GetProperty("petsAtendidos").GetInt32());

        // Avaliacao so entra depois de aprovada.
        var comentario = "Nota da home " + Guid.NewGuid().ToString("N");
        var enviada = await site.Post("/api/public/depoimentos", new { nome = "Cliente Numeros", telefone = "(11) 98765-4321", pet = "Bolinha", avaliacao = 4, comentario });
        Assert.True(enviada.Codigo == 200, enviada.ToString());
        Assert.Equal(antes.GetProperty("avaliacoes").GetInt32(), (await Numeros(site)).GetProperty("avaliacoes").GetInt32());

        var id = await app.NoBanco(db => Task.FromResult(db.Depoimentos.Single(d => d.Comentario == comentario).Id));
        var aprovada = await painel.Post($"/api/admin/depoimentos/{id}/status", new { token, status = "Aprovado" });
        Assert.True(aprovada.Codigo == 200, aprovada.ToString());

        var final = await Numeros(site);
        Assert.Equal(antes.GetProperty("avaliacoes").GetInt32() + 1, final.GetProperty("avaliacoes").GetInt32());
        var media = final.GetProperty("avaliacaoMedia");
        Assert.Equal(JsonValueKind.Number, media.ValueKind);
        Assert.InRange(media.GetDouble(), 1, 5);
    }

    private static async Task<JsonElement> PetNoEstado(Api api, string token, string id)
    {
        var estado = await api.Get($"/api/admin/estado?token={token}");
        Assert.True(estado.Codigo == 200, estado.ToString());
        return estado.Data.GetProperty("pets").EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task Pacote_vazio_vem_como_objeto_e_marcar_cliente_pacote_grava()
    {
        var api = app.Api();
        var token = await api.LoginAdmin();
        var id = Api.NovoId("PET");
        var pet = new { id, dono = "Dono Pacote", pet = "Fumaça", tipo = "Gato", raca = "SRD", telefone = "11911112222", endereco = "Rua A, 1", unidade = "franco" };
        var criado = await api.Sync(token, "pets", criados: [pet]);
        Assert.True(criado.Codigo == 200, criado.ToString());

        // Sem pacote: objeto vazio, nunca [] (num array a tela perdia o "ativo").
        var vazio = (await PetNoEstado(api, token, id)).GetProperty("pacote");
        Assert.Equal(JsonValueKind.Object, vazio.ValueKind);

        var comPacote = new { pet.id, pet.dono, pet.pet, pet.tipo, pet.raca, pet.telefone, pet.endereco, pet.unidade,
            pacote = new { ativo = true, tipo = "Mensal", total = 4, historico = Array.Empty<string>() } };
        var salvo = await api.Sync(token, "pets", atualizados: [comPacote]);
        Assert.True(salvo.Codigo == 200, salvo.ToString());

        var pacote = (await PetNoEstado(api, token, id)).GetProperty("pacote");
        Assert.True(pacote.GetProperty("ativo").GetBoolean());
        Assert.Equal("Mensal", pacote.GetProperty("tipo").GetString());
    }
}
