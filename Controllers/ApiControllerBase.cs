using Microsoft.AspNetCore.Mvc;
namespace LanePets.Controllers;
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult OkApi(object data) => Ok(new { ok=true, data, timestamp=DateTime.UtcNow.ToString("O") });
    protected IActionResult ErrorApi(Exception ex) => StatusCode(ex is UnauthorizedAccessException ? 401 : 400, new { ok=false, error=ex.Message, timestamp=DateTime.UtcNow.ToString("O") });
}
