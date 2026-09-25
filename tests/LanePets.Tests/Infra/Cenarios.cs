using System.Text.Json;

namespace LanePets.Tests.Infra;

/// <summary>
/// Montagem dos cenarios da fase 12.3 (agendamentos, pedidos e pagamentos) pelos
/// mesmos caminhos que as telas usam — nada e inserido direto no banco.
/// </summary>
public static class Cenarios
{
    public const string Franco = "Franco da Rocha";   // o site grava a unidade pelo NOME
    public const string FrancoId = "franco";          // o painel grava pelo ID
    public const string Caieiras = "Caieiras";

    private static int _dia;

    /// <summary>
    /// Uma data futura diferente a cada chamada. As unidades semeadas atendem 1 pet por
    /// horario (capacidade 1): com data exclusiva, um teste nunca ocupa o horario do outro.
    /// </summary>
    public static string NovaData() => DateTime.Today.AddDays(400 + Interlocked.Increment(ref _dia)).ToString("yyyy-MM-dd");

    /// <summary>Cria a conta (ficando com a sessao) e devolve o id do pet do cadastro.</summary>
    public static async Task<(ClienteCriado Cliente, string PetId)> ClienteComPet(this Api api, string nome = "Cliente Agenda", string pet = "Pipoca")
    {
        var cliente = await api.CadastrarCliente(nome: nome, pet: pet);
        var conta = await api.Get("/api/cliente/conta");
        return (cliente, conta.Data.GetProperty("pets")[0].GetProperty("id").GetString()!);
    }

    /// <summary>Um servico do catalogo com preco (id e preco).</summary>
    public static async Task<(string Id, decimal Preco)> Servico(this Api api)
    {
        var catalogo = await api.Get("/api/cliente/catalogo");
        var servico = catalogo.Data.GetProperty("servicos").EnumerateArray().First(s => s.GetProperty("preco").GetDecimal() > 0);
        return (servico.GetProperty("id").GetString()!, servico.GetProperty("preco").GetDecimal());
    }

    /// <summary>POST /api/cliente/agendamentos como a tela "Agendar" manda.</summary>
    public static Task<Resposta> Agendar(this Api api, string petId, string servicoId, string data, string horario, string unidade = Franco, string? transporte = null)
        => api.Post("/api/cliente/agendamentos", new
        {
            petId, servicoId, unidade, data, horario,
            transporte = transporte ?? "Cliente leva",
            formaPagamento = "Pix",
            observacao = "Criado pelo teste"
        });

    /// <summary>
    /// Item que a tela de Agendamentos do painel manda no sync para ATUALIZAR um
    /// agendamento: o registro inteiro, so com o status trocado.
    /// </summary>
    public static object ItemPainel(JsonElement agendamento, string status) => new
    {
        id = agendamento.GetProperty("id").GetString(),
        pet = agendamento.GetProperty("pet").GetString(),
        petId = agendamento.GetProperty("petId").GetString(),
        dono = agendamento.GetProperty("dono").GetString(),
        telefone = agendamento.GetProperty("telefone").GetString(),
        dataHora = agendamento.GetProperty("dataHora").GetString(),
        total = agendamento.GetProperty("total").GetDecimal(),
        valorTransporte = 0,
        status,
        pagamentoStatus = agendamento.GetProperty("pagamentoStatus").GetString(),
        formaPagamento = agendamento.GetProperty("formaPagamento").GetString(),
        obs = agendamento.GetProperty("obs").GetString(),
        unidade = agendamento.GetProperty("unidade").GetString()
    };

    /// <summary>Produto criado pelo painel (Geral) com controle de estoque. Devolve o id.</summary>
    public static async Task<string> ProdutoNaLoja(this Api api, string token, int estoque = 10, double valorVenda = 25, bool visivelLoja = true)
    {
        var id = Api.NovoId("PRD");
        var r = await api.Sync(token, "produtos", criados:
        [
            new { id, codigo = id, nome = $"Produto {id}", categoria = "Acessórios", valorVenda, valorCompra = 10, estoque, estoqueMinimo = 0, controlaEstoque = true, visivelLoja }
        ]);
        Assert.True(r.Codigo == 200, $"Criar produto falhou: {r}");
        return id;
    }

    /// <summary>POST /api/cliente/pedidos como a loja da area do cliente manda.</summary>
    public static Task<Resposta> Pedir(this Api api, string produtoId, int quantidade, string? unidade = Franco)
        => api.Post("/api/cliente/pedidos", new { produtoId, quantidade, formaPagamento = "Pix", unidade });
}
