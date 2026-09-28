using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// 29/09: acesso de VISITANTE ao painel (vitrine/portfolio). Ligado com <c>LanePets:Visitante=true</c>.
///
/// - Conta propria (<see cref="Id"/>), perfil Admin comum, sem acesso total e SO com "visualizar" nos modulos
///   do petshop (Usuarios, Log, Integridade e Configuracoes ficam fechados). Toda gravacao continua barrada
///   pelo backend (403), como para qualquer usuario sem permissao.
/// - Entra pelo botao "Entrar como visitante" (POST /api/login/visitante) — sem senha para divulgar; a senha
///   da conta e aleatoria e ninguem a conhece.
/// - A cada subida as permissoes voltam a ser so leitura (se alguem mexeu, desfaz). Desligado, a conta e desativada.
/// Atencao: o visitante ve os dados do painel (clientes, telefones). Ligue so numa vitrine com dados de exemplo.
/// </summary>
public static class Visitante
{
    public const string Id = "ADM-VISITANTE";
    public const string Email = "visitante@lanepets.demo";
    public const string Nome = "Visitante (só leitura)";

    private static readonly HashSet<string> Fechados = new(StringComparer.OrdinalIgnoreCase) { ModulosAdmin.Configuracoes, ModulosAdmin.Usuarios };

    public static bool Ligado(IConfiguration config) => string.Equals(config["LanePets:Visitante"], "true", StringComparison.OrdinalIgnoreCase);

    public static bool Eh(UsuarioAdministrador? u) => u?.Id == Id;

    public static async Task GarantirAsync(LanePetsDbContext db, IConfiguration config, ILogger log)
    {
        var conta = await db.UsuariosAdministradores.FirstOrDefaultAsync(u => u.Id == Id);
        if (!Ligado(config))
        {
            if (conta is { Ativo: true })
            {
                conta.Ativo = false;
                await db.SaveChangesAsync();
                log.LogInformation("LanePets: acesso de visitante desligado (conta desativada).");
            }
            return;
        }

        if (conta is null)
        {
            var (hash, salt) = SessionService.HashPassword(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
            conta = new UsuarioAdministrador { Id = Id, Email = Email, Nome = Nome, SenhaHash = hash, SenhaSalt = salt };
            db.UsuariosAdministradores.Add(conta);
        }
        conta.Ativo = true;
        conta.Perfil = PerfilAdmin.Comum;
        conta.AcessoTotal = false;
        conta.Unidade = "";

        db.UsuariosAdminPermissoes.RemoveRange(await db.UsuariosAdminPermissoes.Where(p => p.UsuarioAdminId == Id).ToListAsync());
        foreach (var m in ModulosAdmin.Todos.Where(m => !m.SomenteGeral && !Fechados.Contains(m.Chave)))
            db.UsuariosAdminPermissoes.Add(new UsuarioAdminPermissao { Id = Guid.NewGuid().ToString("N"), UsuarioAdminId = Id, Modulo = m.Chave, PodeVisualizar = true });
        await db.SaveChangesAsync();
        log.LogInformation("LanePets: acesso de visitante ligado ({Email}, só leitura).", Email);
    }
}
