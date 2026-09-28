using LanePets.Models;
using LanePets.Services;
using LanePets.Tests.Infra;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// Tarefa 1, etapa 3: primeiro acesso e vinculacao da conta Google (ContaGoogleService). A identidade chega aqui
/// ja conferida (etapa 2), entao os testes montam a GoogleIdentidade direto.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class ContaGoogleTests(LanePetsApp app)
{
    private readonly List<ContaClienteService.Recusa> recusas = [];
    private Task Anotar(ContaClienteService.Recusa r) { recusas.Add(r); return Task.CompletedTask; }

    private static GoogleIdentidade Google(string? email = null, string nome = "Tutor Google", string? sub = null)
        => new(sub ?? Guid.NewGuid().ToString("N"), (email ?? Api.NovoEmail("google")).ToLowerInvariant(), nome, "https://lh3.googleusercontent.com/a/foto");

    private Task<ContaGoogleService.Resultado> Entrar(GoogleIdentidade g)
        => app.NoBanco(db => ContaGoogleService.EntrarAsync(db, g, Anotar));

    private Task<ContaGoogleService.Resultado> Vincular(GoogleIdentidade g, string? senha)
        => app.NoBanco(db => ContaGoogleService.VincularAsync(db, g, senha, Anotar));

    private Task<UsuarioCliente> Usuario(string email)
        => app.NoBanco(db => db.UsuariosClientes.AsNoTracking().SingleAsync(u => u.Email == email));

    [Fact]
    public async Task Cliente_novo_ganha_conta_sem_senha_e_sem_pet()
    {
        var g = Google();
        var r = await Entrar(g);

        Assert.Equal(ContaGoogleService.Situacao.ContaCriada, r.Situacao);
        Assert.Equal("Tutor Google", r.Cliente!.Nome);
        Assert.Equal("google", r.Cliente.Origem);
        var u = await Usuario(g.Email);
        Assert.Equal(g.Sub, u.GoogleSub);
        Assert.Equal("", u.SenhaHash);
        Assert.Equal(0, await app.NoBanco(db => db.Pets.CountAsync(p => p.ClienteId == u.ClienteId)));
    }

    [Fact]
    public async Task Segunda_vez_entra_na_mesma_conta_sem_criar_outra()
    {
        var g = Google();
        var primeira = await Entrar(g);
        var segunda = await Entrar(g);

        Assert.Equal(ContaGoogleService.Situacao.Entrou, segunda.Situacao);
        Assert.Equal(primeira.Cliente!.Id, segunda.Cliente!.Id);
        Assert.Equal(1, await app.NoBanco(db => db.UsuariosClientes.CountAsync(u => u.GoogleSub == g.Sub)));
    }

    [Fact]
    public async Task Trocou_o_email_no_google_continua_na_mesma_conta()
    {
        var g = Google();
        var criada = await Entrar(g);
        var r = await Entrar(g with { Email = Api.NovoEmail("novo-email") });

        Assert.Equal(ContaGoogleService.Situacao.Entrou, r.Situacao);
        Assert.Equal(criada.Cliente!.Id, r.Cliente!.Id);
        Assert.Equal(g.Email, r.Email);   // e-mail de acesso continua o de sempre
    }

    [Fact]
    public async Task Email_com_conta_de_senha_pede_vinculacao_e_nao_grava_nada()
    {
        var conta = await app.Api().CadastrarCliente();
        var r = await Entrar(Google(conta.Email));

        Assert.Equal(ContaGoogleService.Situacao.PrecisaVincular, r.Situacao);
        Assert.Null(r.Cliente);
        Assert.Equal("", (await Usuario(conta.Email)).GoogleSub);
        Assert.Equal(1, await app.NoBanco(db => db.UsuariosClientes.CountAsync(u => u.Email == conta.Email)));
    }

    [Fact]
    public async Task Vincular_com_a_senha_certa_liga_o_google_e_a_senha_continua_valendo()
    {
        var api = app.Api();
        var conta = await api.CadastrarCliente();
        var g = Google(conta.Email);

        var r = await Vincular(g, conta.Senha);
        Assert.Equal(ContaGoogleService.Situacao.Entrou, r.Situacao);
        Assert.Equal(g.Sub, (await Usuario(conta.Email)).GoogleSub);

        Assert.Equal(ContaGoogleService.Situacao.Entrou, (await Entrar(g)).Situacao);   // proxima vez: direto
        Assert.Equal(200, (await api.Post("/api/cliente/login", new { email = conta.Email, senha = conta.Senha })).Codigo);
    }

    [Fact]
    public async Task Vincular_com_senha_errada_e_recusado_e_vira_evento()
    {
        var conta = await app.Api().CadastrarCliente();
        var ex = await Assert.ThrowsAsync<Exception>(() => Vincular(Google(conta.Email), "SenhaErrada9"));

        Assert.Contains("Senha incorreta", ex.Message);
        Assert.Equal("", (await Usuario(conta.Email)).GoogleSub);
        Assert.Contains(recusas, x => x.Detalhes == "Senha incorreta." && x.Autor == conta.Email);
    }

    [Fact]
    public async Task Email_ja_vinculado_a_outra_conta_google_e_recusado()
    {
        var g = Google();
        await Entrar(g);
        var outraConta = Google(g.Email);   // mesmo e-mail, "sub" diferente

        var ex = await Assert.ThrowsAsync<Exception>(() => Entrar(outraConta));
        Assert.Equal(ContaGoogleService.MensagemRecusa, ex.Message);
        Assert.Contains(recusas, x => x.Detalhes.Contains("OUTRA conta Google"));
        await Assert.ThrowsAsync<Exception>(() => Vincular(outraConta, "Qualquer123"));
    }

    [Fact]
    public async Task Mesma_conta_google_nao_vincula_em_segundo_cadastro()
    {
        var g = Google();
        await Entrar(g);
        var conta = await app.Api().CadastrarCliente();

        await Assert.ThrowsAsync<Exception>(() => Vincular(g with { Email = conta.Email }, conta.Senha));
        Assert.Equal("", (await Usuario(conta.Email)).GoogleSub);
    }

    [Fact]
    public async Task Conta_so_do_google_nao_entra_por_senha()
    {
        var g = Google();
        await Entrar(g);
        foreach (var senha in new[] { "", "Senha123" })
        {
            var r = await app.Api().Post("/api/cliente/login", new { email = g.Email, senha });
            Assert.Equal(400, r.Codigo);
            Assert.Equal("E-mail ou senha inválidos.", r.Erro);
        }
    }

    [Theory]
    [InlineData("  Maria   da  Silva ", "maria@exemplo.com", "Maria da Silva")]
    [InlineData("", "joao.pereira@exemplo.com", "joao.pereira")]
    [InlineData("Al", "jo@exemplo.com", "Cliente")]
    public void Nome_vem_do_google_ou_do_email(string nome, string email, string esperado)
        => Assert.Equal(esperado, ContaGoogleService.NomeDoGoogle(new GoogleIdentidade("sub", email, nome, "")));
}
