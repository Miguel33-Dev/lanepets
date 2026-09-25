using System.Text.Json;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// Clientes no painel: GET /api/admin/clientes e a gravacao por POST /api/admin/sync/clientes.
/// Regras: CONTEXTO §6.4 (403, nunca 200 com dados), §6.19 (permissao por modulo e acao
/// no backend), §6.25 (acao do painel vira evento com o autor).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class ClientesPainelTests(LanePetsApp app)
{
    private static JsonElement? Achar(Resposta lista, string id)
        => lista.Data.EnumerateArray().Cast<JsonElement?>().FirstOrDefault(c => c!.Value.GetProperty("id").GetString() == id);

    [Fact]
    public async Task Cliente_cadastrado_no_site_aparece_no_painel_com_a_conta_e_o_pet()
    {
        var site = app.Api();
        var criado = await site.CadastrarCliente(nome: "Carla Site", pet: "Nina");
        var clienteId = await app.NoBanco(db => Task.FromResult(db.UsuariosClientes.Single(u => u.Email == criado.Email).ClienteId));

        var painel = app.Api();
        var lista = await painel.Get($"/api/admin/clientes?token={await painel.LoginAdmin()}");

        Assert.Equal(200, lista.Codigo);
        var cliente = Achar(lista, clienteId) ?? throw new Xunit.Sdk.XunitException("Cliente do site não apareceu no painel.");
        Assert.Equal("Carla Site", cliente.GetProperty("nome").GetString());
        Assert.Equal(criado.Email, cliente.GetProperty("email").GetString());
        Assert.True(cliente.GetProperty("temConta").GetBoolean());
        Assert.Equal("portal_cliente", cliente.GetProperty("origem").GetString());
        Assert.Equal(1, cliente.GetProperty("qtdPets").GetInt32());
    }

    [Fact]
    public async Task Cliente_criado_pelo_painel_fica_sem_conta_e_vira_evento()
    {
        var api = app.Api();
        var token = await api.LoginAdmin();
        var id = Api.NovoId("CLI");

        var r = await api.Sync(token, "clientes", criados: [new { id, nome = "Pedro Balcão", telefone = "(11) 4444-5555", endereco = "Av. B, 2" }]);

        Assert.Equal(200, r.Codigo);
        Assert.Equal(1, r.Data.GetProperty("criados").GetInt32());
        var cliente = Achar(await api.Get($"/api/admin/clientes?token={token}"), id) ?? throw new Xunit.Sdk.XunitException("Cliente criado não apareceu.");
        Assert.False(cliente.GetProperty("temConta").GetBoolean());
        Assert.Equal("cadastro_painel", cliente.GetProperty("origem").GetString());

        var evento = await app.NoBanco(db => Task.FromResult(db.EventosLog.Single(e => e.Acao == "Cliente criado" && e.AlvoId == id)));
        Assert.Equal(Api.AdminEmail, evento.Autor);
    }

    [Fact]
    public async Task Editar_cliente_no_painel_leva_nome_e_telefone_para_os_pets()
    {
        var site = app.Api();
        var criado = await site.CadastrarCliente(nome: "Nome Antigo", pet: "Bidu");
        var clienteId = await app.NoBanco(db => Task.FromResult(db.UsuariosClientes.Single(u => u.Email == criado.Email).ClienteId));

        var painel = app.Api();
        var r = await painel.Sync(await painel.LoginAdmin(), "clientes",
            atualizados: [new { id = clienteId, nome = "Nome Novo", telefone = "(11) 91234-5678", endereco = "Rua Nova, 3" }]);

        Assert.Equal(200, r.Codigo);
        var pet = await app.NoBanco(db => Task.FromResult(db.Pets.Single(p => p.ClienteId == clienteId)));
        Assert.Equal("Nome Novo", pet.Dono);
        Assert.Equal("(11) 91234-5678", pet.Telefone);
    }

    [Fact]
    public async Task Sem_o_modulo_clientes_a_lista_responde_403_sem_dados()
    {
        var api = app.Api();
        var restrito = await api.AdminCom(await api.LoginAdmin(), Permissao.So("produtos"));

        var r = await api.Get($"/api/admin/clientes?token={restrito}");

        Assert.Equal(403, r.Codigo);
        Assert.Equal("ERR-4030", r.CodigoErro);
        Assert.False(r.Corpo.TryGetProperty("data", out _));
    }

    [Fact]
    public async Task So_visualizar_nao_deixa_criar_nem_excluir_cliente()
    {
        var api = app.Api();
        var geral = await api.LoginAdmin();
        var leitor = await api.AdminCom(geral, Permissao.So("clientes", visualizar: true));
        var id = Api.NovoId("CLI");

        var lista = await api.Get($"/api/admin/clientes?token={leitor}");
        var criar = await api.Sync(leitor, "clientes", criados: [new { id, nome = "Não Deveria Existir" }]);

        Assert.Equal(200, lista.Codigo);
        Assert.Equal(403, criar.Codigo);
        Assert.False(await app.NoBanco(db => Task.FromResult(db.Clientes.Any(c => c.Id == id))));

        // Existe de verdade, criado pelo Geral: o leitor tambem nao consegue apagar.
        await api.Sync(geral, "clientes", criados: [new { id, nome = "Cliente Protegido" }]);
        var excluir = await api.Sync(leitor, "clientes", removidos: [id]);

        Assert.Equal(403, excluir.Codigo);
        Assert.True(await app.NoBanco(db => Task.FromResult(db.Clientes.Any(c => c.Id == id))));
    }

    [Fact]
    public async Task Acesso_negado_vira_evento_de_seguranca()
    {
        var api = app.Api();
        var restrito = await api.AdminCom(await api.LoginAdmin());
        var antes = await app.NoBanco(db => Task.FromResult(db.EventosLog.Count(e => e.Acao == "Acesso negado")));

        await api.Get($"/api/admin/clientes?token={restrito}");

        // O tradutor de erros grava esse evento em segundo plano (dispara e esquece).
        var depois = antes;
        for (var tentativa = 0; tentativa < 20 && depois == antes; tentativa++)
        {
            await Task.Delay(100);
            depois = await app.NoBanco(db => Task.FromResult(db.EventosLog.Count(e => e.Acao == "Acesso negado")));
        }
        Assert.True(depois > antes, "403 deveria ter virado evento \"Acesso negado\".");
    }
}
