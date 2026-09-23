using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetshopCSharp.Data;
using PetshopCSharp.Extensions;
using PetshopCSharp.Models;

namespace PetshopCSharp.Controllers;

public class AgendamentoController : Controller
{
    private readonly ApplicationDbContext _context;

    public AgendamentoController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var usuario = HttpContext.Session.GetUsuario(_context);
        if (usuario is null)
        {
            TempData["Mensagem"] = "É necessário estar logado para agendar um serviço.";
            return RedirectToAction("Login", "Account");
        }

        return View(new AgendamentoViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(AgendamentoViewModel model)
    {
        var usuario = HttpContext.Session.GetUsuario(_context);
        if (usuario is null)
        {
            TempData["Mensagem"] = "É necessário estar logado para agendar um serviço.";
            return RedirectToAction("Login", "Account");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var agendamento = new Agendamento
        {
            UsuarioId = usuario.Id,
            PetNome = model.PetNome.Trim(),
            Servico = model.Servico.Trim(),
            Telefone = model.Telefone.Trim(),
            DataAgendada = model.DataAgendada
        };

        _context.Agendamentos.Add(agendamento);
        await _context.SaveChangesAsync();

        TempData["Mensagem"] = "Agendamento realizado com sucesso!";
        return RedirectToAction(nameof(Index));
    }
}
