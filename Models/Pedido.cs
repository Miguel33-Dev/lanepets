namespace PetshopCSharp.Models;

public class Pedido
{
    public int Id { get; set; }
    public int? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public string NomeCliente { get; set; } = string.Empty;
    public string Telefone { get; set; } = string.Empty;
    public string Endereco { get; set; } = string.Empty;
    public string? Observacao { get; set; }
    public string FormaPagamento { get; set; } = "PIX";
    public DateTime DataPedido { get; set; } = DateTime.Now;
    public string Status { get; set; } = "Confirmado";
    public decimal Total { get; set; }
    public List<PedidoItem> Itens { get; set; } = new();
}
