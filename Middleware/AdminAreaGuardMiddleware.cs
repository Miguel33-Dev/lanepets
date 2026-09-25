using LanePets.Services;

namespace LanePets.Middleware;

/// <summary>
/// Porta de entrada das PAGINAS administrativas.
///
///     /produtos.html
///          |
///     tem sessao?  -- nao -->  /admin-login.html
///          | sim
///     pode ver o modulo? -- nao -->  /acesso-negado.html
///          | sim
///     a pagina e servida
///
/// Isto acontece ANTES de UseStaticFiles, entao digitar a URL na barra de
/// enderecos nao entrega o HTML. Mesmo assim, esta camada e conveniencia: o
/// dado em si esta protegido nos endpoints da API, que respondem 403 sozinhos.
/// </summary>
public class AdminAreaGuardMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Pagina -> modulos que dao direito de abri-la. Mais de um modulo
    /// significa "qualquer um destes serve": /gestao-publica.html cuida de
    /// seguros E avaliacoes, entao quem tem so um dos dois ainda entra.
    /// </summary>
    private static readonly Dictionary<string, string[]> PaginasProtegidas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/index.html"]                = [ModulosAdmin.Pets, ModulosAdmin.Clientes],
        ["/clientes.html"]             = [ModulosAdmin.Clientes],
        ["/clientes_pacote.html"]      = [ModulosAdmin.Pets],
        ["/agendamentos.html"]         = [ModulosAdmin.Agendamentos],
        ["/produtos.html"]             = [ModulosAdmin.Produtos],
        ["/entradas_e_saidas.html"]    = [ModulosAdmin.Pagamentos],
        ["/dashboard_financeiro.html"] = [ModulosAdmin.Dashboard],
        ["/relatorio.html"]            = [ModulosAdmin.Relatorios],
        ["/gestao-publica.html"]       = [ModulosAdmin.Seguros, ModulosAdmin.Avaliacoes],
        ["/migrar-dados.html"]         = [ModulosAdmin.Configuracoes],
        ["/pedidos.html"]              = [ModulosAdmin.Pedidos],
        ["/usuarios-admin.html"]       = [ModulosAdmin.Usuarios]
    };

    public async Task InvokeAsync(HttpContext context, PermissaoService permissoes)
    {
        var caminho = context.Request.Path.Value ?? string.Empty;

        if (!PaginasProtegidas.TryGetValue(caminho, out var modulosAceitos))
        {
            await next(context);
            return;
        }

        var token = context.Request.Cookies["lanePetsAdmin"] ?? "";

        ContextoAdmin contexto;
        try
        {
            contexto = await permissoes.ResolverAsync(token);
        }
        catch (UnauthorizedAccessException)
        {
            // Sem sessao, sessao expirada ou conta desativada: volta ao login.
            context.Response.Redirect("/admin-login.html?retorno=" + Uri.EscapeDataString(caminho));
            return;
        }

        if (!modulosAceitos.Any(m => contexto.Pode(m, AcaoPermissao.Visualizar)))
        {
            context.Response.Redirect("/acesso-negado.html?modulo=" + Uri.EscapeDataString(modulosAceitos[0]));
            return;
        }

        await next(context);
    }
}
