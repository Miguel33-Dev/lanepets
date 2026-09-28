using LanePets.Controllers;
using LanePets.Data;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;
[Route("api")]
public class AuthController(SessionService sessions, LanePetsDbContext db, PermissaoService permissoes, EventosService eventos, EmailService email, IConfiguration config, IHostEnvironment ambiente, ILogger<AuthController> log) : ApiControllerBase
{
    [HttpGet("health")]
    public IActionResult Health() => OkApi(new { ok=true, system="Lane Pets", version="CSharp-1.0", timezone="America/Sao_Paulo", unidades=Normalizador.Unidades.Where(u=>u.Ativa).Select(u=>u.Nome), api="ASP.NET Core + SQLite", banco="lanepets.db", escrita=new { habilitada=true, modo="DADOS_REAIS" }, demo=Hospedagem.ModoDemo(config, ambiente), visitante=Visitante.Ligado(config) });   // 29/09: demo -> a tela de login mostra as credenciais de teste

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

            // Item 15: toda recusa de login vira evento (o motivo real fica so no
            // log; a tela continua com a mensagem generica, para nao revelar se
            // o e-mail existe).
            var admin = await db.UsuariosAdministradores.FirstOrDefaultAsync(u => u.Email == email);
            if (admin is null)
            {
                await eventos.RegistrarAsync(new("autenticacao", "Login administrativo recusado", "aviso", "admin", Autor: email, Detalhes: "E-mail não cadastrado."), HttpContext);
                throw new Exception("E-mail ou senha inválidos.");
            }

            if (!SessionService.VerifyPassword(body.Senha, admin.SenhaHash, admin.SenhaSalt))
            {
                await eventos.RegistrarAsync(new("autenticacao", "Login administrativo recusado", "aviso", "admin", admin.Id, admin.Email, Detalhes: "Senha incorreta."), HttpContext);
                throw new Exception("E-mail ou senha inválidos.");
            }

            // Conta desativada nao entra. A mensagem e distinta da de senha
            // errada porque aqui a credencial estava certa — esconder isso so
            // faria o usuario tentar de novo achando que errou a senha.
            if (!admin.Ativo)
            {
                await eventos.RegistrarAsync(new("autenticacao", "Login administrativo recusado", "aviso", "admin", admin.Id, admin.Email, Detalhes: "Conta desativada."), HttpContext);
                throw new UnauthorizedAccessException("Esta conta administrativa está desativada. Procure o Administrador Geral.");
            }

