using System.ComponentModel.DataAnnotations;

namespace PetshopCSharp.Models;

public class AgendamentoViewModel
{
    [Required(ErrorMessage = "Informe o nome do pet.")]
    public string PetNome { get; set; } = string.Empty;

    [Required(ErrorMessage = "Selecione um serviço.")]
    public string Servico { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o telefone de contato.")]
    public string Telefone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a data e hora do agendamento.")]
    public DateTime DataAgendada { get; set; } = DateTime.Now.AddDays(1);
}
