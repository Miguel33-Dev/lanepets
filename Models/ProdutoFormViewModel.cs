using System.ComponentModel.DataAnnotations;

namespace PetshopCSharp.Models;

public class ProdutoFormViewModel
{
    [Required(ErrorMessage = "Informe o nome do produto.")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o preço do produto.")]
    [Range(0.01, double.MaxValue, ErrorMessage = "O preço deve ser maior que zero.")]
    public decimal Preco { get; set; }

    public string? Descricao { get; set; }

    public string? Imagem { get; set; }
}
