using System.ComponentModel.DataAnnotations;

namespace PetshopCSharp.Models;

public class Usuario
{
    public int Id { get; set; }

    [Required]
    public string Nome { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Senha { get; set; } = string.Empty;

    public string Tipo { get; set; } = "cliente";
}
