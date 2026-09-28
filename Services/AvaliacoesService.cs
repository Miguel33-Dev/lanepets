using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Item 11.4c (26/09): avaliacoes (depoimentos) enviadas pela AREA DO CLIENTE. Estava no
/// ClientPortalController; veio para ca sem mudar regra nem mensagem. Toda avaliacao nasce
/// "Pendente" e so aparece no site depois da moderacao no painel.
/// Desde 26/09 e o unico caminho para avaliar (o formulario anonimo do site, que aceitava
/// nome + telefone, foi removido). A moderacao continua no PublicController.
/// </summary>
public static class AvaliacoesService
{
    public const int ComentarioMinimo = 10;
    public const int ComentarioMaximo = 500;

    /// <summary>Avaliacoes do proprio cliente, com o status da moderacao.</summary>
    public static Task<List<Depoimento>> DoClienteAsync(LanePetsDbContext db, string clienteId)
        => db.Depoimentos.AsNoTracking()
            .Where(d => d.ClienteId == clienteId)
            .OrderByDescending(d => d.CriadoEm)
            .ToListAsync();

    /// <summary>
    /// Grava uma avaliacao ja vinculada a conta. O pet informado precisa ser da conta (26/09:
    /// pet de outra conta e recusado); sem pet informado, usa o primeiro pet da conta, como antes.
    /// </summary>
    public static async Task<Depoimento> CriarDoClienteAsync(LanePetsDbContext db, Cliente cliente, string? petId, int avaliacao, string? comentarioInformado)
    {
        var comentario = (comentarioInformado ?? "").Trim();
        if (comentario.Length < ComentarioMinimo) throw new Exception("Escreva um comentario com pelo menos 10 caracteres.");
        if (avaliacao is < 1 or > 5) throw new Exception("Escolha uma nota de 1 a 5 estrelas.");

        var pet = string.IsNullOrWhiteSpace(petId)
            ? await db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.ClienteId == cliente.Id)
            : await db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == petId && p.ClienteId == cliente.Id)
              ?? throw new Exception("Pet nao localizado na sua conta.");

        var depoimento = new Depoimento
        {
            Id = "DEP-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            ClienteId = cliente.Id,
            NomeCliente = cliente.Nome,
            NomePet = pet?.PetNome ?? "",
            Telefone = cliente.Telefone,
            Avaliacao = avaliacao,
            Comentario = comentario.Length > ComentarioMaximo ? comentario[..ComentarioMaximo] : comentario,
            Status = "Pendente"
        };
        db.Depoimentos.Add(depoimento);
        await db.SaveChangesAsync();
        return depoimento;
    }
}
