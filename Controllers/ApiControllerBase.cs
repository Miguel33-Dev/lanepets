using Microsoft.AspNetCore.Mvc;
namespace LanePets.Controllers;
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult OkApi(object data) => Ok(new { ok=true, data, timestamp=DateTime.UtcNow.ToString("O") });
    /// <summary>
    /// Traducao unica de excecao para status HTTP:
    ///
    ///     UnauthorizedAccessException -> 401  nao esta autenticado
    ///     AcessoNegadoException       -> 403  esta autenticado, mas nao pode
    ///     qualquer outra              -> 400  erro de regra de negocio
    ///
    /// O 403 e o que faz a permissao valer de verdade: sem permissao a
    /// requisicao NAO retorna 200 com dados, retorna 403 sem dado nenhum.
    /// </summary>
    protected IActionResult ErrorApi(Exception ex) => StatusCode(
        ex switch
        {
            UnauthorizedAccessException => 401,
            LanePets.Services.AcessoNegadoException => 403,
            _ => 400
        },
        new { ok=false, error=ex.Message, proibido = ex is LanePets.Services.AcessoNegadoException, timestamp=DateTime.UtcNow.ToString("O") });
}
