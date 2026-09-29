using LanePets.Tests.Infra;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// Segurança — Etapa 1 da auditoria (29/09): XSS armazenado.
/// Um visitante conseguia cadastrar um pet chamado <c>&lt;img src=x onerror=...&gt;</c> e o painel
/// mostrava esse nome como HTML. A tela passou a escapar tudo; aqui fica provada a segunda
/// barreira: o servidor recusa &lt; e &gt; em nomes (cliente, pet, raça, endereço),
/// venha do site, da área do cliente ou do painel. Texto comum (acento, apóstrofo, &amp;) continua valendo.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class SemMarcacaoTests(LanePetsApp app)
{
    private const string Html = "<img src=x onerror=alert(1)>";

    private static object Cadastro(string email, string nome = "Tutor Teste", string pet = "Rex", string endereco = "Rua A, 1")
        => new { nome, email, senha = "Senha123", telefone = "(11) 98765-4321", endereco, pet, tipo = "Cachorro", raca = "SRD" };

    [Theory]
    [InlineData("pet")]
    [InlineData("nome")]
    [InlineData("endereco")]
    public async Task Cadastro_com_html_no_nome_e_recusado_e_nada_e_gravado(string campo)
    {
        var api = app.Api();
        var email = Api.NovoEmail("xss");
        var corpo = campo switch
        {
            "pet" => Cadastro(email, pet: Html),
            "nome" => Cadastro(email, nome: "Maria " + Html),
            _ => Cadastro(email, endereco: "Rua " + Html)
        };

        var r = await api.Post("/api/cliente/cadastro", corpo);

        Assert.Equal(400, r.Codigo);
        Assert.Contains("< ou >", r.Erro);
        var existe = await app.NoBanco(db => Task.FromResult(db.UsuariosClientes.Any(u => u.Email == email)));
        Assert.False(existe);
    }

    [Fact]
    public async Task Pet_novo_e_perfil_com_html_sao_recusados()
    {
        var api = app.Api();
        await api.CadastrarCliente();

        var pet = await api.Post("/api/cliente/pets", new { nome = Html, tipo = "Gato" });
        Assert.Equal(400, pet.Codigo);
        Assert.Contains("< ou >", pet.Erro);

        var raca = await api.Post("/api/cliente/pets", new { nome = "Mimi", tipo = "Gato", raca = "<b>Siamês</b>" });
        Assert.Equal(400, raca.Codigo);

        var perfil = await api.Put("/api/cliente/conta", new { nome = "Maria " + Html, telefone = "(11) 98765-4321", endereco = "Rua A" });
        Assert.Equal(400, perfil.Codigo);
        Assert.Contains("< ou >", perfil.Erro);
    }

    [Fact]
    public async Task Pedido_de_seguro_pelo_site_com_html_e_recusado()
    {
        var api = app.Api();
        var planos = await api.Get("/api/public/seguros");
        var planoId = planos.Data.EnumerateArray().First().GetProperty("id").GetString();

        var r = await api.Post("/api/public/seguros/solicitacoes",
            new { planoId, nome = "Tutor Site", telefone = "(11) 98765-4321", pet = Html, observacao = "" });

        Assert.Equal(400, r.Codigo);
        Assert.Contains("< ou >", r.Erro);
    }

    [Fact]
    public async Task Painel_nao_grava_pet_com_html_no_nome()
    {
        var api = app.Api();
        var token = await api.LoginAdmin();
        var pet = new { id = Api.NovoId("PET"), dono = "Dono Painel", pet = Html, tipo = "Gato", raca = "SRD",
            telefone = "11911112222", endereco = "Rua A, 1", unidade = "franco" };

        var r = await api.Sync(token, "pets", criados: [pet]);

        Assert.Equal(400, r.Codigo);
        var gravou = await app.NoBanco(db => Task.FromResult(db.Pets.Any(p => p.Id == pet.id)));
        Assert.False(gravou);
    }

    [Fact]
    public async Task Nome_comum_com_acento_apostrofo_e_e_comercial_continua_valendo()
    {
        var api = app.Api();
        var r = await api.Post("/api/cliente/cadastro",
            Cadastro(Api.NovoEmail("ok"), nome: "João D'Ávila & Filhos", pet: "Pé-de-Pano", endereco: "Rua São João, 12 - apto 3"));

        Assert.True(r.Codigo == 200, r.ToString());
    }
}
