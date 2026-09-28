using LanePets.Models;
using LanePets.Services;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Operacao;

/// <summary>
/// Unidade gravada pelo id (26/09). O site mandava o NOME ("Franco da Rocha") e o painel o id
/// ("franco"); agora o banco guarda sempre o id, a area do cliente continua recebendo o nome, e
/// a subida converte o que ficou gravado pelo nome (UnidadesRegras.UnificarGravadasPeloNomeAsync).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class UnidadePorIdTests(LanePetsApp app)
{
    [Fact]
    public async Task Cliente_escolhe_pelo_nome_o_banco_grava_o_id_e_a_tela_recebe_o_nome()
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet("Cliente Unidade Id", "Bidu");
        var (servicoId, _) = await cliente.Servico();

        var r = await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), "10:00", Cenarios.Franco);

        Assert.True(r.Codigo == 200, r.ToString());
        Assert.Equal(Cenarios.Franco, r.Texto("unidade"));                 // resposta para a tela: nome
        var id = r.Texto("id");
        Assert.Equal(Cenarios.FrancoId, await app.NoBanco(db => Task.FromResult(db.Agendamentos.Single(a => a.Id == id).Unidade)));

        var conta = await cliente.Get("/api/cliente/conta");
        var naLista = conta.Data.GetProperty("agendamentos").EnumerateArray().Single(a => a.GetProperty("id").GetString() == id);
        Assert.Equal(Cenarios.Franco, naLista.GetProperty("unidade").GetString());
    }

    [Fact]
    public async Task Subida_troca_pelo_id_o_que_ficou_gravado_pelo_nome_e_preserva_o_desconhecido()
    {
        var marca = Guid.NewGuid().ToString("N")[..8];
        await app.NoBanco(async db =>
        {
            db.Agendamentos.Add(new Agendamento { Id = "AGD-NOME-" + marca, Pet = "Legado", Dono = "Legado", DataHora = "2035-01-02T10:00:00", Unidade = "Franco da Rocha", ServicosJson = "[]" });
            db.Pets.Add(new Pet { Id = "PET-NOME-" + marca, PetNome = "Legado " + marca, Unidade = "Caieiras" });
            db.EntradasESaidas.Add(new EntradaSaida { Id = "FIN-DESC-" + marca, Data = "2035-01-02", Descricao = "Legado", Tipo = "Entrada", Unidade = "Filial Antiga" });
            db.EntradasESaidas.Add(new EntradaSaida { Id = "FIN-VAZIA-" + marca, Data = "2035-01-02", Descricao = "Legado", Tipo = "Entrada", Unidade = "" });
            await db.SaveChangesAsync();
            return 0;
        });

        var trocados = await app.NoBanco(db => UnidadesRegras.UnificarGravadasPeloNomeAsync(db));
        var deNovo = await app.NoBanco(db => UnidadesRegras.UnificarGravadasPeloNomeAsync(db));

        Assert.True(trocados >= 2, $"Esperava ao menos 2 trocas, vieram {trocados}.");
        Assert.Equal(0, deNovo);                                           // idempotente
        var (agendamento, pet, desconhecida, vazia) = await app.NoBanco(db => Task.FromResult((
            db.Agendamentos.Single(a => a.Id == "AGD-NOME-" + marca).Unidade,
            db.Pets.Single(p => p.Id == "PET-NOME-" + marca).Unidade,
            db.EntradasESaidas.Single(e => e.Id == "FIN-DESC-" + marca).Unidade,
            db.EntradasESaidas.Single(e => e.Id == "FIN-VAZIA-" + marca).Unidade)));
        Assert.Equal("franco", agendamento);
        Assert.Equal("caieiras", pet);
        Assert.Equal("Filial Antiga", desconhecida);                      // nao reconhecida: fica como estava
        Assert.Equal("", vazia);
    }
}
