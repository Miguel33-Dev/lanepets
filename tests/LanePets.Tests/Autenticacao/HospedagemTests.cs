using LanePets.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;

namespace LanePets.Tests.Autenticacao;

/// <summary>
/// 29/09: no ar de verdade (Production sem LanePets:Demo) o LanePets nao sobe com as senhas de exemplo —
/// senao qualquer pessoa com o link entraria como Administrador Geral com admin@gmail.com / 123456.
/// Regra pura (Hospedagem): nao precisa da aplicacao de pe.
/// </summary>
public class HospedagemTests
{
    private static IConfiguration Config(params (string Chave, string Valor)[] valores)
        => new ConfigurationBuilder().AddInMemoryCollection(valores.ToDictionary(v => v.Chave, v => (string?)v.Valor)).Build();

    private static HostingEnvironment Ambiente(string nome) => new() { EnvironmentName = nome };

    [Fact]
    public void Producao_sem_segredos_nao_sobe()
    {
        var erro = Assert.Throws<InvalidOperationException>(() =>
            Hospedagem.ConferirSegredos(Config(("LanePets:FinancePassword", "123456")), Ambiente("Production")));
        Assert.Contains("LanePets__FinancePassword", erro.Message);
        Assert.Contains("LanePets__AdminSenhaInicial", erro.Message);
    }

    [Fact]
    public void Producao_com_segredos_sobe_e_o_admin_inicial_usa_a_senha_configurada()
    {
        var config = Config(("LanePets:FinancePassword", "Fin2026segura"), ("LanePets:AdminSenhaInicial", "Painel2026seguro"));
        Hospedagem.ConferirSegredos(config, Ambiente("Production"));
        Assert.Equal("Painel2026seguro", Hospedagem.SenhaAdminInicial(config));
        Assert.False(Hospedagem.ModoDemo(config, Ambiente("Production")));
        // Senha inicial fraca tambem nao passa (mesma regra de toda senha nova).
        Assert.ThrowsAny<Exception>(() => Hospedagem.ConferirSegredos(
            Config(("LanePets:FinancePassword", "Fin2026segura"), ("LanePets:AdminSenhaInicial", "abc")), Ambiente("Production")));
    }

    [Fact]
    public void Demo_e_desenvolvimento_aceitam_as_senhas_de_exemplo()
    {
        Hospedagem.ConferirSegredos(Config(("LanePets:Demo", "true")), Ambiente("Production"));
        Hospedagem.ConferirSegredos(Config(), Ambiente("Development"));
        Assert.True(Hospedagem.ModoDemo(Config(("LanePets:Demo", "true")), Ambiente("Production")));
        Assert.Equal("123456", Hospedagem.SenhaAdminInicial(Config()));
    }
}
