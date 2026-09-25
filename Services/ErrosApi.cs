using System.Text;

namespace LanePets.Services;

/// <summary>
/// Codigos de erro da API (item 14 do roadmap, 24/09). Vao na resposta
/// (campo "codigo") e no log, para quem atende conseguir achar o caso.
/// </summary>
public static class CodigoErro
{
    public const string Validacao      = "ERR-4000"; // regra de negocio / campo invalido (400)
    public const string DadosInvalidos = "ERR-4001"; // corpo da requisicao em formato errado (400)
    public const string Sessao         = "ERR-4010"; // sem sessao ou sessao expirada (401)
    public const string Permissao      = "ERR-4030"; // autenticado, mas sem permissao (403)
    public const string NaoEncontrado  = "ERR-4040"; // rota da API inexistente (404)
    public const string Metodo         = "ERR-4050"; // rota existe, metodo HTTP nao (405)
    public const string Interno        = "ERR-5001"; // falha inesperada no servidor (500)
}

/// <summary>
/// TRADUCAO UNICA DE EXCECAO PARA RESPOSTA (item 14 do roadmap, 24/09).
///
///     UnauthorizedAccessException     -> 401  ERR-4010  mensagem da excecao
///     AcessoNegadoException           -> 403  ERR-4030  mensagem da excecao
///     erro de negocio (ver abaixo)    -> 400  ERR-4000  mensagem da excecao
///     QUALQUER OUTRA                  -> 500  ERR-5001  mensagem generica + referencia
///
/// "Erro de negocio" = excecao lancada DE PROPOSITO pelo nosso codigo com texto
/// escrito para o usuario: `new Exception("...")` (o tipo base exato) e as
/// ValidacaoException/ImagemInvalidaException dos Services. Tudo o mais
/// (NullReference, erro do SQLite, JSON quebrado...) e falha inesperada: o
/// usuario NUNCA ve o texto tecnico, ve "Erro interno do sistema" com codigo e
/// referencia; o detalhe completo vai para o log com a mesma referencia.
/// </summary>
public static class ErrosApi
{
    public const string MensagemInterna = "Erro interno do sistema. Tente novamente mais tarde.";

    public static string NovaReferencia() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    public static bool EhErroDeNegocio(Exception ex) =>
        ex.GetType() == typeof(Exception)
        || ex is Validacao.ValidacaoException
        || ex is PetFicha.ValidacaoException
        || ex is ImagemDataUri.ImagemInvalidaException;

    /// <summary>Monta status + corpo no envelope da API e registra o que precisa.</summary>
    public static (int Status, object Corpo) Traduzir(Exception ex, HttpContext contexto)
    {
        var log = contexto.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("LanePets.Erros");
        var agora = DateTime.UtcNow.ToString("O");
        var metodo = contexto.Request.Method;
        var caminho = contexto.Request.Path.Value ?? "";

        switch (ex)
        {
            case UnauthorizedAccessException:
                return (401, new { ok = false, error = ex.Message, codigo = CodigoErro.Sessao, proibido = false, timestamp = agora });

            case AcessoNegadoException:
                log?.LogWarning("LanePets {Codigo}: acesso negado em {Metodo} {Caminho}: {Mensagem}",
                    CodigoErro.Permissao, metodo, caminho, ex.Message);
                contexto.RequestServices.GetService<EventosService>()?.Registrar(new("seguranca", "Acesso negado", "aviso", "admin",
                    Detalhes: $"{metodo} {caminho} — {ex.Message}"), contexto);
                return (403, new { ok = false, error = ex.Message, codigo = CodigoErro.Permissao, proibido = true, timestamp = agora });

            // Quem chamou desistiu (fechou a aba no meio da requisicao): nao e
            // falha do servidor e nao merece virar ERR-5001 no log.
            case OperationCanceledException when contexto.RequestAborted.IsCancellationRequested:
                return (499, new { ok = false, error = "Requisição cancelada.", codigo = "ERR-4990", proibido = false, timestamp = agora });

            case var _ when EhErroDeNegocio(ex):
                return (400, new { ok = false, error = ex.Message, codigo = CodigoErro.Validacao, proibido = false, timestamp = agora });

            default:
                var referencia = NovaReferencia();
                log?.LogError(ex, "LanePets {Codigo} ref {Referencia}: falha inesperada em {Metodo} {Caminho}",
                    CodigoErro.Interno, referencia, metodo, caminho);
                contexto.RequestServices.GetService<RegistroErros>()?.Registrar(referencia, CodigoErro.Interno, metodo, caminho, ex);
                // Item 15: o erro interno tambem entra no log de eventos, so com o
                // tipo da excecao (o detalhe tecnico fica no arquivo de erros).
                contexto.RequestServices.GetService<EventosService>()?.Registrar(new("sistema", "Erro interno", "erro", "sistema",
                    Detalhes: $"{metodo} {caminho} — {ex.GetType().Name}", Referencia: referencia), contexto);
                return (500, new
                {
                    ok = false,
                    // O codigo e a referencia vao DENTRO do texto de proposito:
                    // toda tela ja mostra "error", entao o usuario ve o codigo
                    // sem nenhuma mudanca no frontend.
                    error = $"{MensagemInterna} Código: {CodigoErro.Interno} · Ref. {referencia}",
                    codigo = CodigoErro.Interno,
                    referencia,
                    proibido = false,
                    timestamp = agora
                });
        }
    }
}

/// <summary>
/// Arquivo de erros inesperados: um arquivo por dia em
/// &lt;pasta do banco&gt;/logs/erros-AAAA-MM-DD.log, fora do OneDrive, junto do
/// lanepets.db. Cada linha comeca com a referencia mostrada ao usuario, entao
/// "Ref. 7F3A2C9B" na tela = procurar 7F3A2C9B no arquivo.
/// Nunca derruba a requisicao: se nao conseguir escrever, so desiste.
/// </summary>
public class RegistroErros(string pasta, ILogger<RegistroErros> log)
{
    private readonly object _trava = new();

    public string Pasta => pasta;

    public void Registrar(string referencia, string codigo, string metodo, string caminho, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(pasta);
            var arquivo = Path.Combine(pasta, $"erros-{DateTime.Now:yyyy-MM-dd}.log");
            var texto = new StringBuilder()
                .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                .Append(referencia).Append(' ').Append(codigo).Append(' ')
                .Append(metodo).Append(' ').AppendLine(caminho)
                .AppendLine(ex.ToString())
                .AppendLine(new string('-', 80))
                .ToString();
            lock (_trava) File.AppendAllText(arquivo, texto, Encoding.UTF8);
        }
        catch (Exception falha)
        {
            log.LogWarning(falha, "LanePets: não foi possível gravar o arquivo de erros em {Pasta}.", pasta);
        }
    }
}
