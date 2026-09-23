using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetshopCSharp.Data;

namespace PetshopCSharp.Controllers;

public class ProdutoController : Controller
{
    private readonly ApplicationDbContext _context;

    public ProdutoController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? busca)
    {
        var query = _context.Produtos.AsQueryable();

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            query = query.Where(p =>
                p.Nome.Contains(termo) ||
                (p.Descricao != null && p.Descricao.Contains(termo)));
        }

        var produtos = await query
            .OrderByDescending(p => p.Id)
            .ToListAsync();

        ViewBag.Busca = busca ?? string.Empty;
        return View(produtos);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var produto = await _context.Produtos.FirstOrDefaultAsync(p => p.Id == id);
        if (produto is null)
        {
            TempData["Mensagem"] = "Produto não encontrado.";
            return RedirectToAction(nameof(Index));
        }

        return View(produto);
    }
}
