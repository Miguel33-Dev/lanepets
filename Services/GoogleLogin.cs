namespace LanePets.Services;

/// <summary>
/// Tarefa 1 (Login com Google), etapa 1 — configuracao.
/// Fluxo: botao oficial do Google (Google Identity Services) no navegador -> ID token (JWT) ->
/// o backend confere a assinatura e abre a sessao de cliente de sempre (X-LanePets-Client).
/// Esse fluxo so usa o Client ID, que e PUBLICO (vai na pagina); nao existe segredo para guardar.
/// Mesmo assim o valor fica fora do git: <c>LanePets:Google:ClientId</c> no appsettings.Development.json
/// ou na variavel de ambiente <c>LanePets__Google__ClientId</c> (Railway). Vazio = login com Google desligado.
/// </summary>
public static class GoogleLogin
{
    public const string Chave = "LanePets:Google:ClientId";
    private const string Sufixo = ".apps.googleusercontent.com";

    /// <summary>Client ID configurado e com o formato do Google; qualquer outra coisa = desligado (falha fechada).</summary>
    public static string? ClientId(IConfiguration config)
    {
        var id = (config[Chave] ?? "").Trim();
        return id.Length > Sufixo.Length && id.EndsWith(Sufixo, StringComparison.Ordinal) && !id.Any(char.IsWhiteSpace) ? id : null;
    }

    public static bool Ativo(IConfiguration config) => ClientId(config) is not null;

    /// <summary>Aviso de subida quando a chave existe mas nao tem o formato do Google (nunca derruba o app).</summary>
    public static void AvisarSeMalConfigurado(IConfiguration config, ILogger logger)
    {
        // Etapa 6 (Railway): a subida diz se o login com Google esta ligado — o Client ID e publico, entao pode aparecer.
        if (ClientId(config) is { } id) logger.LogInformation("LanePets: login com Google ligado (Client ID {Id}).", id);
        else if (string.IsNullOrWhiteSpace(config[Chave])) logger.LogInformation("LanePets: login com Google desligado ({Chave} vazio).", Chave);
        if (!string.IsNullOrWhiteSpace(config[Chave]) && !Ativo(config))
            logger.LogWarning("{Chave} preenchido, mas nao parece um Client ID do Google (termina em {Sufixo}). Login com Google desligado.", Chave, Sufixo);
    }
}
