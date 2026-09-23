using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetshopCSharp.Data;
using PetshopCSharp.Extensions;
using PetshopCSharp.Models;

namespace PetshopCSharp.Controllers;

public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;

    public AccountController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Email == model.Email.Trim());

        if (usuario is not null && BCrypt.Net.BCrypt.Verify(model.Senha, usuario.Senha))
        {
            var usuarioAtual = usuario;
            HttpContext.Session.SetUsuario(usuarioAtual);

            if (usuarioAtual.Tipo == "admin")
            {
                return RedirectToAction("Index", "Admin");
            }

            return RedirectToAction("Index", "Home");
        }

        ModelState.AddModelError(string.Empty, "E-mail ou senha inválidos.");
        return View(model);
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var emailJaCadastrado = await _context.Usuarios.AnyAsync(u => u.Email == model.Email.Trim());
        if (emailJaCadastrado)
        {
            ModelState.AddModelError(string.Empty, "Este e-mail já está cadastrado.");
            return View(model);
        }

        var usuario = new Usuario
        {
            Nome = model.Nome.Trim(),
            Email = model.Email.Trim(),
            Senha = BCrypt.Net.BCrypt.HashPassword(model.Senha),
            Tipo = "cliente"
        };

        _context.Usuarios.Add(usuario);
        await _context.SaveChangesAsync();

        HttpContext.Session.SetUsuario(usuario);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public async Task<IActionResult> MeusPedidos()
    {
        var usuario = HttpContext.Session.GetUsuario(_context);
        if (usuario is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var pedidos = await _context.Pedidos
            .Include(p => p.Itens)
            .Where(p => p.UsuarioId == usuario.Id || p.NomeCliente == usuario.Nome)
            .OrderByDescending(p => p.DataPedido)
            .ToListAsync();

        return View(pedidos);
    }

    [HttpGet]
    public async Task<IActionResult> DetalhesPedido(int id)
    {
        var usuario = HttpContext.Session.GetUsuario(_context);
        if (usuario is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var pedido = await _context.Pedidos
            .Include(p => p.Itens)
            .FirstOrDefaultAsync(p => p.Id == id && (p.UsuarioId == usuario.Id || p.NomeCliente == usuario.Nome));

        if (pedido is null)
        {
            TempData["Mensagem"] = "Pedido não encontrado.";
            return RedirectToAction(nameof(MeusPedidos));
        }

        return View(pedido);
    }

    [HttpGet]
    public IActionResult Logout()
    {
        HttpContext.Session.ClearUsuario();
        return RedirectToAction("Index", "Home");
    }
}
