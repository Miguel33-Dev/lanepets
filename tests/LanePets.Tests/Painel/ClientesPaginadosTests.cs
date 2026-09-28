using System.Text.Json;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// 27/09: tela Clientes sem o /api/admin/estado. Lista de clientes e de pets paginadas e
/// filtradas no servidor, detalhe proprio; gravacao continua pelo sync. Regras: §6.19
/// (permissao por modulo, funcionario so a unidade dele), §6.11 (resumo da base real).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class ClientesPaginadosTests(LanePetsApp app)
{
    private static List<string> Ids(Resposta r)
        => r.Data.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();

    private static int Total(Resposta r) => r.Data.GetProperty("total").GetInt32();

    [Fact]
    public async Task Clientes_vem_em_paginas_com_filtros_no_servidor_e_resumo_da_base_inteira()
    {
        var marca = Guid.NewGuid().ToString("N")[..8];
        var api = app.Api();
        var token = await api.LoginAdmin();
        var criar = await api.Sync(token, "clientes", criados:
        [
            new { id = Api.NovoId("CLI"), nome = $"Ana {marca}", telefone = "(11) 3333-1001" },
            new { id = Api.NovoId("CLI"), nome = $"Bia {marca}", telefone = "(11) 3333-1002" },
            new { id = Api.NovoId("CLI"), nome = $"Caio {marca}", telefone = "(11) 3333-1003", status = "inativo" }
        ]);
        Assert.True(criar.Codigo == 200, criar.ToString());
        var url = $"/api/admin/clientes?token={token}&busca={marca}";

        var p1 = await api.Get(url + "&limite=2&offset=0");
        var p2 = await api.Get(url + "&limite=2&offset=2");

        Assert.True(p1.Codigo == 200, p1.ToString());
        Assert.Equal(3, Total(p1));
        Assert.True(p1.Data.GetProperty("temMais").GetBoolean());
        Assert.False(p2.Data.GetProperty("temMais").GetBoolean());
        var nomes = p1.Data.GetProperty("itens").EnumerateArray().Concat(p2.Data.GetProperty("itens").EnumerateArray())
            .Select(i => i.GetProperty("nome").GetString()).ToList();
        Assert.Equal(new[] { $"Ana {marca}", $"Bia {marca}", $"Caio {marca}" }, nomes);   // por nome, sem repetir
        Assert.Equal(12, (await api.Get(url)).Data.GetProperty("limite").GetInt32());       // pagina padrao da tela

        var resumo = p1.Data.GetProperty("resumo");
        Assert.True(resumo.GetProperty("total").GetInt32() >= 3);
        Assert.Equal(resumo.GetProperty("total").GetInt32(), resumo.GetProperty("ativos").GetInt32() + resumo.GetProperty("inativos").GetInt32());

        Assert.Equal(1, Total(await api.Get(url + "&status=inativo")));
        Assert.Equal(2, Total(await api.Get(url + "&status=ativo")));
        Assert.Equal(3, Total(await api.Get(url + "&origem=cadastro_painel")));
        Assert.Equal(0, Total(await api.Get(url + "&origem=portal_cliente")));
        Assert.Equal(1, Total(await api.Get(url + "%20" + Uri.EscapeDataString("3333-1002"))));   // telefone pelos digitos
    }

    [Fact]
    public async Task Detalhe_traz_pets_e_agendamentos_so_para_quem_pode_ver()
    {
        var site = app.Api();
        var (conta, petId) = await site.ClienteComPet(("Cliente Detalhe " + Guid.NewGuid().ToString("N"))[..24], "Paçoca");
        var (servicoId, _) = await site.Servico();
        Assert.Equal(200, (await site.Agendar(petId, servicoId, Cenarios.NovaData(), "10:00")).Codigo);
        var clienteId = await app.NoBanco(db => Task.FromResult(db.UsuariosClientes.Single(u => u.Email == conta.Email).ClienteId));

        var api = app.Api();
        var geral = await api.LoginAdmin();
        var d = await api.Get($"/api/admin/clientes/{clienteId}?token={geral}");

        Assert.True(d.Codigo == 200, d.ToString());
        Assert.Equal(conta.Email, d.Texto("cliente", "email"));
        Assert.Equal(1, d.Data.GetProperty("cliente").GetProperty("qtdPets").GetInt32());
        Assert.Equal("Paçoca", d.Data.GetProperty("pets")[0].GetProperty("pet").GetString());
        Assert.Equal(1, d.Data.GetProperty("agendamentos").GetProperty("total").GetInt32());

        // So o modulo Clientes: sem pets nem agendamentos (e a tela esconde as secoes).
        var soClientes = await api.AdminCom(geral, Permissao.So("clientes"));
        var restrito = await api.Get($"/api/admin/clientes/{clienteId}?token={soClientes}");
        Assert.True(restrito.Codigo == 200, restrito.ToString());
        Assert.False(restrito.Data.GetProperty("podeVerPets").GetBoolean());
        Assert.Equal(0, restrito.Data.GetProperty("pets").GetArrayLength());
        Assert.Equal(0, restrito.Data.GetProperty("agendamentos").GetProperty("total").GetInt32());

        // Sem o modulo Clientes: 403, sem dados.
        var semClientes = await api.AdminCom(geral, Permissao.So("produtos"));
        Assert.Equal(403, (await api.Get($"/api/admin/clientes/{clienteId}?token={semClientes}")).Codigo);
        Assert.Equal(400, (await api.Get($"/api/admin/clientes/NAO-EXISTE?token={geral}")).Codigo);
    }

    [Fact]
    public async Task Pets_paginados_com_busca_tipo_por_grupo_e_unidade_do_funcionario()
    {
        var marca = Guid.NewGuid().ToString("N")[..8];
        var site = app.Api();
        await site.CadastrarCliente(nome: $"Dono {marca}", pet: $"Totó {marca}");   // tipo "Cachorro" no site
        var api = app.Api();
        var geral = await api.LoginAdmin();
        var url = $"/api/admin/pets?token={geral}&busca={marca}";

        var r = await api.Get(url);
        Assert.True(r.Codigo == 200, r.ToString());
        Assert.Equal(1, Total(r));
        Assert.True(r.Data.GetProperty("totalGeral").GetInt32() >= 1);
        var pet = r.Data.GetProperty("itens")[0];
        Assert.Equal($"Dono {marca}", pet.GetProperty("dono").GetString());
        Assert.Equal(JsonValueKind.Object, pet.GetProperty("pacote").ValueKind);                // formato do sync
        Assert.Equal(1, Total(await api.Get(url + "&tipo=" + Uri.EscapeDataString("Cão"))));   // "Cachorro" ≈ "Cão"
        Assert.Equal(0, Total(await api.Get(url + "&tipo=Gato")));
        Assert.Equal(50, (await api.Get(url)).Data.GetProperty("limite").GetInt32());

        // Pet sem unidade e sem agendamento nao aparece para funcionario (falha fechada).
        var func = await api.FuncionarioCom(geral, "caieiras", Permissao.So("pets"));
        Assert.Equal(0, Total(await api.Get($"/api/admin/pets?token={func}&busca={marca}")));
        // Sem o modulo Pets: 403.
        var semPets = await api.AdminCom(geral, Permissao.So("clientes"));
        Assert.Equal(403, (await api.Get($"/api/admin/pets?token={semPets}")).Codigo);
    }

    [Fact]
    public async Task Editar_pela_lista_e_gravar_pelo_sync_mantem_o_restante_do_registro()
    {
        var marca = Guid.NewGuid().ToString("N")[..8];
        var site = app.Api();
        await site.CadastrarCliente(nome: $"Dona {marca}", pet: $"Mel {marca}");
        var api = app.Api();
        var geral = await api.LoginAdmin();
        var pet = (await api.Get($"/api/admin/pets?token={geral}&busca={marca}")).Data.GetProperty("itens")[0];

        // A tela manda o objeto da lista inteiro, com so a raca trocada.
        var editado = JsonSerializer.Deserialize<Dictionary<string, object?>>(pet.GetRawText())!;
        editado["raca"] = "Poodle";
        var r = await api.Sync(geral, "pets", atualizados: [editado]);
        Assert.True(r.Codigo == 200, r.ToString());

        var id = pet.GetProperty("id").GetString();
        var gravado = await app.NoBanco(db => Task.FromResult(db.Pets.Single(p => p.Id == id)));
        Assert.Equal("Poodle", gravado.Raca);
        Assert.Equal($"Mel {marca}", gravado.PetNome);
        Assert.Equal(pet.GetProperty("clienteId").GetString(), gravado.ClienteId);
    }
}
