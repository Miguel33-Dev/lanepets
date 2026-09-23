namespace PetshopCSharp.Models;

public class CartItemViewModel
{
    public int ProductId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public decimal Preco { get; set; }
    public int Quantidade { get; set; } = 1;

    public decimal Total => Preco * Quantidade;
}

public class CartViewModel
{
    public List<CartItemViewModel> Items { get; set; } = new();

    public decimal Total => Items.Sum(i => i.Total);
}
