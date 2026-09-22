using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;

[Route("api")]
public class PublicController(LanePetsDbContext db, SessionService sessions) : ApiControllerBase
{
    [HttpGet("public/servicos")]
    public async Task<IActionResult> Servicos() => OkApi((await db.Servicos.AsNoTracking().OrderBy(s => s.Nome).ToListAsync()).Select(s => new { s.Id, s.Nome, s.Preco, s.Porte }));

    [HttpGet("public/produtos")]
    public async Task<IActionResult> Produtos() => OkApi((await db.Produtos.AsNoTracking().OrderBy(p => p.Nome).ToListAsync()).Select(p => new { p.Id, p.Nome, p.Categoria, p.ValorVenda }));

    [HttpGet("public/depoimentos")]
    public async Task<IActionResult> Depoimentos() => OkApi((await db.Depoimentos.AsNoTracking().Where(d => d.Status == "Aprovado").OrderByDescending(d => d.CriadoEm).ToListAsync()).Select(d => new { d.Id, d.NomeCliente, d.NomePet, d.Avaliacao, d.Comentario, d.CriadoEm }));

    [HttpGet("public/seguros")]
    public async Task<IActionResult> Seguros() => OkApi(await db.PlanosSeguro.AsNoTracking().Where(p => p.Ativo).OrderBy(p => p.ValorMensal).ToListAsync());

