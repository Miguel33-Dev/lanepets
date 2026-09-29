using LanePets.Controllers;
using LanePets.Data;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;
[Route("api")]
public class AuthController(SessionService sessions, LanePetsDbContext db, PermissaoService permissoes, EventosService eventos, EmailService email, IConfiguration config, IHostEnvironment ambiente, ILogger<AuthController> log, TentativasLogin tentativas) : ApiControllerBase
{
    /// <summary>Seguranca (29/09): IP de quem chamou (ja corrigido pelo proxy) para a trava de tentativas.</summary>
    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Senha errada: conta para a trava e, se acabou de bloquear, vira evento no Log.</summary>
    private async Task Errou(string conta, string autor)
    {
        if (tentativas.Falhou(conta, Ip()))
            await eventos.RegistrarAsync(new("seguranca", "Acesso bloqueado por excesso de tentativas", "aviso", "admin", Autor: autor,
                Detalhes: "Senhas erradas demais seguidas; novas tentativas ficam bloqueadas por alguns minutos."), HttpContext);
    }

    [HttpGet("health")]
    // Etapa 3 (29/09): so o que as telas e o Railway precisam. Antes saiam versao, banco, API e unidades — informacao
    // de graca para quem procura brecha. demo -> modo demonstracao; google -> login Google.
    public IActionResult Health() => OkApi(new { ok=true, demo=Hospedagem.ModoDemo(config, ambiente), google=GoogleLogin.Ativo(config) });

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
    [HttpPost("login")] [EnableRateLimiting("sensivel")]
    public async Task<IActionResult> Login([FromBody] LoginBody body)
    {
        try
        {
            var email = (body.Email ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(body.Senha))
                throw new Exception("Informe e-mail e senha.");
            tentativas.Conferir("admin:" + email, Ip());   // Etapa 2: bloqueado = 429, mesmo com a senha certa

            // Item 15: toda recusa de login vira evento (o motivo real fica so no
            // log; a tela continua com a mensagem generica, para nao revelar se
            // o e-mail existe).
            var admin = await db.UsuariosAdministradores.FirstOrDefaultAsync(u => u.Email == email);
            if (admin is null)
            {
                await eventos.RegistrarAsync(new("autenticacao", "Login administrativo recusado", "aviso", "admin", Autor: email, Detalhes: "E-mail não cadastrado."), HttpContext);
                await Errou("admin:" + email, email);
                throw new Exception("E-mail ou senha inválidos.");
            }

            if (!SessionService.VerifyPassword(body.Senha, admin.SenhaHash, admin.SenhaSalt))
            {
                await eventos.RegistrarAsync(new("autenticacao", "Login administrativo recusado", "aviso", "admin", admin.Id, admin.Email, Detalhes: "Senha incorreta."), HttpContext);
                await Errou("admin:" + email, email);
                throw new Exception("E-mail ou senha inválidos.");
            }
            tentativas.Acertou("admin:" + email);

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

    // 30/09: o acesso de visitante (POST /api/login/visitante) foi removido a pedido do Fabricio — ninguem de fora
    // da equipe entra no painel, nem so para olhar. A conta antiga e desativada na subida (Visitante.DesativarAsync).

    private async Task<IActionResult> Entrar(Models.UsuarioAdministrador admin, string acao, string detalhes)
    {
        admin.UltimoAcesso = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var t = sessions.CreateAdmin(admin.Id);
        await eventos.RegistrarAsync(new("autenticacao", acao, "info", "admin", admin.Id, admin.Email, Detalhes: detalhes), HttpContext);
        // Etapa 3 (29/09): Secure quando a requisicao chegou por HTTPS (no Railway, via X-Forwarded-Proto) — o cookie
        // nunca viaja em HTTP aberto. Local em http://localhost continua funcionando.
        Response.Cookies.Append("lanePetsAdmin", t, new CookieOptions { HttpOnly=true, Secure=Request.IsHttps, SameSite=SameSiteMode.Lax, IsEssential=true, MaxAge=TimeSpan.FromHours(2) });

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

    [HttpPost("admin/senha/esqueci")] [EnableRateLimiting("sensivel")]
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

    [HttpPost("admin/senha/redefinir")] [EnableRateLimiting("sensivel")]
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
    public IActionResult Status([FromQuery] string? token = null) { try { var s=sessions.RequireAdmin(permissoes.Token(token)); return OkApi(new { autenticado=true, perfil=s.Profile, criadoEm=s.CreatedAt.ToString("O") }); } catch(Exception ex){return ErrorApi(ex);} }
    [HttpPost("logout")]
    public IActionResult Logout([FromBody] TokenBody body) { var tk = permissoes.Token(body.Token); sessions.Logout(tk); Response.Cookies.Delete("lanePetsAdmin", new CookieOptions { HttpOnly=true, Secure=Request.IsHttps, SameSite=SameSiteMode.Lax }); return OkApi(new { encerrado=!string.IsNullOrWhiteSpace(tk) }); }
    [HttpPost("financeiro_login")] [EnableRateLimiting("sensivel")]
    public async Task<IActionResult> FinanceLogin([FromBody] FinanceBody body) { try { var tk = permissoes.Token(body.Token); await permissoes.ExigirAsync(tk, ModulosAdmin.Pagamentos, AcaoPermissao.Visualizar); var contaFin = "financeiro:" + sessions.RequireAdmin(tk).UsuarioId; tentativas.Conferir(contaFin, Ip()); if(!sessions.ValidateFinance(body.Senha)) { await Errou(contaFin, "senha financeira"); throw new Exception("Senha financeira inválida."); } tentativas.Acertou(contaFin); var t=sessions.CreateFinancial(tk); return OkApi(new { autorizado=true, perfil="financeiro", token=t, expiresInMinutes=30 }); } catch(Exception ex){return ErrorApi(ex);} }
    [HttpGet("financeiro_status")]
    public IActionResult FinanceStatus([FromQuery(Name="financeiro_token")] string? token = null) { try { var s=sessions.RequireFinancial(TokenPainel.Escolher(HttpContext, token, TokenPainel.CabecalhoFinanceiro)); return OkApi(new { autorizado=true, perfil="financeiro", criadoEm=s.CreatedAt.ToString("O"), expiresInMinutes=sessions.RemainingMinutes(s) }); } catch(Exception ex){return ErrorApi(ex);} }
    [HttpPost("financeiro_logout")]
    public IActionResult FinanceLogout([FromBody] FinanceTokenBody body) { var tk = TokenPainel.Escolher(HttpContext, body.FinanceiroToken, TokenPainel.CabecalhoFinanceiro); sessions.LogoutFinancial(tk); return OkApi(new { encerrado=!string.IsNullOrWhiteSpace(tk) }); }
    public record LoginBody(string? Email, string Senha); public record EsqueciBody(string? Email); public record RedefinirBody(string? Email, string? Codigo, string? NovaSenha, string? ConfirmarSenha); public record TokenBody(string? Token); public record FinanceBody(string? Token,string Senha); public record FinanceTokenBody(string? FinanceiroToken);
}
