namespace LanePets.Services;

/// <summary>
/// 29/09: regras para subir o LanePets fora da maquina de desenvolvimento.
///
/// Modo demonstracao (<c>LanePets:Demo=true</c>, ou ambiente Development): a tela de login do painel
/// mostra as credenciais de teste e as senhas de exemplo sao aceitas — bom para rodar local.
/// Fora dele (producao de verdade), a aplicacao NAO sobe com a senha financeira de exemplo e o
/// administrador inicial nasce com <c>LanePets:AdminSenhaInicial</c> — senao qualquer pessoa que
/// abrisse o link entraria como Administrador Geral com admin@gmail.com / 123456.
/// </summary>
public static class Hospedagem
{
    public const string SenhaDeExemplo = "123456";

    public static bool ModoDemo(IConfiguration config, IHostEnvironment env)
        => env.IsDevelopment() || string.Equals(config["LanePets:Demo"], "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Senha do administrador inicial quando ele for criado (so na primeira subida de um banco novo).</summary>
    public static string SenhaAdminInicial(IConfiguration config)
        => config["LanePets:AdminSenhaInicial"] is { Length: > 0 } s ? s : SenhaDeExemplo;

    public static void ConferirSegredos(IConfiguration config, IHostEnvironment env)
    {
        if (ModoDemo(config, env) || env.IsEnvironment("Testing")) return;
        var faltando = new List<string>();
        if (config["LanePets:FinancePassword"] is not { Length: > 0 } fin || fin == SenhaDeExemplo)
            faltando.Add("LanePets__FinancePassword (senha da área financeira)");
        var admin = config["LanePets:AdminSenhaInicial"];
        if (string.IsNullOrWhiteSpace(admin) || admin == SenhaDeExemplo)
            faltando.Add("LanePets__AdminSenhaInicial (senha do admin@gmail.com num banco novo)");
        else
            Validacao.Senha(admin, admin);   // mesma regra de toda senha nova: 8+ com letra e numero
        if (faltando.Count > 0)
            throw new InvalidOperationException(
                "LanePets em produção com senha de exemplo. Defina as variáveis de ambiente: " + string.Join("; ", faltando) +
                ". Para uma vitrine com as credenciais de teste, use LanePets__Demo=true.");
    }
}
