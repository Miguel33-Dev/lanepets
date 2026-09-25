using LanePets.Controllers;
using LanePets.Data;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;
[Route("api")]
public class AuthController(SessionService sessions, LanePetsDbContext db, PermissaoService permissoes) : ApiControllerBase
{
    [HttpGet("health")]
    public IActionResult Health() => OkApi(new { ok=true, system="Lane Pets", version="CSharp-1.0", timezone="America/Sao_Paulo", unidades=new[]{"Franco","Caieiras"}, api="ASP.NET Core + SQLite", banco="lanepets.db", escrita=new { habilitada=true, modo="DADOS_REAIS" } });

    // -----------------------------------------------------------------------
    // LOGIN ADMINISTRATIVO — UM SO, o que ja existia.
    //
    //     e-mail + senha -> usuario ativo? -> senha confere? -> sessao
    //                                                             |
    //                                            perfil e permissoes do banco
    //
    // Administrador Geral e administrador comum entram pela MESMA porta. O que
    // muda e o que cada um encontra depois dela. Nao existe tela de login
    // separada por perfil.
    // -----------------------------------------------------------------------
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginBody body)
    {
        try
        {
            var email = (body.Email ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(body.Senha))
                throw new Exception("Informe e-mail e senha.");

            var admin = await db.UsuariosAdministradores.FirstOrDefaultAsync(u => u.Email == email)
                ?? throw new Exception("E-mail ou senha inválidos.");

            if (!SessionService.VerifyPassword(body.Senha, admin.SenhaHash, admin.SenhaSalt))
                throw new Exception("E-mail ou senha inválidos.");

            // Conta desativada nao entra. A mensagem e distinta da de senha
            // errada porque aqui a credencial estava certa — esconder isso so
            // faria o usuario tentar de novo achando que errou a senha.
            if (!admin.Ativo)
                throw new UnauthorizedAccessException("Esta conta administrativa está desativada. Procure o Administrador Geral.");

            admin.UltimoAcesso = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var t = sessions.CreateAdmin(admin.Id);
            Response.Cookies.Append("lanePetsAdmin", t, new CookieOptions { HttpOnly=true, SameSite=SameSiteMode.Lax, IsEssential=true, MaxAge=TimeSpan.FromHours(2) });

            var contexto = await permissoes.ResolverAsync(t);
            return OkApi(new { autenticado=true, perfil="admin", token=t, expiresInMinutes=120, permissoes = PermissaoService.Mapear(contexto) });
        }
        catch(Exception ex){ return ErrorApi(ex); }
    }

    /// <summary>
    /// Perfil e permissoes de quem esta logado. O painel chama isto na abertura
    /// de cada pagina para montar o menu e decidir se a pagina pode abrir.
    /// </summary>
    [HttpGet("admin/me")]
    public async Task<IActionResult> Eu([FromQuery] string token = "")
    {
        try { return OkApi(PermissaoService.Mapear(await permissoes.ResolverAsync(token))); }
        catch(Exception ex){ return ErrorApi(ex); }
    }

    [HttpGet("auth_status")]
    public IActionResult Status([FromQuery] string token) { try { var s=sessions.RequireAdmin(token); return OkApi(new { autenticado=true, perfil=s.Profile, criadoEm=s.CreatedAt.ToString("O") }); } catch(Exception ex){return ErrorApi(ex);} }
    [HttpPost("logout")]
    public IActionResult Logout([FromBody] TokenBody body) { sessions.Logout(body.Token ?? ""); Response.Cookies.Delete("lanePetsAdmin"); return OkApi(new { encerrado=!string.IsNullOrWhiteSpace(body.Token) }); }
    [HttpPost("financeiro_login")]
    public async Task<IActionResult> FinanceLogin([FromBody] FinanceBody body) { try { await permissoes.ExigirAsync(body.Token??"", ModulosAdmin.Pagamentos, AcaoPermissao.Visualizar); if(!sessions.ValidateFinance(body.Senha)) throw new Exception("Senha financeira inválida."); var t=sessions.CreateFinancial(body.Token!); return OkApi(new { autorizado=true, perfil="financeiro", token=t, expiresInMinutes=30 }); } catch(Exception ex){return ErrorApi(ex);} }
    [HttpGet("financeiro_status")]
    public IActionResult FinanceStatus([FromQuery(Name="financeiro_token")] string token) { try { var s=sessions.RequireFinancial(token); return OkApi(new { autorizado=true, perfil="financeiro", criadoEm=s.CreatedAt.ToString("O"), expiresInMinutes=sessions.RemainingMinutes(s) }); } catch(Exception ex){return ErrorApi(ex);} }
    [HttpPost("financeiro_logout")]
    public IActionResult FinanceLogout([FromBody] FinanceTokenBody body) { sessions.LogoutFinancial(body.FinanceiroToken??""); return OkApi(new { encerrado=!string.IsNullOrWhiteSpace(body.FinanceiroToken) }); }
    public record LoginBody(string? Email, string Senha); public record TokenBody(string? Token); public record FinanceBody(string Token,string Senha); public record FinanceTokenBody(string? FinanceiroToken);
}
