using System.ComponentModel.DataAnnotations;

namespace PetshopCSharp.Models;

public class Agendamento
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }

    [Required]
    public string PetNome { get; set; } = string.Empty;

    [Required]
    public string Servico { get; set; } = string.Empty;

    [Required]
    public string Telefone { get; set; } = string.Empty;

    [Required]
    public DateTime DataAgendada { get; set; }

    public Usuario? Usuario { get; set; }
}
