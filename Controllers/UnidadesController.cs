using System.Text.Json;
using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;

/// <summary>
/// Administração > Unidades (item 5 do roadmap, 24/09).
///
/// Modulo proprio "unidades" (4 acoes, como os outros). Funcionario nunca tem.
/// Nao existe excluir: a unidade sai de operacao com Ativa = false e o
/// historico (agendamentos, lancamentos, funcionarios) continua apontando para ela.
/// Toda escrita refaz o registro dinamico do Normalizador e vira evento no log.
/// </summary>
[Route("api/admin/unidades")]
public class UnidadesController(LanePetsDbContext db, PermissaoService permissoes, EventosService eventos, RealtimeNotifier realtime) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string token = "")
    {
        try
        {
            await permissoes.ExigirAsync(token, ModulosAdmin.Unidades, AcaoPermissao.Visualizar);

            var unidades = await db.Unidades.AsNoTracking().OrderByDescending(u => u.Ativa).ThenBy(u => u.Nome).ToListAsync();
            var servicos = await db.Servicos.AsNoTracking().OrderBy(s => s.Nome).Select(s => new { s.Id, s.Nome, s.Preco, s.Porte }).ToListAsync();
            var funcionarios = await db.UsuariosAdministradores.AsNoTracking()
                .Where(u => u.Perfil == PerfilAdmin.Funcionario)
                .Select(u => new { u.Id, u.Nome, u.Email, u.Ativo, u.Unidade })
                .ToListAsync();

            // Agenda futura por unidade: conta so o que ainda vai acontecer.
            var hoje = DateTime.Now.ToString("yyyy-MM-dd");
            var futuros = (await db.Agendamentos.AsNoTracking()
                    .Where(a => a.Status != "Cancelado" && string.Compare(a.DataHora, hoje) >= 0)
                    .Select(a => new { a.Unidade, a.Status }).ToListAsync())
                .Where(a => Normalizador.Status(a.Status) != "Cancelado")
                .GroupBy(a => Normalizador.IdUnidade(a.Unidade))
                .ToDictionary(g => g.Key, g => g.Count());

            return OkApi(new
            {
                unidades = unidades.Select(u => new
                {
                    u.Id,
                    u.Nome,
                    u.Endereco,
                    u.Telefone,
                    u.HorarioFuncionamento,
                    u.Ativa,
                    capacidade = UnidadesRegras.CapacidadeDe(u),
                    servicos = UnidadesRegras.Servicos(u),
                    funcionarios = funcionarios.Where(f => f.Unidade == u.Id).Select(f => new { f.Id, nome = string.IsNullOrWhiteSpace(f.Nome) ? f.Email : f.Nome, f.Email, f.Ativo }),
                    agendamentosFuturos = futuros.TryGetValue(u.Id, out var n) ? n : 0
                }),
                servicos
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>
    /// Lista leve (id, nome, ativa, capacidade, servicos) para as TELAS do painel
    /// montarem abas, filtros e selects de unidade. Basta estar logado no
    /// painel: nome de unidade nao e dado sensivel. Inclui as inativas, porque o
    /// historico ainda aponta para elas.
    /// </summary>
    [HttpGet("lista")]
    public async Task<IActionResult> Lista([FromQuery] string token = "")
    {
        try
        {
            var contexto = await permissoes.ResolverAsync(token);
            var unidades = await db.Unidades.AsNoTracking().OrderBy(u => u.Nome).ToListAsync();
            // Item 4: funcionarios ATIVOS de cada unidade, para o select de
            // "responsavel" no agendamento. So id e nome.
            var funcionarios = (await ResponsaveisAgendamento.ListarAsync(db)).Where(f => f.Ativo).ToList();
            return OkApi(new
            {
                minhaUnidade = contexto.EhFuncionario ? Normalizador.IdUnidade(contexto.Usuario.Unidade) : "",
                unidades = unidades.Select(u => new
                {
                    u.Id,
                    u.Nome,
                    u.Ativa,
                    capacidade = UnidadesRegras.CapacidadeDe(u),
                    servicos = UnidadesRegras.Servicos(u),
                    funcionarios = funcionarios.Where(f => f.Unidade == u.Id).Select(f => new { f.Id, f.Nome })
                })
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] UnidadeRequest req)
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(req.Token ?? "", ModulosAdmin.Unidades, AcaoPermissao.Criar);
            var dados = await ValidarAsync(req, idAtual: null);

            var unidade = new Unidade { Id = await NovoIdAsync(dados.Nome), Ativa = true };
            Aplicar(unidade, dados);
            db.Unidades.Add(unidade);
            await db.SaveChangesAsync();
            await UnidadesRegras.RecarregarAsync(db);

            await eventos.RegistrarAsync(new("unidade", "Unidade criada", "info", "admin", contexto.Usuario.Id, contexto.Usuario.Email,
                unidade.Id, $"{unidade.Nome} · capacidade {unidade.Capacidade} por horário"), HttpContext);
            await realtime.NotificarAsync("unidades", "criada", new { unidade.Id, unidade.Nome });
            return OkApi(new { unidade.Id, message = "Unidade cadastrada." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Editar(string id, [FromBody] UnidadeRequest req)
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(req.Token ?? "", ModulosAdmin.Unidades, AcaoPermissao.Editar);
            var unidade = await db.Unidades.FirstOrDefaultAsync(u => u.Id == id) ?? throw new Exception("Unidade não encontrada.");
            var dados = await ValidarAsync(req, unidade.Id);

            var antes = $"{unidade.Nome} · capacidade {unidade.Capacidade}";
            Aplicar(unidade, dados);
            await db.SaveChangesAsync();
            await UnidadesRegras.RecarregarAsync(db);

            await eventos.RegistrarAsync(new("unidade", "Unidade alterada", "info", "admin", contexto.Usuario.Id, contexto.Usuario.Email,
                unidade.Id, $"De [{antes}] para [{unidade.Nome} · capacidade {unidade.Capacidade}]"), HttpContext);
            await realtime.NotificarAsync("unidades", "alterada", new { unidade.Id, unidade.Nome });
            return OkApi(new { unidade.Id, message = "Unidade atualizada." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>Ativar / desativar. Desativar tira a unidade do site e do agendamento, sem apagar nada.</summary>
    [HttpPost("{id}/ativa")]
    public async Task<IActionResult> Ativa(string id, [FromBody] AtivaRequest req)
    {
        try
        {
            var contexto = await permissoes.ExigirAsync(req.Token ?? "", ModulosAdmin.Unidades, AcaoPermissao.Editar);
            var unidade = await db.Unidades.FirstOrDefaultAsync(u => u.Id == id) ?? throw new Exception("Unidade não encontrada.");

            if (!req.Ativa && unidade.Ativa && await db.Unidades.CountAsync(u => u.Ativa) <= 1)
                throw new Exception("Esta é a única unidade ativa. Cadastre ou ative outra antes de desativar esta.");

            unidade.Ativa = req.Ativa;
            await db.SaveChangesAsync();
            await UnidadesRegras.RecarregarAsync(db);

            var futuros = req.Ativa ? 0 : await ContarFuturosAsync(unidade.Id);
            await eventos.RegistrarAsync(new("unidade", req.Ativa ? "Unidade ativada" : "Unidade desativada", req.Ativa ? "info" : "aviso", "admin",
                contexto.Usuario.Id, contexto.Usuario.Email, unidade.Id,
                req.Ativa ? unidade.Nome : $"{unidade.Nome} · {futuros} agendamento(s) futuro(s) continuam registrados"), HttpContext);
            await realtime.NotificarAsync("unidades", req.Ativa ? "ativada" : "desativada", new { unidade.Id, unidade.Nome });

            return OkApi(new
            {
                unidade.Id,
                unidade.Ativa,
                agendamentosFuturos = futuros,
                message = req.Ativa
                    ? "Unidade ativada: volta a aparecer no site e no agendamento."
                    : futuros > 0
                        ? $"Unidade desativada. Atenção: ela tem {futuros} agendamento(s) futuro(s), que continuam registrados — remarque ou avise os clientes."
                        : "Unidade desativada: não aparece mais no site nem no agendamento. O histórico foi mantido."
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // -----------------------------------------------------------------------
    private record Dados(string Nome, string Endereco, string Telefone, string Horario, int Capacidade, List<string> Servicos);

    private async Task<Dados> ValidarAsync(UnidadeRequest req, string? idAtual)
    {
        var nome = (req.Nome ?? "").Trim();
        if (nome.Length < 3) throw new Exception("Informe o nome da unidade (mínimo 3 letras).");
        if (nome.Length > 80) throw new Exception("O nome da unidade pode ter até 80 caracteres.");

        // Nome nao pode colidir com outra unidade nem com o apelido de outra
        // ("Franco" e "Franco da Rocha" sao a mesma coisa para o sistema).
        var outra = Normalizador.IdUnidade(nome);
        if (outra.Length > 0 && outra != idAtual) throw new Exception("Já existe uma unidade com esse nome.");
        var todas = await db.Unidades.AsNoTracking().Where(u => u.Id != idAtual).Select(u => u.Nome).ToListAsync();
        if (todas.Any(n => Normalizador.Texto(n) == Normalizador.Texto(nome))) throw new Exception("Já existe uma unidade com esse nome.");

        var endereco = (req.Endereco ?? "").Trim();
        if (endereco.Length > 200) throw new Exception("O endereço pode ter até 200 caracteres.");
        var telefone = Validacao.Telefone(req.Telefone, obrigatorio: false);
        var horario = (req.HorarioFuncionamento ?? "").Trim();
        if (horario.Length > 120) throw new Exception("O horário de funcionamento pode ter até 120 caracteres.");

        var capacidade = req.Capacidade ?? 1;
        if (capacidade < 1 || capacidade > UnidadesRegras.CapacidadeMaxima)
            throw new Exception($"A capacidade deve ficar entre 1 e {UnidadesRegras.CapacidadeMaxima} atendimentos por horário.");

        var pedidos = (req.Servicos ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();
        if (pedidos.Count > 0)
        {
            var existentes = await db.Servicos.AsNoTracking().Where(s => pedidos.Contains(s.Id)).Select(s => s.Id).ToListAsync();
            var faltando = pedidos.Except(existentes).ToList();
            if (faltando.Count > 0) throw new Exception("Um ou mais serviços escolhidos não existem mais. Atualize a página.");
        }

        return new Dados(nome, endereco, telefone, horario, capacidade, pedidos);
    }

    private static void Aplicar(Unidade u, Dados d)
    {
        u.Nome = d.Nome;
        u.Endereco = d.Endereco;
        u.Telefone = d.Telefone;
        u.HorarioFuncionamento = d.Horario;
        u.Capacidade = d.Capacidade;
        u.ServicosJson = JsonSerializer.Serialize(d.Servicos);
    }

    /// <summary>Id curto a partir do nome ("Jundiaí Centro" -> "jundiai-centro"), sem repetir.</summary>
    private async Task<string> NovoIdAsync(string nome)
    {
        var baseId = new string(Normalizador.Texto(nome).Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (baseId.Contains("--")) baseId = baseId.Replace("--", "-");
        baseId = baseId.Trim('-');
        if (baseId.Length == 0) baseId = "unidade";
        if (baseId.Length > 40) baseId = baseId[..40].Trim('-');
        var id = baseId;
        for (var i = 2; await db.Unidades.AnyAsync(u => u.Id == id); i++) id = $"{baseId}-{i}";
        return id;
    }

    private async Task<int> ContarFuturosAsync(string unidadeId)
    {
        var hoje = DateTime.Now.ToString("yyyy-MM-dd");
        return (await db.Agendamentos.AsNoTracking()
                .Where(a => a.Status != "Cancelado" && string.Compare(a.DataHora, hoje) >= 0)
                .Select(a => new { a.Unidade, a.Status }).ToListAsync())
            .Count(a => Normalizador.Status(a.Status) != "Cancelado" && Normalizador.IdUnidade(a.Unidade) == unidadeId);
    }

    public record UnidadeRequest(string? Token, string? Nome, string? Endereco, string? Telefone, string? HorarioFuncionamento, int? Capacidade, List<string>? Servicos);
    public record AtivaRequest(string? Token, bool Ativa);
}
