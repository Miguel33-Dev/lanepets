using LanePets.Services;
using LanePets.Tests.Infra;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LanePets.Tests.Operacao;

/// <summary>
/// 28/09: e-mails para o tutor (AvisosClienteService). Boas-vindas e "recebemos o agendamento" sempre;
/// Confirmado/Cancelado pelo painel e lembrete da vespera respeitam Cliente.ReceberAvisos (§6.43).
/// </summary>
[Collection(ColecaoApi.Nome)]
public class AvisosClienteTests(LanePetsApp app)
{
    private EmailService Email => app.Services.GetRequiredService<EmailService>();

    private int Quantos(string para, string trechoAssunto)
        => Email.Enviados.Count(m => m.Para == para && m.Assunto.Contains(trechoAssunto));

    [Fact]
    public async Task Cadastro_e_agendamento_pelo_site_mandam_boas_vindas_e_comprovante()
    {
        var cliente = app.Api();
        var (conta, petId) = await cliente.ClienteComPet("Tutor Avisos", "Paçoca");
        Assert.Equal(1, Quantos(conta.Email, "Bem-vindo"));

        var (servicoId, _) = await cliente.Servico();
        var r = await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), "09:00");
        Assert.True(r.Codigo == 200, r.ToString());

        var msg = Email.Enviados.Last(m => m.Para == conta.Email);
        Assert.Contains("recebemos o agendamento de Paçoca", msg.Assunto);
        Assert.Contains("Franco da Rocha", msg.Corpo);
    }

    [Fact]
    public async Task Painel_avisa_confirmado_e_cancelado_mas_nao_os_outros_status()
    {
        var cliente = app.Api();
        var (conta, petId) = await cliente.ClienteComPet("Tutor Status", "Farofa");
        var (servicoId, _) = await cliente.Servico();
        var agendamento = (await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), "10:00")).Data;
        var painel = app.Api();
        var token = await painel.LoginAdmin();

        async Task Mudar(string status)
        {
            var s = await painel.Sync(token, "agendamentos", atualizados: [Cenarios.ItemPainel(agendamento, status)]);
            Assert.True(s.Codigo == 200, s.ToString());
        }

        await Mudar("Confirmado");
        Assert.Equal(1, Quantos(conta.Email, "confirmado"));
        await Mudar("Confirmado");                                  // salvar sem mudar o status
        Assert.Equal(1, Quantos(conta.Email, "confirmado"));
        await Mudar("Em andamento");
        await Mudar("Cancelado");
        Assert.Equal(1, Quantos(conta.Email, "cancelado"));
        Assert.Equal(4, Email.Enviados.Count(m => m.Para == conta.Email)); // boas-vindas, recebido, confirmado, cancelado
    }

    [Fact]
    public async Task Cliente_desliga_os_avisos_e_o_painel_nao_manda_mais()
    {
        var cliente = app.Api();
        var (conta, petId) = await cliente.ClienteComPet("Tutor Sem Avisos", "Bento");
        Assert.True((await cliente.Get("/api/cliente/conta")).Data.GetProperty("receberAvisos").GetBoolean());

        var r = await cliente.Put("/api/cliente/conta/avisos", new { receber = false });
        Assert.True(r.Codigo == 200, r.ToString());
        Assert.False(r.Data.GetProperty("receberAvisos").GetBoolean());
        Assert.False((await cliente.Get("/api/cliente/conta")).Data.GetProperty("receberAvisos").GetBoolean());

        var (servicoId, _) = await cliente.Servico();
        var agendamento = (await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), "11:00")).Data;
        Assert.Equal(1, Quantos(conta.Email, "recebemos"));        // comprovante vai sempre
        var painel = app.Api();
        await painel.Sync(await painel.LoginAdmin(), "agendamentos", atualizados: [Cenarios.ItemPainel(agendamento, "Confirmado")]);
        Assert.Equal(0, Quantos(conta.Email, "confirmado"));

        Assert.Equal(401, (await app.Api().Put("/api/cliente/conta/avisos", new { receber = true })).Codigo);
    }

    [Fact]
    public async Task Lembrete_da_vespera_vai_uma_vez_so_para_quem_aceita_e_nao_para_cancelado()
    {
        // Data exclusiva no futuro: a rotina de verdade (que usa o relogio real) nunca pega estes.
        var dia = Cenarios.NovaData();
        var vespera = DateTime.Parse(dia).AddDays(-1);

        var aceita = app.Api();
        var (contaAceita, petAceita) = await aceita.ClienteComPet("Tutor Lembrete", "Tico");
        var (servicoId, _) = await aceita.Servico();
        Assert.True((await aceita.Agendar(petAceita, servicoId, dia, "09:00")).Codigo == 200);
        var cancelado = (await aceita.Agendar(petAceita, servicoId, dia, "15:00")).Data;
        Assert.True((await aceita.Post($"/api/cliente/agendamentos/{cancelado.GetProperty("id").GetString()}/cancelar")).Codigo == 200);

        var recusa = app.Api();
        var (contaRecusa, petRecusa) = await recusa.ClienteComPet("Tutor Recusa", "Teco");
        await recusa.Put("/api/cliente/conta/avisos", new { receber = false });
        Assert.True((await recusa.Agendar(petRecusa, servicoId, dia, "10:00")).Codigo == 200);

        var config = app.Services.GetRequiredService<IConfiguration>();
        Task<int> Rodar(DateTime agora) => app.NoBanco(db => AvisosClienteService.EnviarLembretesAsync(db, Email, config, agora));

        Assert.Equal(0, await Rodar(vespera.AddHours(8)));          // antes das 9h nao manda
        Assert.Equal(1, await Rodar(vespera.AddHours(10)));
        Assert.Equal(0, await Rodar(vespera.AddHours(11)));         // uma vez so
        Assert.Equal(1, Quantos(contaAceita.Email, "lembrete"));
        Assert.Equal(0, Quantos(contaRecusa.Email, "lembrete"));
        Assert.Contains("09:00", Email.Enviados.Last(m => m.Para == contaAceita.Email).Corpo);
    }
}
