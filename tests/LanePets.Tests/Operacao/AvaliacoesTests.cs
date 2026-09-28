using LanePets.Tests.Infra;

namespace LanePets.Tests.Operacao;

/// <summary>
/// Avaliacoes (26/09): so pela conta do cliente. O POST anonimo do site
/// (/api/public/depoimentos com nome + telefone) foi removido porque nome + telefone nao
/// identificam ninguem (CONTEXTO §6.5) — dava para avaliar em nome de outro cliente.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class AvaliacoesTests(LanePetsApp app)
{
    private Task<int> AvaliacoesCom(string comentario)
        => app.NoBanco(db => Task.FromResult(db.Depoimentos.Count(d => d.Comentario == comentario)));

    [Fact]
    public async Task Post_anonimo_do_site_nao_existe_mais_mesmo_com_nome_e_telefone_de_um_cliente()
    {
        var dono = app.Api();
        await dono.ClienteComPet(nome: "Cliente Alvo", pet: "Nina");
        var comentario = "Tentativa anonima " + Guid.NewGuid().ToString("N");

        var intruso = app.Api();   // sem sessao
        var r = await intruso.Post("/api/public/depoimentos",
            new { nome = "Cliente Alvo", telefone = "(11) 98765-4321", pet = "Nina", avaliacao = 1, comentario });

        Assert.Equal(405, r.Codigo);
        Assert.False(r.Ok);
        Assert.Equal(0, await AvaliacoesCom(comentario));
        // A leitura publica das avaliacoes aprovadas continua.
        Assert.Equal(200, (await intruso.Get("/api/public/depoimentos")).Codigo);
    }

    [Fact]
    public async Task Avaliar_sem_sessao_de_cliente_e_401()
    {
        var comentario = "Sem login " + Guid.NewGuid().ToString("N");
        var r = await app.Api().Post("/api/cliente/avaliacoes", new { avaliacao = 5, comentario });

        Assert.Equal(401, r.Codigo);
        Assert.Equal(0, await AvaliacoesCom(comentario));
    }

    [Fact]
    public async Task Com_homonimos_a_avaliacao_fica_com_quem_esta_logado()
    {
        // Mesmo nome e mesmo telefone: exatamente o caso que o formulario antigo confundia.
        var primeiro = app.Api();
        await primeiro.ClienteComPet(nome: "Cliente Homonimo", pet: "Pingo");
        var segundo = app.Api();
        var (_, petDoSegundo) = await segundo.ClienteComPet(nome: "Cliente Homonimo", pet: "Pingo");
        var clienteDoSegundo = (await segundo.Get("/api/cliente/conta")).Texto("id");
        var comentario = "Avaliacao do segundo " + Guid.NewGuid().ToString("N");

        var r = await segundo.Post("/api/cliente/avaliacoes", new { petId = petDoSegundo, avaliacao = 5, comentario });

        Assert.True(r.Codigo == 200, r.ToString());
        Assert.Equal("Pendente", r.Texto("status"));   // continua passando pela moderacao
        var gravada = await app.NoBanco(db => Task.FromResult(db.Depoimentos.Single(d => d.Comentario == comentario)));
        Assert.Equal(clienteDoSegundo, gravada.ClienteId);
        Assert.Equal("Pingo", gravada.NomePet);
    }

    [Fact]
    public async Task Pet_de_outra_conta_e_recusado()
    {
        var dono = app.Api();
        var (_, petAlheio) = await dono.ClienteComPet(nome: "Dono do Pet", pet: "Alheio");
        var outro = app.Api();
        await outro.ClienteComPet(nome: "Outro Cliente", pet: "Meu");
        var comentario = "Pet de outra conta " + Guid.NewGuid().ToString("N");

        var r = await outro.Post("/api/cliente/avaliacoes", new { petId = petAlheio, avaliacao = 4, comentario });

        Assert.Equal(400, r.Codigo);
        Assert.Contains("Pet nao localizado", r.Erro);
        Assert.Equal(0, await AvaliacoesCom(comentario));
    }
}
