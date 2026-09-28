using LanePets.Services;
using LanePets.Tests.Infra;
using Microsoft.Extensions.DependencyInjection;

namespace LanePets.Tests.Operacao;

/// <summary>
/// 29/09: confirmacao automatica por unidade. Ligada, o agendamento feito pelo site ja nasce Confirmado
/// (a capacidade continua valendo) e o comprovante por e-mail diz "confirmado"; desligada (padrao), nada muda.
/// Usa uma unidade criada so para o teste (e desativada no fim) para nao mexer em Franco/Caieiras.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class ConfirmacaoAutomaticaTests(LanePetsApp app)
{
    private EmailService Email => app.Services.GetRequiredService<EmailService>();

    [Fact]
    public async Task Unidade_com_confirmacao_automatica_ja_nasce_confirmado_e_respeita_a_capacidade()
    {
        var painel = app.Api();
        var token = await painel.LoginAdmin();
        var nome = "Auto " + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var criada = await painel.Post("/api/admin/unidades", new { token, nome, capacidade = 1, confirmacaoAutomatica = true });
        Assert.True(criada.Codigo == 200, criada.ToString());
        var unidadeId = criada.Texto("id");
        try
        {
            // PUT sem o campo (tela antiga, script) mantem a confirmacao ligada.
            Assert.Equal(200, (await painel.Put($"/api/admin/unidades/{unidadeId}", new { token, nome, capacidade = 1 })).Codigo);
            var lista = await painel.Get($"/api/admin/unidades?token={token}");
            var registro = lista.Data.GetProperty("unidades").EnumerateArray().Single(u => u.GetProperty("id").GetString() == unidadeId);
            Assert.True(registro.GetProperty("confirmacaoAutomatica").GetBoolean());

            var cliente = app.Api();
            var (conta, petId) = await cliente.ClienteComPet("Tutor Automatico", "Nina");
            var catalogo = await cliente.Get("/api/cliente/catalogo");
            Assert.Contains(catalogo.Data.GetProperty("unidades").EnumerateArray(),
                u => u.GetProperty("id").GetString() == unidadeId && u.GetProperty("confirmacaoAutomatica").GetBoolean());

            var (servicoId, _) = await cliente.Servico();
            var data = Cenarios.NovaData();
            var r = await cliente.Agendar(petId, servicoId, data, "09:00", unidade: nome);
            Assert.True(r.Codigo == 200, r.ToString());
            Assert.Equal("Confirmado", r.Texto("status"));

            var msg = Email.Enviados.Last(m => m.Para == conta.Email);
            Assert.Contains("confirmado", msg.Assunto);
            Assert.DoesNotContain(Email.Enviados, m => m.Para == conta.Email && m.Assunto.Contains("recebemos"));
            var id = r.Texto("id");
            Assert.True(await app.NoBanco(db => Task.FromResult(db.EventosLog.Any(e => e.AlvoId == id && e.Acao == "Agendamento criado e confirmado automaticamente"))));

            // Capacidade 1: o mesmo horario nao aceita um segundo pet, confirmado ou nao.
            var outro = app.Api();
            var (_, petOutro) = await outro.ClienteComPet("Tutor Atrasado", "Tobias");
            Assert.Equal(400, (await outro.Agendar(petOutro, servicoId, data, "09:00", unidade: nome)).Codigo);
        }
        finally
        {
            await painel.Post($"/api/admin/unidades/{unidadeId}/ativa", new { token, ativa = false });
        }
    }

    [Fact]
    public async Task Unidade_sem_confirmacao_automatica_continua_nascendo_solicitado()
    {
        var cliente = app.Api();
        var (conta, petId) = await cliente.ClienteComPet("Tutor Manual", "Bisteca");
        var (servicoId, _) = await cliente.Servico();
        var r = await cliente.Agendar(petId, servicoId, Cenarios.NovaData(), "14:00");   // Franco: padrao desligado
        Assert.True(r.Codigo == 200, r.ToString());
        Assert.Equal("Solicitado", r.Texto("status"));
        Assert.Contains("recebemos", Email.Enviados.Last(m => m.Para == conta.Email).Assunto);
    }
}
