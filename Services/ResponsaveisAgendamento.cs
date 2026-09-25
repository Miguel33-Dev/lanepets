using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Item 4 do roadmap (24/09): funcionario responsavel pelo agendamento.
///
/// Regra do Fabricio: so pode ser escolhido um usuario com perfil Funcionario,
/// ATIVO e vinculado a MESMA unidade do agendamento. O campo e opcional.
/// O cliente ve apenas o primeiro nome ("Atendimento com Ana").
/// </summary>
public static class ResponsaveisAgendamento
{
    public record Funcionario(string Id, string Nome, string Unidade, bool Ativo);

    public static async Task<List<Funcionario>> ListarAsync(LanePetsDbContext db) =>
        (await db.UsuariosAdministradores.AsNoTracking()
            .Where(u => u.Perfil == PerfilAdmin.Funcionario)
            .Select(u => new { u.Id, u.Nome, u.Email, u.Unidade, u.Ativo })
            .ToListAsync())
        .Select(u => new Funcionario(u.Id, string.IsNullOrWhiteSpace(u.Nome) ? u.Email : u.Nome.Trim(), Normalizador.IdUnidade(u.Unidade), u.Ativo))
        .OrderBy(f => f.Nome)
        .ToList();

    /// <summary>Id → nome completo, para montar as respostas do painel.</summary>
    public static async Task<Dictionary<string, string>> NomesAsync(LanePetsDbContext db) =>
        (await ListarAsync(db)).ToDictionary(f => f.Id, f => f.Nome);

    public static string PrimeiroNome(string? nome)
    {
        var partes = (nome ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0) return "";
        var primeiro = partes[0];
        // Nome vazio cai no e-mail: nunca mostrar o e-mail ao cliente.
        return primeiro.Contains('@') ? "" : primeiro;
    }

    /// <summary>
    /// Valida o responsavel escolhido no painel. "" = sem responsavel.
    /// Mudar a unidade de um agendamento com responsavel de outra unidade e recusado.
    /// </summary>
    public static async Task<string> ValidarAsync(LanePetsDbContext db, string? responsavelId, string unidadeDoAgendamento)
    {
        var id = (responsavelId ?? "").Trim();
        if (id.Length == 0) return "";
        var usuario = await db.UsuariosAdministradores.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (usuario is null || usuario.Perfil != PerfilAdmin.Funcionario)
            throw new Exception("O responsável precisa ser um usuário com perfil Funcionário.");
        if (!usuario.Ativo)
            throw new Exception("Este funcionário está desativado e não pode receber atendimentos.");
        var unidade = Normalizador.IdUnidade(unidadeDoAgendamento);
        if (unidade.Length == 0)
            throw new Exception("Escolha a unidade do agendamento antes de definir o responsável.");
        if (Normalizador.IdUnidade(usuario.Unidade) != unidade)
        {
            var nome = string.IsNullOrWhiteSpace(usuario.Nome) ? usuario.Email : usuario.Nome;
            throw new Exception($"{nome} trabalha em {Normalizador.NomeUnidade(usuario.Unidade)} e não pode atender na unidade {Normalizador.NomeUnidade(unidade)}.");
        }
        return id;
    }
}
