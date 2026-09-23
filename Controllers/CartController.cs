using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetshopCSharp.Data;
using PetshopCSharp.Extensions;
using PetshopCSharp.Models;

namespace PetshopCSharp.Controllers;

public class CartController : Controller
{
    private readonly ApplicationDbContext _context;

    public CartController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var cart = HttpContext.Session.GetCart();
        return View(new CartViewModel { Items = cart });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId, int quantity = 1)
    {
        if (productId <= 0 || quantity <= 0)
        {
            return RedirectToAction("Index", "Produto");
        }

        var produto = await _context.Produtos.FirstOrDefaultAsync(p => p.Id == productId && p.Disponivel);
        if (produto is null)
        {
            TempData["Mensagem"] = "Este produto não está disponível no momento.";
            return RedirectToAction("Index", "Produto");
        }

        var cart = HttpContext.Session.GetCart();
        var item = cart.FirstOrDefault(i => i.ProductId == productId);

        if (item is null)
        {
            cart.Add(new CartItemViewModel
            {
                ProductId = produto.Id,
                Nome = produto.Nome,
                Preco = produto.Preco,
                Quantidade = quantity
            });
        }
        else
        {
            item.Quantidade += quantity;
        }

        HttpContext.Session.SetCart(cart);
        TempData["Mensagem"] = $"{produto.Nome} adicionado ao carrinho.";
        return RedirectToAction("Index", "Produto");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateQuantity(int productId, int quantity)
    {
        var cart = HttpContext.Session.GetCart();
        var item = cart.FirstOrDefault(i => i.ProductId == productId);
        if (item is null)
        {
            return RedirectToAction(nameof(Index));
        }

        if (quantity <= 0)
        {
            cart.Remove(item);
        }
        else
        {
            item.Quantidade = quantity;
        }

        HttpContext.Session.SetCart(cart);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Remove(int productId)
    {
        var cart = HttpContext.Session.GetCart();
        var item = cart.FirstOrDefault(i => i.ProductId == productId);
        if (item is not null)
        {
            cart.Remove(item);
            HttpContext.Session.SetCart(cart);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Checkout()
    {
        var cart = HttpContext.Session.GetCart();
        if (cart.Count == 0)
        {
            TempData["Mensagem"] = "Seu carrinho está vazio.";
            return RedirectToAction(nameof(Index));
        }

        var usuario = HttpContext.Session.GetUsuario(_context);
        var model = new CheckoutViewModel
        {
            Cart = new CartViewModel { Items = cart },
            NomeCliente = usuario?.Nome ?? string.Empty
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalizarCompra(CheckoutViewModel model)
    {
        var cart = HttpContext.Session.GetCart();
        if (cart.Count == 0)
        {
            TempData["Mensagem"] = "Seu carrinho está vazio.";
            return RedirectToAction(nameof(Index));
        }

        model.Cart = new CartViewModel { Items = cart };

        if (!ModelState.IsValid)
        {
            return View("Checkout", model);
        }

        var usuario = HttpContext.Session.GetUsuario(_context);
        var pedido = new Pedido
        {
            UsuarioId = usuario?.Id,
            NomeCliente = model.NomeCliente.Trim(),
            Telefone = model.Telefone.Trim(),
            Endereco = model.Endereco.Trim(),
            Observacao = string.IsNullOrWhiteSpace(model.Observacao) ? null : model.Observacao.Trim(),
            FormaPagamento = string.IsNullOrWhiteSpace(model.FormaPagamento) ? "PIX" : model.FormaPagamento,
            DataPedido = DateTime.Now,
            Status = "Confirmado",
            Total = cart.Sum(i => i.Total),
            Itens = cart.Select(item => new PedidoItem
            {
                ProdutoId = item.ProductId,
                ProdutoNome = item.Nome,
                Quantidade = item.Quantidade,
                PrecoUnitario = item.Preco
            }).ToList()
        };

        _context.Pedidos.Add(pedido);
        await _context.SaveChangesAsync();

        HttpContext.Session.ClearCart();
        TempData["Mensagem"] = "Pedido realizado com sucesso! Seu pedido foi registrado e está em análise.";
        return RedirectToAction(nameof(PedidoConfirmado), new { id = pedido.Id });
    }

    [HttpGet]
    public async Task<IActionResult> PedidoConfirmado(int id)
    {
        var pedido = await _context.Pedidos
            .Include(p => p.Itens)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (pedido is null)
        {
            return RedirectToAction("Index", "Produto");
        }

        return View(pedido);
    }
}