            return await Entrar(admin, "Login administrativo", $"Perfil {PerfilAdmin.Rotulo(admin.Perfil)}.");
        }
        catch(Exception ex){ return ErrorApi(ex); }
    }

    /// <summary>
    /// 29/09: "Entrar como visitante" — so com LanePets:Visitante=true. Sem senha a divulgar: abre a conta
    /// so leitura (Visitante). Mesma sessao e mesmo pacote de permissoes do login normal.
    /// </summary>
    [HttpPost("login/visitante")]
    public async Task<IActionResult> LoginVisitante()
    {
        try
        {
            const string desligado = "O acesso de visitante não está ligado neste LanePets.";
            if (!Visitante.Ligado(config)) throw new Exception(desligado);
            // A chave pode ter sido ligada com o LanePets ja aberto (o appsettings recarrega sozinho, mas a conta
            // so nascia na subida): garante a conta aqui tambem, sempre so leitura.
            var admin = await db.UsuariosAdministradores.FirstOrDefaultAsync(u => u.Id == Visitante.Id && u.Ativo);
            if (admin is null)
            {
                await Visitante.GarantirAsync(db, config, log);
                admin = await db.UsuariosAdministradores.FirstOrDefaultAsync(u => u.Id == Visitante.Id && u.Ativo) ?? throw new Exception(desligado);
            }
            return await Entrar(admin, "Login como visitante", "Acesso só leitura da vitrine.");
        }
        catch(Exception ex){ return ErrorApi(ex); }
    }

    private async Task<IActionResult> Entrar(Models.UsuarioAdministrador admin, string acao, string detalhes)
    {
        admin.UltimoAcesso = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var t = sessions.CreateAdmin(admin.Id);
        await eventos.RegistrarAsync(new("autenticacao", acao, "info", "admin", admin.Id, admin.Email, Detalhes: detalhes), HttpContext);
        Response.Cookies.Append("lanePetsAdmin", t, new CookieOptions { HttpOnly=true, SameSite=SameSiteMode.Lax, IsEssential=true, MaxAge=TimeSpan.FromHours(2) });

        var contexto = await permissoes.ResolverAsync(t);
        return OkApi(new { autenticado=true, perfil="admin", token=t, expiresInMinutes=120, permissoes = PermissaoService.Mapear(contexto) });
    }

    /// <summary>
    /// Perfil e permissoes de quem esta logado. O painel chama isto na abertura
    /// de cada pagina para montar o menu e decidir se a pagina pode abrir.
    /// </summary>
    // ------------------------------------------------------------------ 29/09: esqueci a senha do painel
    // Mesmas regras da area do cliente (RecuperacaoSenhaService): resposta igual exista ou nao a conta,
    // codigo so como hash, 15 min, uso unico, 5 tentativas, 3 pedidos/hora; conta desativada nao recebe.

    [HttpPost("admin/senha/esqueci")]
    public async Task<IActionResult> EsqueciSenha([FromBody] EsqueciBody body)
    {
        try
        {
            var pedido = await RecuperacaoSenhaService.SolicitarAdminAsync(db, body.Email, DateTime.UtcNow);
            if (pedido.Usuario is { } admin)
            {
                if (pedido.Codigo is not null)
                {
                    var enviado = await email.EnviarAsync(admin.Email, "LanePets · código para redefinir sua senha do painel",
                        RecuperacaoSenhaService.TextoEmailAdmin(admin.Nome, pedido.Codigo));
                    // Nunca o codigo no log (§6.25): so o fato e o destino.
                    await eventos.RegistrarAsync(new("seguranca",
                        enviado ? "Recuperação de senha do painel solicitada" : "Falha ao enviar código de recuperação do painel",
                        enviado ? "info" : "erro", "admin", admin.Id, admin.Email, admin.Id,
                        enviado ? $"Código enviado para {admin.Email}." : "Envio de e-mail falhou; ver log do servidor."), HttpContext);
                }
                else
                {
                    await eventos.RegistrarAsync(new("seguranca", "Recuperação de senha do painel recusada", "aviso", "admin",
                        admin.Id, admin.Email, admin.Id, pedido.Motivo), HttpContext);
                }
            }
            return OkApi(new { message = RecuperacaoSenhaService.MensagemPedido, validadeMinutos = RecuperacaoSenhaService.MinutosValidade });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpPost("admin/senha/redefinir")]
    public async Task<IActionResult> RedefinirSenha([FromBody] RedefinirBody body)
    {
        try
        {
            var admin = await RecuperacaoSenhaService.RedefinirAdminAsync(db, body.Email, body.Codigo, body.NovaSenha, body.ConfirmarSenha, DateTime.UtcNow,
                (a, motivo) => eventos.RegistrarAsync(new("seguranca", "Redefinição de senha do painel recusada", "aviso", "admin", a.Id, a.Email, a.Id, motivo), HttpContext));
            // Quem estava no painel com a senha antiga (outro computador) sai, junto com a sessao financeira.
            sessions.EncerrarSessoesDoUsuario(admin.Id);
            await eventos.RegistrarAsync(new("seguranca", "Senha do painel redefinida pelo código", "info", "admin", admin.Id, admin.Email, admin.Id,
                "Sessões anteriores encerradas."), HttpContext);
            return OkApi(new { email = admin.Email, message = "Senha redefinida. Entre com a nova senha." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

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
    public record LoginBody(string? Email, string Senha); public record EsqueciBody(string? Email); public record RedefinirBody(string? Email, string? Codigo, string? NovaSenha, string? ConfirmarSenha); public record TokenBody(string? Token); public record FinanceBody(string Token,string Senha); public record FinanceTokenBody(string? FinanceiroToken);
}