    [HttpPost("public/depoimentos")]
    public async Task<IActionResult> CriarDepoimento([FromBody] DepoimentoRequest request)
    {
        try
        {
            var nome = (request.Nome ?? "").Trim(); var telefone = NormalizarTelefone(request.Telefone);
            if (nome.Length < 3 || telefone.Length < 8 || string.IsNullOrWhiteSpace(request.Comentario)) throw new Exception("Informe seu nome, telefone e comentário.");
            if (request.Avaliacao is < 1 or > 5) throw new Exception("A avaliação deve ter entre 1 e 5 estrelas.");
            var cliente = (await db.Clientes.AsNoTracking().ToListAsync()).FirstOrDefault(c => NormalizarTelefone(c.Telefone) == telefone && string.Equals(c.Nome, nome, StringComparison.OrdinalIgnoreCase));
            if (cliente is null) throw new Exception("Seu cadastro não foi localizado. Use os mesmos nome e telefone cadastrados no LanePets.");
            var pet = await db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.ClienteId == cliente.Id && p.PetNome.ToLower() == (request.Pet ?? "").Trim().ToLower());
            if (pet is null) throw new Exception("Pet não localizado para este cadastro.");
            db.Depoimentos.Add(new Depoimento { Id = "DEP-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), ClienteId = cliente.Id, NomeCliente = cliente.Nome, NomePet = pet.PetNome, Telefone = cliente.Telefone, Avaliacao = request.Avaliacao, Comentario = request.Comentario.Trim(), Status = "Pendente" });
            await db.SaveChangesAsync(); return OkApi(new { message = "Obrigado! Seu comentário foi enviado para moderação." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpPost("public/seguros/solicitacoes")]
    public async Task<IActionResult> ContratarSeguro([FromBody] SeguroRequest request)
    {
        try
        {
            var plano = await db.PlanosSeguro.FirstOrDefaultAsync(p => p.Id == request.PlanoId && p.Ativo) ?? throw new Exception("Plano indisponível.");
            if (string.IsNullOrWhiteSpace(request.Nome) || NormalizarTelefone(request.Telefone).Length < 8 || string.IsNullOrWhiteSpace(request.Pet)) throw new Exception("Informe seu nome, telefone e nome do pet.");
            db.SolicitacoesSeguro.Add(new SolicitacaoSeguro { Id = "SOL-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), PlanoSeguroId = plano.Id, NomePlano = plano.Nome, NomeCliente = request.Nome.Trim(), Telefone = request.Telefone.Trim(), NomePet = request.Pet.Trim(), Observacao = (request.Observacao ?? "").Trim() });
            await db.SaveChangesAsync(); return OkApi(new { message = "Recebemos seu pedido. A equipe LanePets entrará em contato para concluir a contratação." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpGet("admin/depoimentos")]
    public async Task<IActionResult> ListarDepoimentos([FromQuery] string token = "") { try { sessions.RequireAdmin(token); return OkApi(await db.Depoimentos.AsNoTracking().OrderByDescending(d => d.CriadoEm).ToListAsync()); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpGet("admin/seguros/solicitacoes")]
    public async Task<IActionResult> ListarSolicitacoes([FromQuery] string token = "") { try { sessions.RequireAdmin(token); return OkApi(await db.SolicitacoesSeguro.AsNoTracking().OrderByDescending(s => s.CriadoEm).ToListAsync()); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpGet("admin/seguros")]
    public async Task<IActionResult> ListarPlanos([FromQuery] string token = "") { try { sessions.RequireAdmin(token); return OkApi(await db.PlanosSeguro.AsNoTracking().OrderBy(p => p.ValorMensal).ToListAsync()); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpPost("admin/seguros")]
    public async Task<IActionResult> CriarPlano([FromBody] PlanoRequest request) { try { sessions.RequireAdmin(request.Token ?? ""); if (string.IsNullOrWhiteSpace(request.Nome) || request.ValorMensal <= 0) throw new Exception("Informe nome e valor do plano."); var plano = new PlanoSeguro { Id = "SEG-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), Nome = request.Nome.Trim(), Descricao = (request.Descricao ?? "").Trim(), Coberturas = (request.Coberturas ?? "").Trim(), Beneficios = (request.Beneficios ?? "").Trim(), ValorMensal = request.ValorMensal, Ativo = true }; db.PlanosSeguro.Add(plano); await db.SaveChangesAsync(); return OkApi(plano); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpPost("admin/seguros/{id}/ativo")]
    public async Task<IActionResult> AtualizarPlano(string id, [FromBody] PlanoStatusRequest request) { try { sessions.RequireAdmin(request.Token ?? ""); var plano = await db.PlanosSeguro.FindAsync(id) ?? throw new Exception("Plano não encontrado."); plano.Ativo = request.Ativo; await db.SaveChangesAsync(); return OkApi(plano); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpPost("admin/depoimentos/{id}/status")]
    public async Task<IActionResult> Moderar(string id, [FromBody] StatusRequest request) { try { sessions.RequireAdmin(request.Token ?? ""); var item = await db.Depoimentos.FindAsync(id) ?? throw new Exception("Depoimento não encontrado."); item.Status = request.Status is "Aprovado" or "Recusado" ? request.Status : throw new Exception("Status inválido."); await db.SaveChangesAsync(); return OkApi(item); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpPost("admin/solicitacoes/{id}/status")]
    public async Task<IActionResult> AtualizarSolicitacao(string id, [FromBody] StatusRequest request) { try { sessions.RequireAdmin(request.Token ?? ""); var item = await db.SolicitacoesSeguro.FindAsync(id) ?? throw new Exception("Solicitação não encontrada."); item.Status = request.Status is "Pendente" or "Em contato" or "Concluída" or "Cancelada" ? request.Status : throw new Exception("Status inválido."); await db.SaveChangesAsync(); return OkApi(item); } catch (Exception ex) { return ErrorApi(ex); } }
    private static string NormalizarTelefone(string? value) => new string((value ?? "").Where(char.IsDigit).ToArray());
    public record DepoimentoRequest(string? Nome, string? Telefone, string? Pet, int Avaliacao, string? Comentario);
    public record SeguroRequest(string PlanoId, string? Nome, string? Telefone, string? Pet, string? Observacao);
    public record StatusRequest(string? Token, string Status);
    public record PlanoRequest(string? Token, string? Nome, string? Descricao, string? Coberturas, string? Beneficios, decimal ValorMensal);
    public record PlanoStatusRequest(string? Token, bool Ativo);
}
