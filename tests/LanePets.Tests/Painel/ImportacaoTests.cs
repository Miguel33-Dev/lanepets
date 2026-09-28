using System.Text.Json;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// Importacao do navegador (migrar-dados.html -> POST /api/admin/importar, ImportacaoService).
/// Regras: so acrescenta (repetido pela identidade natural e ignorado, nada e sobrescrito);
/// simular nao grava; exige Configuracoes:criar; agendamento importado ganha pagamento;
/// vira evento no Log. Cada teste usa nomes unicos para nao cruzar com os outros.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class ImportacaoTests(LanePetsApp app)
{
    /// <summary>Um lote como o migrar-dados.html manda: um de cada + uma linha invalida por colecao.</summary>
    private static object Lote(string token, bool simular, string marca, string dia) => new
    {
        token,
        simular,
        servicos = new object[] { new { nome = $"Servico {marca}", preco = "45,90", porte = "Pequeno" }, new { preco = 10 } },
        produtos = new object[] { new { codigo = $"COD-{marca}", nome = $"Produto {marca}", valorVenda = "19.90", estoque = "3" }, new { categoria = "sem nome" } },
        pets = new object[] { new { dono = $"Dono {marca}", pet = $"Pet {marca}", telefone = "11 90000-0000", tipo = "Cão" }, new { dono = "Sem pet" } },
        agendamentos = new object[]
        {
            new { dataHora = $"{dia}T09:30", pet = $"Pet {marca}", dono = $"Dono {marca}", total = "80", status = "Concluído", unidade = "franco", formaPagamento = "Pix" },
            new { pet = "Sem data" }
        },
        entradasESaidas = new object[] { new { data = dia, descricao = $"Lançamento {marca}", tipo = "Entrada", valor = "150,00" }, new { descricao = "Sem data" } }
    };

    private static void Confere(JsonElement relatorio, string colecao, int novos, int jaExistiam, int ignorados)
    {
        var c = relatorio.GetProperty(colecao);
        Assert.True(c.GetProperty("novos").GetInt32() == novos, $"{colecao}: novos {c}");
        Assert.True(c.GetProperty("jaExistiam").GetInt32() == jaExistiam, $"{colecao}: jaExistiam {c}");
        Assert.True(c.GetProperty("ignorados").GetInt32() == ignorados, $"{colecao}: ignorados {c}");
    }

    private static readonly string[] Colecoes = ["servicos", "produtos", "pets", "agendamentos", "entradasESaidas"];

    [Fact]
    public async Task Simular_conta_novos_e_ignorados_sem_gravar_nada()
    {
        var marca = Guid.NewGuid().ToString("N")[..8];
        var painel = app.Api();
        var token = await painel.LoginAdmin();

        var r = await painel.Post("/api/admin/importar", Lote(token, simular: true, marca, Cenarios.NovaData()));

        Assert.True(r.Codigo == 200, r.ToString());
        Assert.True(r.Data.GetProperty("simulacao").GetBoolean());
        foreach (var c in Colecoes) Confere(r.Data.GetProperty("relatorio"), c, novos: 1, jaExistiam: 0, ignorados: 1);
        var gravados = await app.NoBanco(db => Task.FromResult(
            db.Pets.Count(p => p.PetNome == $"Pet {marca}") + db.Servicos.Count(s => s.Nome == $"Servico {marca}")
            + db.Agendamentos.Count(a => a.Pet == $"Pet {marca}") + db.EntradasESaidas.Count(e => e.Descricao == $"Lançamento {marca}")));
        Assert.Equal(0, gravados);
    }

    [Fact]
    public async Task Importar_grava_uma_vez_da_pagamento_ao_agendamento_e_repetir_nao_duplica()
    {
        var marca = Guid.NewGuid().ToString("N")[..8];
        var dia = Cenarios.NovaData();
        var painel = app.Api();
        var token = await painel.LoginAdmin();

        var r = await painel.Post("/api/admin/importar", Lote(token, simular: false, marca, dia));
        Assert.True(r.Codigo == 200, r.ToString());
        foreach (var c in Colecoes) Confere(r.Data.GetProperty("relatorio"), c, novos: 1, jaExistiam: 0, ignorados: 1);

        var (pet, cliente, agendamento, servico, lancamento) = await app.NoBanco(db => Task.FromResult((
            db.Pets.Single(p => p.PetNome == $"Pet {marca}"),
            db.Clientes.Single(c => c.Nome == $"Dono {marca}"),
            db.Agendamentos.Single(a => a.Pet == $"Pet {marca}"),
            db.Servicos.Single(s => s.Nome == $"Servico {marca}"),
            db.EntradasESaidas.Single(e => e.Descricao == $"Lançamento {marca}"))));
        Assert.Equal(cliente.Id, pet.ClienteId);                        // dono vira cliente
        Assert.Equal("importacao_painel", cliente.Origem);
        Assert.Equal(45.90m, servico.Preco);                            // "45,90" lido com tolerancia
        Assert.Equal(150m, lancamento.Valor);
        Assert.Equal("Concluído", agendamento.Status);
        var pagamento = await app.NoBanco(db => Task.FromResult(db.Pagamentos.SingleOrDefault(p => p.Origem == "agendamento" && p.OrigemId == agendamento.Id)));
        Assert.NotNull(pagamento);                                      // item 8: importado ganha pagamento
        Assert.Equal(80m, pagamento!.Valor);

        var eventos = await app.NoBanco(db => Task.FromResult(
            db.EventosLog.Where(e => e.Acao == "Importação de dados do navegador").ToList()));
        Assert.Contains(eventos, e => e.Detalhes.Contains("pets: 1 novo(s)"));

        // Segunda vez: tudo reconhecido como repetido, nada duplicado.
        var deNovo = await painel.Post("/api/admin/importar", Lote(token, simular: false, marca, dia));
        Assert.True(deNovo.Codigo == 200, deNovo.ToString());
        foreach (var c in Colecoes) Confere(deNovo.Data.GetProperty("relatorio"), c, novos: 0, jaExistiam: 1, ignorados: 1);
        Assert.Equal(1, await app.NoBanco(db => Task.FromResult(db.Pets.Count(p => p.PetNome == $"Pet {marca}"))));
        Assert.Equal(1, await app.NoBanco(db => Task.FromResult(db.Agendamentos.Count(a => a.Pet == $"Pet {marca}"))));
    }

    [Fact]
    public async Task Sem_permissao_de_configuracoes_e_403_e_nada_e_gravado()
    {
        var marca = Guid.NewGuid().ToString("N")[..8];
        var painel = app.Api();
        var geral = await painel.LoginAdmin();
        var soVe = await painel.AdminCom(geral, Permissao.So("configuracoes"));   // visualizar nao basta: exige criar

        var r = await painel.Post("/api/admin/importar", Lote(soVe, simular: false, marca, Cenarios.NovaData()));

        Assert.Equal(403, r.Codigo);
        Assert.Equal(0, await app.NoBanco(db => Task.FromResult(db.Pets.Count(p => p.PetNome == $"Pet {marca}"))));
    }
}
