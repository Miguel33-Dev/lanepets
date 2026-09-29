namespace LanePets.Services;

/// <summary>
/// Seguranca — Etapa 4 da auditoria (29/09), item H: o token do painel vai no header, nunca na URL.
///
/// Na URL (?token=...) ele fica no historico do navegador, nos logs de acesso do servidor/proxy e podia
/// sair no Referer. Agora as telas mandam:
///     X-LanePets-Admin: &lt;token do painel&gt;
///     X-LanePets-Financeiro: &lt;token da senha financeira&gt;
///
/// O backend continua aceitando o token antigo (?token= ou "token" no corpo) para nao quebrar uma aba aberta com
/// JS antigo em cache; o header, quando vem, tem prioridade. O cookie lanePetsAdmin sozinho continua NAO valendo
/// para a API (protecao contra CSRF: outro site consegue fazer o navegador mandar cookie, mas nao um header).
/// </summary>
public static class TokenPainel
{
    public const string Cabecalho = "X-LanePets-Admin";
    public const string CabecalhoFinanceiro = "X-LanePets-Financeiro";

    public static string Escolher(HttpContext? http, string? informado, string cabecalho = Cabecalho)
    {
        var doHeader = http?.Request.Headers[cabecalho].ToString().Trim() ?? "";
        return doHeader.Length > 0 ? doHeader : (informado ?? "").Trim();
    }
}
