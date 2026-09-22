using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetshopCSharp.Data;
using PetshopCSharp.Extensions;
using PetshopCSharp.Models;

namespace PetshopCSharp.Controllers;

public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;

    public AdminController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? status, string? busca, DateTime? inicio, DateTime? fim)
    {
        var usuario = HttpContext.Session.GetUsuario(_context);
        if (usuario is null || usuario.Tipo != "admin")
        {
            return RedirectToAction("Login", "Account");
        }

        var pedidosQuery = _context.Pedidos
            .Include(p => p.Usuario)
            .Include(p => p.Itens)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            pedidosQuery = pedidosQuery.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            pedidosQuery = pedidosQuery.Where(p =>
                p.NomeCliente.Contains(termo) ||
                p.Endereco.Contains(termo) ||
                p.Telefone.Contains(termo) ||
                p.Itens.Any(i => i.ProdutoNome.Contains(termo)));
        }

        if (inicio.HasValue)
        {
            pedidosQuery = pedidosQuery.Where(p => p.DataPedido >= inicio.Value.Date);
        }

        if (fim.HasValue)
        {
            pedidosQuery = pedidosQuery.Where(p => p.DataPedido <= fim.Value.Date.AddDays(1));
        }

        var dashboard = new DashboardViewModel
        {
            Produtos = await _context.Produtos.OrderByDescending(p => p.Id).ToListAsync(),
            Agendamentos = await _context.Agendamentos
                .Include(a => a.Usuario)
                .OrderByDescending(a => a.DataAgendada)
                .ToListAsync(),
            Pedidos = await pedidosQuery
                .OrderByDescending(p => p.DataPedido)
                .ToListAsync()
        };

        ViewBag.StatusAtual = status ?? string.Empty;
        ViewBag.BuscaAtual = busca ?? string.Empty;
        ViewBag.InicioAtual = inicio?.ToString("yyyy-MM-dd") ?? string.Empty;
        ViewBag.FimAtual = fim?.ToString("yyyy-MM-dd") ?? string.Empty;
        return View(dashboard);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProduct(ProdutoFormViewModel model)
    {
        var usuario = HttpContext.Session.GetUsuario(_context);
        if (usuario is null || usuario.Tipo != "admin")
        {
            return RedirectToAction("Login", "Account");
        }

        var rawPreco = Request.Form["Preco"].ToString();
        var valorPreco = ParsePreco(rawPreco);

        if (!ModelState.IsValid || !valorPreco.HasValue)
        {
            TempData["Mensagem"] = "Dados do produto inválidos.";
            return RedirectToAction(nameof(Index));
        }

        _context.Produtos.Add(new Produto
        {
            Nome = model.Nome.Trim(),
            Preco = valorPreco.Value,
            Descricao = model.Descricao?.Trim(),
            Imagem = model.Imagem?.Trim(),
            Disponivel = true
        });

        await _context.SaveChangesAsync();
        TempData["Mensagem"] = "Produto cadastrado com sucesso!";
        return RedirectToAction(nameof(Index));
    }

    private static decimal? ParsePreco(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var valor = value.Trim();

        if (valor.Contains(".") && valor.Contains(","))
        {
            valor = valor.Replace(".", string.Empty).Replace(",", ".");
        }
        else if (valor.Contains(",") && !valor.Contains("."))
        {
            valor = valor.Replace(",", ".");
        }

        if (decimal.TryParse(valor, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    [HttpGet]
    public async Task<IActionResult> ToggleProduto(int id)
    {
        var usuario = HttpContext.Session.GetUsuario(_context);
        if (usuario is null || usuario.Tipo != "admin")
        {
            return RedirectToAction("Login", "Account");
        }

        var produto = await _context.Produtos.FindAsync(id);
        if (produto is not null)
        {
            produto.Disponivel = !produto.Disponivel;
            await _context.SaveChangesAsync();
            TempData["Mensagem"] = "Status do produto atualizado!";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarStatusPedido(int id, string status)
    {
        var usuario = HttpContext.Session.GetUsuario(_context);
        if (usuario is null || usuario.Tipo != "admin")
        {
            return RedirectToAction("Login", "Account");
        }

        var statusValidos = new[]
        {
            "Confirmado",
            "Em preparação",
            "Saiu para entrega",
            "Entregue",
            "Cancelado"
        };

        if (!statusValidos.Contains(status))
        {
            TempData["Mensagem"] = "Status inválido.";
            return RedirectToAction(nameof(Index));
        }

        var pedido = await _context.Pedidos.FirstOrDefaultAsync(p => p.Id == id);
        if (pedido is not null)
        {
            pedido.Status = status;
            await _context.SaveChangesAsync();
            TempData["Mensagem"] = "Status do pedido atualizado!";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> CancelarAgendamento(int id)
    {
        var usuario = HttpContext.Session.GetUsuario(_context);
        if (usuario is null || usuario.Tipo != "admin")
        {
            return RedirectToAction("Login", "Account");
        }

        var agendamento = await _context.Agendamentos.FindAsync(id);
        if (agendamento is not null)
        {
            _context.Agendamentos.Remove(agendamento);
            await _context.SaveChangesAsync();
            TempData["Mensagem"] = "Agendamento cancelado com sucesso!";
        }

        return RedirectToAction(nameof(Index));
    }
}
