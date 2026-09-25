using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
namespace LanePets.Controllers;
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult OkApi(object data) => Ok(new { ok=true, data, timestamp=DateTime.UtcNow.ToString("O") });

    /// <summary>
    /// Traducao unica de excecao para status HTTP — a regra mora em
    /// ErrosApi.Traduzir (item 14 do roadmap):
    ///
    ///     UnauthorizedAccessException -> 401  ERR-4010  nao esta autenticado
    ///     AcessoNegadoException       -> 403  ERR-4030  esta autenticado, mas nao pode
    ///     erro de negocio             -> 400  ERR-4000  mensagem escrita para o usuario
    ///     qualquer outra              -> 500  ERR-5001  mensagem generica + referencia no log
    ///
    /// O 403 e o que faz a permissao valer de verdade: sem permissao a
    /// requisicao NAO retorna 200 com dados, retorna 403 sem dado nenhum.
    /// E o 500 garante que texto tecnico (NullReference, SQLite...) nunca
    /// chega na tela — antes ele ia como 400 com a mensagem crua.
    /// </summary>
    protected IActionResult ErrorApi(Exception ex)
    {
        var (status, corpo) = ErrosApi.Traduzir(ex, HttpContext);
        return StatusCode(status, corpo);
    }
}
