using System.Text.Json;
using System.Text.RegularExpressions;
using LanePets.Services;
using LanePets.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// Tarefa 2, etapa 1 (28/09): o Login com Google nao pode prejudicar nada que ja existia. Conta criada pelo Google
/// passando por TODAS as secoes da area do cliente, conta antiga que vincula o Google, criar senha pelo "Esqueci",
/// isolamento entre contas, e o que o painel ve. Decisao do Fabricio: agendar, pedir e contratar seguro exigem telefone.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class LoginGoogleRegressaoTests(LanePetsApp app)
{
    private async Task<(Api Api, string Email, string Sub)> ContaGoogle(string nome = "Tutor Google")
    {
        var api = GoogleFalso.Api(app);
        var (email, sub) = (Api.NovoEmail("google"), GoogleFalso.NovoSub());
        var r = await api.Post("/api/cliente/google", new { credential = GoogleFalso.Token(email, sub, nome) });
        Assert.True(r.Codigo == 200, r.ToString());
        api.TokenCliente = r.Texto("token");
        return (api, email, sub);
    }

    private static async Task<string> NovoPet(Api api, string nome = "Thor")
    {
        var r = await api.Post("/api/cliente/pets", new { nome, tipo = "Cachorro", raca = "SRD", porte = "Médio" });
        Assert.True(r.Codigo == 200, r.ToString());
        return r.Texto("id");
    }

    private static Task<Resposta> InformarTelefone(Api api, string nome = "Tutor Google")
        => api.Put("/api/cliente/conta", new { nome, telefone = "(11) 91234-5678", endereco = "Rua do Google, 10" });

    private static async Task<string> Plano(Api api)
        => (await api.Get("/api/public/seguros")).Data.EnumerateArray().First().GetProperty("id").GetString()!;

    [Fact]
    public async Task Sem_telefone_a_conta_do_google_nao_agenda_nem_pede_nem_contrata()
    {
        var (api, _, _) = await ContaGoogle();
        var petId = await NovoPet(api);                                   // pet pode ser cadastrado antes do telefone
        var (servicoId, _) = await api.Servico();
        var painel = GoogleFalso.Api(app);
        var produtoId = await painel.ProdutoNaLoja(await painel.LoginAdmin());

        var agendar = await api.Agendar(petId, servicoId, Cenarios.NovaData(), "10:00");
        var pedir = await api.Pedir(produtoId, 1);
        var seguro = await api.Post("/api/cliente/seguros", new { planoId = await Plano(api), petId, metodoPagamento = "PIX" });

        Assert.Equal(400, agendar.Codigo);
        Assert.Equal("Informe seu telefone em Minha Conta para agendar.", agendar.Erro);
        Assert.Equal(400, pedir.Codigo);
        Assert.Contains("telefone", pedir.Erro);
        Assert.Equal(400, seguro.Codigo);
        Assert.Contains("telefone", seguro.Erro);
        var conta = await api.Get("/api/cliente/conta");
        Assert.Equal(0, conta.Data.GetProperty("agendamentos").GetArrayLength());
        Assert.Equal(0, conta.Data.GetProperty("pedidos").GetArrayLength());
    }

    [Fact]
    public async Task Conta_do_google_completa_o_cadastro_e_usa_todas_as_secoes()
    {
        var (api, email, _) = await ContaGoogle("Bruna Google");
        Assert.Equal(200, (await InformarTelefone(api, "Bruna Google")).Codigo);
        var petId = await NovoPet(api, "Mel");

        // Pets: o pet criado antes/depois do telefone fica com o contato da conta.
        var pet = await api.Get($"/api/cliente/pets/{petId}");
        Assert.True(pet.Codigo == 200, pet.ToString());

        // Agendamento
        var (servicoId, _) = await api.Servico();
        var agendar = await api.Agendar(petId, servicoId, Cenarios.NovaData(), "11:00");
        Assert.True(agendar.Codigo == 200, agendar.ToString());

        // Pedido na loja
        var painel = GoogleFalso.Api(app);
        var produtoId = await painel.ProdutoNaLoja(await painel.LoginAdmin());
        var pedir = await api.Pedir(produtoId, 1);
        Assert.True(pedir.Codigo == 200, pedir.ToString());

        // Seguro Pet
        var seguro = await api.Post("/api/cliente/seguros", new { planoId = await Plano(api), petId, metodoPagamento = "PIX" });
        Assert.True(seguro.Codigo == 200, seguro.ToString());
        var seguros = await api.Get("/api/cliente/seguros");
        Assert.Equal(200, seguros.Codigo);

        // Avaliacao
        var avaliar = await api.Post("/api/cliente/avaliacoes", new { petId, avaliacao = 5, comentario = "Atendimento ótimo " + Guid.NewGuid().ToString("N") });
        Assert.True(avaliar.Codigo == 200, avaliar.ToString());

        // Minha Conta mostra tudo, com o cartao fidelidade
        var conta = await api.Get("/api/cliente/conta");
        Assert.Equal(200, conta.Codigo);
        Assert.Equal(email, conta.Texto("email"));
        Assert.Equal(1, conta.Data.GetProperty("pets").GetArrayLength());
        Assert.Equal(1, conta.Data.GetProperty("agendamentos").GetArrayLength());
        Assert.Equal(1, conta.Data.GetProperty("pedidos").GetArrayLength());
        Assert.True(conta.Data.GetProperty("pagamentos").GetArrayLength() >= 2);   // agendamento + pedido (+ seguro)
        Assert.Equal(JsonValueKind.Object, conta.Data.GetProperty("fidelidade").ValueKind);
        Assert.Equal("(11) 91234-5678", conta.Texto("telefone"));

        // Proxima entrada: cadastro completo
        var deNovo = await GoogleFalso.Api(app).Post("/api/cliente/google", new { credential = GoogleFalso.Token(email, (await app.NoBanco(db => db.UsuariosClientes.SingleAsync(u => u.Email == email))).GoogleSub) });
        Assert.False(deNovo.Data.GetProperty("completarCadastro").GetBoolean());
    }

    [Fact]
    public async Task Conta_do_google_cria_senha_pelo_esqueci_e_passa_a_entrar_dos_dois_jeitos()
    {
        var (api, email, sub) = await ContaGoogle();
        var anonimo = GoogleFalso.Api(app);

        Assert.Equal(200, (await anonimo.Post("/api/cliente/senha/esqueci", new { email })).Codigo);
        var mensagem = GoogleFalso.Servicos(app).GetRequiredService<EmailService>().Enviados
            .Last(m => m.Para == email && m.Assunto.Contains("redefinir sua senha"));
        var codigo = Regex.Match(mensagem.Corpo, @"\b\d{6}\b").Value;
        var r = await anonimo.Post("/api/cliente/senha/redefinir", new { email, codigo, novaSenha = "SenhaNova123", confirmarSenha = "SenhaNova123" });
        Assert.True(r.Codigo == 200, r.ToString());

        Assert.Equal(401, (await api.Get("/api/cliente/conta")).Codigo);   // trocar a senha derruba as sessoes (§6.20)

        var porSenha = await anonimo.Post("/api/cliente/login", new { email, senha = "SenhaNova123" });
        Assert.True(porSenha.Codigo == 200, porSenha.ToString());
        var porGoogle = await anonimo.Post("/api/cliente/google", new { credential = GoogleFalso.Token(email, sub) });
        Assert.True(porGoogle.Data.TryGetProperty("token", out _), porGoogle.ToString());

        anonimo.TokenCliente = porSenha.Texto("token");
        var conta = await anonimo.Get("/api/cliente/conta");
        Assert.True(conta.Data.GetProperty("temSenha").GetBoolean());
        Assert.True(conta.Data.GetProperty("googleVinculado").GetBoolean());
    }

    [Fact]
    public async Task Conta_antiga_que_vincula_o_google_mantem_pets_agendamentos_e_a_senha()
    {
        var api = GoogleFalso.Api(app);
        var (conta, petId) = await api.ClienteComPet(nome: "Diego Antigo", pet: "Faísca");
        var (servicoId, _) = await api.Servico();
        Assert.Equal(200, (await api.Agendar(petId, servicoId, Cenarios.NovaData(), "13:00")).Codigo);
        var idAntes = (await api.Get("/api/cliente/conta")).Texto("id");

        var google = GoogleFalso.Api(app);
        var credential = GoogleFalso.Token(conta.Email, GoogleFalso.NovoSub(), "Diego do Google");
        Assert.True((await google.Post("/api/cliente/google", new { credential })).Data.GetProperty("vincular").GetBoolean());
        var vinculou = await google.Post("/api/cliente/google", new { credential, senha = conta.Senha });
        Assert.True(vinculou.Codigo == 200, vinculou.ToString());

        google.TokenCliente = vinculou.Texto("token");
        var depois = await google.Get("/api/cliente/conta");
        Assert.Equal(idAntes, depois.Texto("id"));
        Assert.Equal("Diego Antigo", depois.Texto("nome"));   // o nome do Google nao sobrescreve o cadastro
        Assert.Equal(1, depois.Data.GetProperty("pets").GetArrayLength());
        Assert.Equal(1, depois.Data.GetProperty("agendamentos").GetArrayLength());
        Assert.Equal(200, (await api.Post("/api/cliente/login", new { email = conta.Email, senha = conta.Senha })).Codigo);
    }

    [Fact]
    public async Task Conta_do_google_so_ve_o_que_e_dela_e_nao_entra_no_painel()
    {
        var alheio = GoogleFalso.Api(app);
        var (_, petAlheio) = await alheio.ClienteComPet(nome: "Outra Pessoa", pet: "Nina");
        var (api, _, _) = await ContaGoogle();
        await InformarTelefone(api);

        Assert.Equal(400, (await api.Get($"/api/cliente/pets/{petAlheio}")).Codigo);
        var (servicoId, _) = await api.Servico();
        Assert.Equal(400, (await api.Agendar(petAlheio, servicoId, Cenarios.NovaData(), "14:00")).Codigo);

        var token = api.TokenCliente!;
        var painel = await api.Get($"/api/admin/resumo?token={token}");
        Assert.True(painel.Codigo is 401 or 403, painel.ToString());
    }

    [Fact]
    public async Task Painel_ve_o_cliente_criado_pelo_google_e_os_eventos()
    {
        var (_, email, _) = await ContaGoogle("Eva Google");
        var clienteId = await app.NoBanco(db => db.UsuariosClientes.Where(u => u.Email == email).Select(u => u.ClienteId).SingleAsync());

        var painel = GoogleFalso.Api(app);
        var lista = await painel.Get($"/api/admin/clientes?token={await painel.LoginAdmin()}&busca={Uri.EscapeDataString(email)}");
        Assert.Equal(200, lista.Codigo);
        var cliente = lista.Data.GetProperty("itens").EnumerateArray().Single(c => c.GetProperty("id").GetString() == clienteId);
        Assert.Equal("Eva Google", cliente.GetProperty("nome").GetString());
        Assert.Equal("google", cliente.GetProperty("origem").GetString());
        Assert.True(cliente.GetProperty("temConta").GetBoolean());
        Assert.Equal(0, cliente.GetProperty("qtdPets").GetInt32());

        var acoes = await app.NoBanco(db => db.EventosLog.Where(e => e.Autor == email).Select(e => e.Acao).ToListAsync());
        Assert.Contains("Conta de cliente criada", acoes);
        Assert.Contains("Login de cliente", acoes);
    }
}
