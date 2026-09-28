using System.Text.RegularExpressions;
using LanePets.Services;
using LanePets.Tests.Infra;
using Microsoft.Extensions.DependencyInjection;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// 28/09: "Esqueci minha senha" da area do cliente (POST /api/cliente/senha/esqueci e /senha/redefinir).
/// Regras: resposta igual exista ou nao o e-mail (§6.5); codigo de 6 digitos so como hash, 15 minutos,
/// uso unico, 5 tentativas, 3 pedidos por hora; redefinir derruba as sessoes (§6.20) e vira evento sem o codigo (§6.25).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class RecuperacaoSenhaTests(LanePetsApp app)
{
    private EmailService Email => app.Services.GetRequiredService<EmailService>();

    private string UltimoCodigo(string email)
    {
        var msg = Email.Enviados.Last(m => m.Para == email && EhRecuperacao(m));
        return Regex.Match(msg.Corpo, @"\b\d{6}\b").Value;
    }

    // Desde 28/09 o cadastro tambem manda boas-vindas: contar so os e-mails de recuperacao.
    private static bool EhRecuperacao(EmailService.Mensagem m) => m.Assunto.Contains("redefinir sua senha");

    private static Task<Resposta> Pedir(Api api, string email) => api.Post("/api/cliente/senha/esqueci", new { email });

    private static Task<Resposta> Redefinir(Api api, string email, string codigo, string senha = "NovaSenha123")
        => api.Post("/api/cliente/senha/redefinir", new { email, codigo, novaSenha = senha, confirmarSenha = senha });

    [Fact]
    public async Task Codigo_por_email_troca_a_senha_uma_vez_e_derruba_as_sessoes()
    {
        var site = app.Api();
        var conta = await site.CadastrarCliente();
        var anonimo = app.Api();

        var pedido = await Pedir(anonimo, conta.Email);
        Assert.True(pedido.Codigo == 200, pedido.ToString());
        Assert.Equal(RecuperacaoSenhaService.MensagemPedido, pedido.Texto("message"));
        var codigo = UltimoCodigo(conta.Email);
        Assert.Matches(@"^\d{6}$", codigo);

        // O codigo nunca vai para o banco nem para o log.
        var guardado = await app.NoBanco(db => Task.FromResult(db.RecuperacoesSenha.Where(r => r.UsuarioClienteId != "").ToList()));
        Assert.DoesNotContain(guardado, r => r.CodigoHash.Contains(codigo));
        Assert.All(guardado, r => Assert.Equal(64, r.CodigoHash.Length));

        var trocou = await Redefinir(anonimo, conta.Email, codigo);
        Assert.True(trocou.Codigo == 200, trocou.ToString());

        Assert.Equal(401, (await site.Get("/api/cliente/conta")).Codigo);                                   // sessao antiga caiu
        Assert.Equal(200, (await app.Api().Post("/api/cliente/login", new { email = conta.Email, senha = "NovaSenha123" })).Codigo);
        Assert.NotEqual(200, (await app.Api().Post("/api/cliente/login", new { email = conta.Email, senha = conta.Senha })).Codigo);
        Assert.Equal(400, (await Redefinir(anonimo, conta.Email, codigo, "OutraSenha456")).Codigo);         // uso unico

        var eventos = await app.NoBanco(db => Task.FromResult(db.EventosLog.Where(e => e.Autor == conta.Email && e.Categoria == "seguranca").ToList()));
        Assert.Contains(eventos, e => e.Acao == "Senha redefinida pelo código");
        Assert.DoesNotContain(eventos, e => e.Detalhes.Contains(codigo));
    }

    [Fact]
    public async Task Email_sem_conta_recebe_a_mesma_resposta_e_nada_e_enviado()
    {
        var email = Api.NovoEmail("semconta");
        var r = await Pedir(app.Api(), email);

        Assert.True(r.Codigo == 200, r.ToString());
        Assert.Equal(RecuperacaoSenhaService.MensagemPedido, r.Texto("message"));
        Assert.DoesNotContain(Email.Enviados, m => m.Para == email && EhRecuperacao(m));
        Assert.Equal(400, (await Redefinir(app.Api(), email, "123456")).Codigo);
    }

    [Fact]
    public async Task Cinco_tentativas_erradas_matam_o_codigo_e_senha_fraca_nao_gasta_tentativa()
    {
        var conta = await app.Api().CadastrarCliente();
        var api = app.Api();
        await Pedir(api, conta.Email);
        var certo = UltimoCodigo(conta.Email);
        var errado = certo == "000000" ? "111111" : "000000";

        Assert.Equal(400, (await Redefinir(api, conta.Email, certo, "fraca")).Codigo);                     // regra da senha
        var tentativas = await app.NoBanco(db =>
        {
            var usuario = db.UsuariosClientes.Single(u => u.Email == conta.Email);
            return Task.FromResult(db.RecuperacoesSenha.Where(r => r.UsuarioClienteId == usuario.Id).ToList().OrderByDescending(r => r.CriadoEm).First().Tentativas);
        });
        Assert.Equal(0, tentativas);

        for (var i = 0; i < RecuperacaoSenhaService.MaxTentativas; i++)
            Assert.Equal(400, (await Redefinir(api, conta.Email, errado)).Codigo);
        var depois = await Redefinir(api, conta.Email, certo);
        Assert.Equal(400, depois.Codigo);                                                                    // morreu
        Assert.Equal(RecuperacaoSenhaService.CodigoInvalido, depois.Erro);
    }

    [Fact]
    public async Task Codigo_vencido_nao_vale_e_pedidos_por_hora_tem_limite()
    {
        var conta = await app.Api().CadastrarCliente();
        var api = app.Api();
        await Pedir(api, conta.Email);
        var codigo = UltimoCodigo(conta.Email);
        await app.NoBanco(async db =>
        {
            var usuario = db.UsuariosClientes.Single(u => u.Email == conta.Email);
            foreach (var r in db.RecuperacoesSenha.Where(r => r.UsuarioClienteId == usuario.Id)) r.ExpiraEm = DateTime.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync();
        });
        Assert.Equal(400, (await Redefinir(api, conta.Email, codigo)).Codigo);

        // Ja houve 1 pedido nesta hora: mais 2 geram codigo, o 4o nao (mesma resposta).
        for (var i = 0; i < 3; i++) Assert.Equal(200, (await Pedir(api, conta.Email)).Codigo);
        Assert.Equal(RecuperacaoSenhaService.MaxPedidosPorHora, Email.Enviados.Count(m => m.Para == conta.Email && EhRecuperacao(m)));

        // Pedido novo invalida o anterior: so o ultimo codigo enviado vale.
        var ultimo = UltimoCodigo(conta.Email);
        Assert.Equal(200, (await Redefinir(api, conta.Email, ultimo)).Codigo);
    }
}
