using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;

[Route("api/cliente")]
public class ClientPortalController(LanePetsDbContext db, SessionService sessions) : ApiControllerBase
{
    [HttpPost("cadastro")]
    public async Task<IActionResult> Cadastro([FromBody] CadastroRequest request)
    {
        try
        {
            var email = (request.Email ?? "").Trim().ToLowerInvariant();
            if (request.Nome?.Trim().Length < 3 || !email.Contains('@') || request.Senha?.Length < 8 || request.Pet?.Trim().Length < 2) throw new Exception("Informe nome, e-mail válido, senha com ao menos 8 caracteres e nome do pet.");
            if (await db.UsuariosClientes.AnyAsync(u => u.Email == email)) throw new Exception("Já existe uma conta com este e-mail.");
            var cliente = new Cliente { Id = "CLI-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Nome = request.Nome.Trim(), Telefone = (request.Telefone ?? "").Trim(), Endereco = (request.Endereco ?? "").Trim(), Origem = "portal_cliente", Status = "ativo" };
            var pet = new Pet { Id = "PET-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), ClienteId = cliente.Id, Dono = cliente.Nome, PetNome = request.Pet.Trim(), Tipo = (request.Tipo ?? "Não informado").Trim(), Raca = (request.Raca ?? "Não informada").Trim(), Telefone = cliente.Telefone, Endereco = cliente.Endereco };
            var (hash, salt) = SessionService.HashPassword(request.Senha); db.Clientes.Add(cliente); db.Pets.Add(pet); db.UsuariosClientes.Add(new UsuarioCliente { Id="USR-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), ClienteId=cliente.Id, Email=email, SenhaHash=hash, SenhaSalt=salt }); await db.SaveChangesAsync();
            return OkApi(new { token = sessions.CreateClient(cliente.Id), cliente = new { cliente.Nome, Email = email } });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    { try { var user = await db.UsuariosClientes.FirstOrDefaultAsync(u => u.Email == (request.Email ?? "").Trim().ToLowerInvariant()) ?? throw new Exception("E-mail ou senha inválidos."); if (!SessionService.VerifyPassword(request.Senha ?? "", user.SenhaHash, user.SenhaSalt)) throw new Exception("E-mail ou senha inválidos."); var cliente = await db.Clientes.FindAsync(user.ClienteId) ?? throw new Exception("Cadastro de cliente não localizado."); return OkApi(new { token=sessions.CreateClient(cliente.Id), cliente = new { cliente.Nome, user.Email } }); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpPost("logout")]
    public IActionResult Logout() { sessions.LogoutClient(Token()); return OkApi(new { encerrado = true }); }
    [HttpGet("conta")]
    public async Task<IActionResult> Conta() { try { var cliente = await Cliente(); return OkApi(new { cliente.Id, cliente.Nome, cliente.Telefone, cliente.Endereco, pets = await db.Pets.AsNoTracking().Where(p => p.ClienteId == cliente.Id).ToListAsync(), agendamentos = await db.Agendamentos.AsNoTracking().Where(a => a.ClienteId == cliente.Id).OrderByDescending(a => a.DataHora).ToListAsync(), pedidos = await db.Pedidos.AsNoTracking().Where(p => p.ClienteId == cliente.Id).OrderByDescending(p => p.CriadoEm).ToListAsync() }); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpGet("catalogo")]
    public async Task<IActionResult> Catalogo() => OkApi(new { servicos = await db.Servicos.AsNoTracking().OrderBy(s => s.Nome).ToListAsync(), produtos = await db.Produtos.AsNoTracking().OrderBy(p => p.Nome).ToListAsync(), unidades = await db.Unidades.AsNoTracking().Where(u => u.Ativa).ToListAsync() });
    [HttpGet("horarios")]
    public async Task<IActionResult> Horarios([FromQuery] string unidade, [FromQuery] string data)
    { try { if (!DateOnly.TryParse(data, out var day) || string.IsNullOrWhiteSpace(unidade)) throw new Exception("Informe unidade e data."); var occupied = await db.Agendamentos.AsNoTracking().Where(a => a.Unidade.ToLower() == unidade.ToLower() && a.DataHora.StartsWith(data) && a.Status != "Cancelado").Select(a => a.DataHora).ToListAsync(); var times = new[] { "09:00", "10:00", "11:00", "13:00", "14:00", "15:00", "16:00", "17:00" }.Where(time => !occupied.Any(value => value.Contains(time))).ToArray(); return OkApi(times); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpPost("agendamentos")]
    public async Task<IActionResult> Agendar([FromBody] AgendamentoRequest request)
    {
        try
        {
            var cliente = await Cliente(); var pet = await db.Pets.FirstOrDefaultAsync(p => p.Id == request.PetId && p.ClienteId == cliente.Id) ?? throw new Exception("Pet não localizado."); var service = await db.Servicos.FindAsync(request.ServicoId) ?? throw new Exception("Serviço não localizado.");
            if (!DateOnly.TryParse(request.Data, out _) || !TimeOnly.TryParse(request.Horario, out _) || string.IsNullOrWhiteSpace(request.Unidade)) throw new Exception("Escolha unidade, data e horário válidos.");
            var dataHora = request.Data + "T" + request.Horario + ":00"; var conflict = await db.Agendamentos.AnyAsync(a => a.Unidade.ToLower() == request.Unidade.ToLower() && a.DataHora == dataHora && a.Status != "Cancelado"); if (conflict) throw new Exception("Este horário acabou de ser ocupado. Escolha outro horário.");
            var total = service.Preco + (request.Transporte?.Contains("Busca", StringComparison.OrdinalIgnoreCase) == true ? 15m : 0m) + (request.Transporte?.Contains("Entrega", StringComparison.OrdinalIgnoreCase) == true ? 15m : 0m);
            var appointment = new Agendamento { Id="AGD-"+Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), ClienteId=cliente.Id, PetId=pet.Id, Pet=pet.PetNome, Dono=cliente.Nome, Telefone=cliente.Telefone, DataHora=dataHora, ServicosJson=$"[{{\"id\":\"{service.Id}\",\"nome\":\"{service.Nome.Replace("\"", "") }\"}}]", Total=total, Transporte=request.Transporte ?? "Cliente leva", ValorTransporte=total-service.Preco, Status="Pendente", PagamentoStatus="Pendente", FormaPagamento=request.FormaPagamento ?? "A combinar", Unidade=request.Unidade, Obs=(request.Observacao ?? "").Trim() }; db.Agendamentos.Add(appointment); await db.SaveChangesAsync(); return OkApi(appointment);
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
    [HttpPost("pedidos")]
    public async Task<IActionResult> CriarPedido([FromBody] PedidoRequest request)
    { try { var cliente = await Cliente(); var produto = await db.Produtos.FindAsync(request.ProdutoId) ?? throw new Exception("Produto não localizado."); if (request.Quantidade is < 1 or > 99) throw new Exception("Informe uma quantidade entre 1 e 99."); var pedido = new Pedido { Id = "PED-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), ClienteId = cliente.Id, ProdutoId = produto.Id, ProdutoNome = produto.Nome, Quantidade = request.Quantidade, Total = produto.ValorVenda * request.Quantidade, FormaPagamento = string.IsNullOrWhiteSpace(request.FormaPagamento) ? "A combinar" : request.FormaPagamento, Status = "Pendente" }; db.Pedidos.Add(pedido); await db.SaveChangesAsync(); return OkApi(pedido); } catch (Exception ex) { return ErrorApi(ex); } }
    private string Token() => Request.Headers["X-LanePets-Client"].FirstOrDefault() ?? "";
    private async Task<Cliente> Cliente() { var session = sessions.RequireClient(Token()); return await db.Clientes.FindAsync(session.AdminToken) ?? throw new UnauthorizedAccessException("Cliente não localizado."); }
    public record CadastroRequest(string? Nome, string? Email, string? Senha, string? Telefone, string? Endereco, string? Pet, string? Tipo, string? Raca);
    public record LoginRequest(string? Email, string? Senha);
    public record AgendamentoRequest(string PetId, string ServicoId, string Unidade, string Data, string Horario, string? Transporte, string? FormaPagamento, string? Observacao);
    public record PedidoRequest(string ProdutoId, int Quantidade, string? FormaPagamento);
}
