using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Item 7 do roadmap (24/09): livro de estoque.
///
/// REGRA UNICA: o saldo de um produto so muda por aqui. Cada mudanca
/// acrescenta uma <see cref="MovimentacaoEstoque"/> no MESMO DbContext — a
/// linha e o saldo vao juntos no SaveChanges de quem chamou, ou nenhum dos
/// dois vai. Estoque e unico por produto (decisao do Fabricio: nao e por unidade).
///
/// Produto sem controle de estoque (ControlaEstoque = false) nao tem saldo:
/// nenhuma movimentacao e registrada para ele.
/// </summary>
public static class EstoqueService
{
    public const string Entrada = "entrada";
    public const string Saida = "saida";
    public const string Ajuste = "ajuste";
    public const string Venda = "venda";
    public const string Cancelamento = "cancelamento";

    public static readonly string[] TiposManuais = [Entrada, Saida, Ajuste];

    /// <summary>Aviso para o log de eventos quando o produto cruza o minimo.</summary>
    public record Alerta(string ProdutoId, string ProdutoNome, int Saldo, int Minimo, bool Zerado);

    public static bool EhBaixo(Produto p) => p.ControlaEstoque && p.Estoque <= p.EstoqueMinimo;

    /// <summary>"livre" (sem controle) · "zero" · "baixo" (≤ minimo) · "ok".</summary>
    public static string Situacao(Produto p) =>
        !p.ControlaEstoque ? "livre" : p.Estoque <= 0 ? "zero" : p.Estoque <= p.EstoqueMinimo ? "baixo" : "ok";

    /// <summary>
    /// Aplica uma variacao (com sinal) no saldo e registra a movimentacao.
    /// Recusa deixar o saldo negativo. Devolve o alerta quando o produto
    /// ACABOU de entrar na faixa de estoque baixo (nao a cada venda abaixo dele).
    /// </summary>
    public static Alerta? Movimentar(LanePetsDbContext db, Produto produto, int variacao, string tipo, string motivo,
        string autorId = "", string autor = "", string origem = "admin", string pedidoId = "")
    {
        if (!produto.ControlaEstoque || variacao == 0) return null;
        var antes = produto.Estoque;
        var depois = antes + variacao;
        if (depois < 0)
            throw new Exception(antes <= 0
                ? $"{produto.Nome} está sem estoque."
                : $"Saída maior que o saldo: {produto.Nome} tem {antes} unidade(s).");

        var estavaBaixo = EhBaixo(produto);
        produto.Estoque = depois;
        db.MovimentacoesEstoque.Add(new MovimentacaoEstoque
        {
            Id = "MOV-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            DataHora = DateTime.UtcNow,
            ProdutoId = produto.Id,
            ProdutoNome = produto.Nome,
            Tipo = tipo,
            Quantidade = variacao,
            SaldoAnterior = antes,
            SaldoNovo = depois,
            Motivo = Limitar(motivo, 200),
            PedidoId = pedidoId,
            Origem = origem,
            AutorId = autorId,
            Autor = Limitar(autor, 120)
        });
        return !estavaBaixo && EhBaixo(produto) ? new Alerta(produto.Id, produto.Nome, depois, produto.EstoqueMinimo, depois <= 0) : null;
    }

    /// <summary>Leva o saldo a um valor exato (ajuste de inventario).</summary>
    public static Alerta? AjustarPara(LanePetsDbContext db, Produto produto, int novoSaldo, string motivo,
        string autorId = "", string autor = "", string origem = "admin")
    {
        if (novoSaldo < 0) throw new Exception("O estoque não pode ser negativo.");
        return Movimentar(db, produto, novoSaldo - produto.Estoque, Ajuste, motivo, autorId, autor, origem);
    }

    /// <summary>
    /// Devolve ao estoque o que um pedido baixou. So devolve se existe a
    /// movimentacao de VENDA desse pedido (pedido antigo, de antes do livro,
    /// ou de produto sem controle, nao tem o que devolver) e se ainda nao foi
    /// devolvido. Retorna a quantidade devolvida (0 = nada).
    /// </summary>
    public static async Task<(int Devolvido, Alerta? Alerta)> DevolverPedidoAsync(LanePetsDbContext db, Pedido pedido,
        string autorId, string autor, string origem)
    {
        var movs = await db.MovimentacoesEstoque.AsNoTracking().Where(m => m.PedidoId == pedido.Id).ToListAsync();
        var vendido = -movs.Where(m => m.Tipo == Venda).Sum(m => m.Quantidade);
        var devolvido = movs.Where(m => m.Tipo == Cancelamento).Sum(m => m.Quantidade);
        var aDevolver = vendido - devolvido;
        if (aDevolver <= 0) return (0, null);
        var produto = await db.Produtos.FirstOrDefaultAsync(p => p.Id == pedido.ProdutoId);
        if (produto is null) return (0, null);
        // Devolve mesmo que o controle tenha sido desligado depois: o saldo
        // guardado continua sendo o saldo do produto.
        var controla = produto.ControlaEstoque;
        produto.ControlaEstoque = true;
        Movimentar(db, produto, aDevolver, Cancelamento, $"Pedido {pedido.Id} cancelado", autorId, autor, origem, pedido.Id);
        produto.ControlaEstoque = controla;
        return (aDevolver, null);
    }

    public static string RotuloTipo(string tipo) => tipo switch
    {
        Entrada => "Entrada",
        Saida => "Saída",
        Ajuste => "Ajuste",
        Venda => "Venda",
        Cancelamento => "Pedido cancelado",
        _ => tipo
    };

    private static string Limitar(string? texto, int max)
    {
        var t = (texto ?? "").Trim();
        return t.Length > max ? t[..max] : t;
    }
}
