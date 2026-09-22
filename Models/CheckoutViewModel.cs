using System.ComponentModel.DataAnnotations;

namespace PetshopCSharp.Models;

public class CheckoutViewModel
{
    public CartViewModel Cart { get; set; } = new();

    public List<CartItemViewModel> Items => Cart.Items;
    public decimal Total => Cart.Total;

    [Required(ErrorMessage = "Informe o seu nome.")]
    [Display(Name = "Nome completo")]
    public string NomeCliente { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o seu telefone.")]
    [Phone(ErrorMessage = "Telefone inválido.")]
    [Display(Name = "Telefone")]
    public string Telefone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o endereço para entrega.")]
    [Display(Name = "Endereço")]
    public string Endereco { get; set; } = string.Empty;

    [Required(ErrorMessage = "Selecione uma forma de pagamento.")]
    [Display(Name = "Forma de pagamento")]
    public string FormaPagamento { get; set; } = "PIX";

    [Display(Name = "Observação")]
    public string? Observacao { get; set; }
}
