using LanePets.Data;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;

namespace LanePets.Controllers;

/// <summary>
/// Item 19 do roadmap (25/09): tela "Integridade do banco".
/// Somente leitura e so para o Administrador Geral (mesma regra do Log de
/// eventos): a varredura mostra e-mails e ids de todos os cadastros.
/// </summary>
[Route("api/admin/integridade")]
public class IntegridadeController(LanePetsDbContext db, PermissaoService permissoes, IConfiguration configuracao) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Verificar([FromQuery] string token = "")
    {
        try
        {
            await permissoes.ExigirGeralAsync(token);
            var verificacoes = await IntegridadeService.VerificarAsync(db);
            var caminho = DatabaseBootstrap.ResolverCaminho(configuracao);
            var backups = BancoService.ListarBackups(caminho);
            return OkApi(new
            {
                geradoEm = DateTime.UtcNow.ToString("O"),
                resumo = new
                {
                    erros = verificacoes.Count(v => v.Nivel == "erro"),
                    avisos = verificacoes.Count(v => v.Nivel == "aviso"),
                    informativos = verificacoes.Count(v => v.Nivel == "info"),
                    ok = verificacoes.Count(v => v.Nivel == "ok"),
                    registrosComProblema = verificacoes.Where(v => v.Nivel is "erro" or "aviso").Sum(v => v.Total)
                },
                verificacoes = verificacoes.Select(v => new { v.Codigo, v.Titulo, v.Nivel, v.Total, exemplos = v.Exemplos.Select(e => new { e.Id, e.Descricao }), v.Dica }),
                banco = new
                {
                    arquivo = caminho,
                    bytes = System.IO.File.Exists(caminho) ? new FileInfo(caminho).Length : 0,
                    pastaBackups = BancoService.PastaBackups(caminho),
                    ultimoBackup = BancoService.UltimoBackup,
                    erroBackup = BancoService.UltimoErroBackup,
                    backups = backups.Select(b => new { b.Arquivo, b.Bytes, criadoEm = DateTime.SpecifyKind(b.CriadoEm, DateTimeKind.Utc).ToString("O") }),
                    indices = BancoService.UltimosIndices.Select(i => new { i.Nome, i.Tabela, i.Colunas, i.Situacao })
                }
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
}
