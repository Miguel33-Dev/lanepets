namespace PetshopCSharp.Models;

public class Produto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public decimal Preco { get; set; }
    public string? Descricao { get; set; }
    public string? Imagem { get; set; }
    public bool Disponivel { get; set; } = true;
}
