using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Itens 6/7 do roadmap (24/09): ciclo de vida do pedido da loja.
///   Pendente -> Confirmado -> Entregue      (+ Cancelado, que e final)
/// Cancelar devolve ao estoque o que o pedido baixou (decisao do Fabricio),
/// pela movimentacao "cancelamento" do livro de estoque.
/// Status de pedido NAO tem relacao com status de agendamento (item 4).
/// </summary>
public static class PedidosService
{
    public const string Pendente = "Pendente";
    public const string Confirmado = "Confirmado";
    public const string Entregue = "Entregue";
    public const string Cancelado = "Cancelado";
    public static readonly string[] Todos = [Pendente, Confirmado, Entregue, Cancelado];

    public static string Normalizar(string? valor) => Normalizador.Texto(valor) switch
    {
        "pendente" => Pendente,
        "confirmado" => Confirmado,
        "entregue" or "retirado" => Entregue,
        "cancelado" => Cancelado,
        _ => ""
    };

    public static string Exibir(string? valor) => Normalizar(valor) is { Length: > 0 } s ? s : Pendente;

    /// <summary>
    /// Muda o status (sem SaveChanges — quem chama salva). Devolve quantas
    /// unidades voltaram ao estoque (0 quando nao cancelou ou nao havia baixa).
    /// </summary>
    public static async Task<int> AlterarStatusAsync(LanePetsDbContext db, Pedido pedido, string? novoStatus,
        string autorId, string autor, string origem)
    {
        var novo = Normalizar(novoStatus);
        if (novo.Length == 0) throw new Exception($"Status inválido. Use: {string.Join(", ", Todos)}.");
        var atual = Exibir(pedido.Status);
        if (atual == Cancelado) throw new Exception("Este pedido já foi cancelado e não pode mudar de status.");
        if (atual == novo) return 0;

        pedido.Status = novo;
        if (novo != Cancelado) return 0;
        var (devolvido, _) = await EstoqueService.DevolverPedidoAsync(db, pedido, autorId, autor, origem);
        return devolvido;
    }

    // -----------------------------------------------------------------------
    // Item 11.4 (26/09): pedido feito pela AREA DO CLIENTE. Estava no
    // ClientPortalController; veio para ca sem mudar regra nem mensagem.
    // -----------------------------------------------------------------------

    /// <summary>Resultado de um pedido do cliente: o pedido gravado, o produto (saldo ja baixado),
    /// a unidade de retirada e o alerta de estoque (quando o saldo cruzou o minimo).</summary>
    public sealed record PedidoCriado(Pedido Pedido, Produto Produto, Unidade Retirada, EstoqueService.Alerta? Alerta);

    /// <summary>
    /// Pedido da loja pela area do cliente. Grava o pedido e a baixa no livro de estoque
    /// (movimentacao "venda") no MESMO SaveChanges: ou as duas coisas acontecem ou nenhuma.
    /// Depois o pedido ganha o pagamento (item 8).
    /// </summary>
    public static async Task<PedidoCriado> CriarDoClienteAsync(LanePetsDbContext db, Cliente cliente,
        string? produtoId, int quantidade, string? formaPagamento, string? unidade)
    {
        var produto = await db.Produtos.FindAsync(produtoId) ?? throw new Exception("Produto não localizado.");
        if (quantidade is < 1 or > 99) throw new Exception("Informe uma quantidade entre 1 e 99.");
        if (!produto.VisivelLoja) throw new Exception($"{produto.Nome} não está disponível na loja no momento.");

        // Item 5: o cliente escolhe a unidade de retirada. Com uma unica
        // unidade ativa, ela e usada automaticamente.
        var ativas = await db.Unidades.AsNoTracking().Where(u => u.Ativa).ToListAsync();
        var retirada = string.IsNullOrWhiteSpace(unidade) && ativas.Count == 1
            ? ativas[0]
            : ativas.FirstOrDefault(u => u.Id == Normalizador.IdUnidade(unidade));
        if (retirada is null) throw new Exception("Escolha a unidade onde você vai retirar o pedido.");

        // O controle de estoque e opcional por produto: so vale para os que a
        // equipe marcou como controlados no painel. Produto sem controle
        // continua vendendo normalmente, sem baixa.
        if (produto.ControlaEstoque && produto.Estoque < quantidade)
            throw new Exception(produto.Estoque <= 0
                ? $"{produto.Nome} está sem estoque no momento."
                : $"Temos apenas {produto.Estoque} unidade(s) de {produto.Nome} em estoque.");

        var pedido = new Pedido
        {
            Id = "PED-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            ClienteId = cliente.Id,
            ProdutoId = produto.Id,
            ProdutoNome = produto.Nome,
            Quantidade = quantidade,
            Total = produto.ValorVenda * quantidade,
            FormaPagamento = string.IsNullOrWhiteSpace(formaPagamento) ? "A combinar" : formaPagamento,
            Status = Pendente,
            Unidade = retirada.Id
        };
        // Item 7: a baixa vira movimentacao "venda" no livro de estoque,
        // no mesmo SaveChanges do pedido.
        var alerta = EstoqueService.Movimentar(db, produto, -quantidade, EstoqueService.Venda,
            $"Pedido {pedido.Id}", cliente.Id, cliente.Nome, "cliente", pedido.Id);
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        await PagamentosService.ReconciliarAsync(db);
        return new PedidoCriado(pedido, produto, retirada, alerta);
    }

    /// <summary>
    /// Itens 6/7: o cliente cancela o proprio pedido enquanto ele esta Pendente.
    /// O estoque baixado volta pelo livro de estoque. Devolve o pedido e quantas unidades voltaram.
    /// </summary>
    public static async Task<(Pedido Pedido, int Devolvido)> CancelarDoClienteAsync(LanePetsDbContext db, Cliente cliente, string id)
    {
        var pedido = await db.Pedidos.FirstOrDefaultAsync(p => p.Id == id && p.ClienteId == cliente.Id)
                     ?? throw new Exception("Pedido não localizado na sua conta.");
        var atual = Exibir(pedido.Status);
        if (atual == Cancelado) throw new Exception("Este pedido já está cancelado.");
        if (atual != Pendente)
            throw new Exception("Este pedido já foi confirmado pela equipe. Para cancelar, fale com a LanePets.");
        var devolvido = await AlterarStatusAsync(db, pedido, Cancelado, cliente.Id, cliente.Nome, "cliente");
        await db.SaveChangesAsync();
        await PagamentosService.ReconciliarAsync(db);
        return (pedido, devolvido);
    }
}
