using System.Text.RegularExpressions;
using LanePets.Services;
using LanePets.Tests.Infra;
using Microsoft.Extensions.DependencyInjection;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// 29/09: "Esqueceu a senha?" do acesso administrativo (POST /api/admin/senha/esqueci e /senha/redefinir).
/// Mesmas regras da area do cliente (RecuperacaoSenhaService); conta desativada nao recebe codigo.
/// Nunca usa o admin@gmail.com: trocar a senha dele quebraria os outros testes da colecao.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class RecuperacaoSenhaPainelTests(LanePetsApp app)
{
    private EmailService Email => app.Services.GetRequiredService<EmailService>();

    private string UltimoCodigo(string email, string trechoAssunto)
        => Regex.Match(Email.Enviados.Last(m => m.Para == email && m.Assunto.Contains(trechoAssunto)).Corpo, @"\b\d{6}\b").Value;

    private async Task<string> NovoAdmin(string? email = null)
    {
        var painel = app.Api();
        email ??= Api.NovoEmail("rec.admin");
        var r = await painel.Post("/api/admin/usuarios", new { token = await painel.LoginAdmin(), nome = "Admin Recupera", email, senha = "Senha123", confirmarSenha = "Senha123", perfil = "Admin" });
        Assert.True(r.Codigo == 200, r.ToString());
        return email;
    }

    private static Task<Resposta> Pedir(Api api, string email) => api.Post("/api/admin/senha/esqueci", new { email });

    private static Task<Resposta> Redefinir(Api api, string email, string codigo, string senha)
        => api.Post("/api/admin/senha/redefinir", new { email, codigo, novaSenha = senha, confirmarSenha = senha });

    [Fact]
    public async Task Codigo_troca_a_senha_do_painel_uma_vez_e_derruba_as_sessoes()
    {
        var email = await NovoAdmin();
        var api = app.Api();
        var tokenAntigo = await api.LoginAdmin(email, "Senha123");

        var pedido = await Pedir(app.Api(), email);
        Assert.True(pedido.Codigo == 200, pedido.ToString());
        Assert.Equal(RecuperacaoSenhaService.MensagemPedido, pedido.Texto("message"));
        var codigo = UltimoCodigo(email, "senha do painel");
        Assert.Matches(@"^\d{6}$", codigo);

        Assert.Equal(400, (await Redefinir(api, email, codigo, "fraca")).Codigo);            // senha fraca nao gasta tentativa
        var ok = await Redefinir(api, email, codigo, "NovaSenha123");
        Assert.True(ok.Codigo == 200, ok.ToString());

        Assert.Equal(401, (await api.Get($"/api/auth_status?token={tokenAntigo}")).Codigo);  // sessao antiga caiu
        Assert.Equal(200, (await api.Post("/api/login", new { email, senha = "NovaSenha123" })).Codigo);
        Assert.Equal(400, (await api.Post("/api/login", new { email, senha = "Senha123" })).Codigo);
        var denovo = await Redefinir(api, email, codigo, "OutraSenha9");                        // uso unico
        Assert.Equal(400, denovo.Codigo);
        Assert.Equal(RecuperacaoSenhaService.CodigoInvalido, denovo.Erro);

        var eventos = await app.NoBanco(db => Task.FromResult(db.EventosLog.Where(e => e.Categoria == "seguranca" && e.Autor == email).ToList()));
        Assert.Contains(eventos, e => e.Acao == "Senha do painel redefinida pelo código");
        Assert.DoesNotContain(eventos, e => e.Detalhes.Contains(codigo));                       // codigo nunca no log
    }

    [Fact]
    public async Task Email_inexistente_e_conta_desativada_recebem_a_mesma_resposta_sem_codigo()
    {
        var email = await NovoAdmin();
        await app.NoBanco(async db =>
        {
            db.UsuariosAdministradores.Single(u => u.Email == email).Ativo = false;
            return await db.SaveChangesAsync();
        });
        var inexistente = Api.NovoEmail("ninguem");

        var a = await Pedir(app.Api(), email);
        var b = await Pedir(app.Api(), inexistente);

        Assert.Equal(200, a.Codigo);
        Assert.Equal(a.Texto("message"), b.Texto("message"));
        Assert.DoesNotContain(Email.Enviados, m => m.Para == email || m.Para == inexistente);
    }

    [Fact]
    public async Task Codigo_do_cliente_nao_vale_no_painel_nem_o_contrario()
    {
        var email = Api.NovoEmail("mesmo");
        await NovoAdmin(email);
        var site = app.Api();
        await site.CadastrarCliente(email);

        await site.Post("/api/cliente/senha/esqueci", new { email });
        var doCliente = UltimoCodigo(email, "redefinir sua senha");
        Assert.Equal(400, (await Redefinir(app.Api(), email, doCliente, "NovaSenha123")).Codigo);

        await Pedir(app.Api(), email);
        var doPainel = UltimoCodigo(email, "senha do painel");
        var r = await site.Post("/api/cliente/senha/redefinir", new { email, codigo = doPainel, novaSenha = "NovaSenha123", confirmarSenha = "NovaSenha123" });
        Assert.Equal(400, r.Codigo);

        Assert.Equal(200, (await Redefinir(app.Api(), email, doPainel, "NovaSenha123")).Codigo);
    }
}
