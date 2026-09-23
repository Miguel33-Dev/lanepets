namespace PetshopCSharp.Models;

public class DashboardViewModel
{
    public List<Produto> Produtos { get; set; } = new();
    public List<Agendamento> Agendamentos { get; set; } = new();
    public List<Pedido> Pedidos { get; set; } = new();
}
