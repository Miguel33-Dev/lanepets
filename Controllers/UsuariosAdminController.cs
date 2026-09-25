using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;

/// <summary>
/// Administração > Usuários Administrativos.
///
/// Esta area nao e uma segunda area administrativa: ela vive dentro do mesmo
/// painel, atras do mesmo login, sobre a mesma tabela UsuariosAdministradores
/// que ja autenticava o admin antes.
///
/// REGRA UNICA E INEGOCIAVEL DESTE CONTROLLER:
///
///     TODOS os endpoints aqui exigem Perfil = AdminGeral.
///
/// Nao existe permissao que libere esta area para um administrador comum, e
/// "acesso total" tambem nao a libera — acesso total abre as FERRAMENTAS do
/// petshop, nunca a autoridade sobre os outros administradores. Um admin comum
/// que chame qualquer rota daqui, pelo navegador ou pelo Postman, leva 403.
///
/// A razao e simples: se administrar administradores fosse uma permissao
/// delegavel, bastaria conceder essa permissao uma vez para que a hierarquia
/// deixasse de existir — o delegado se auto-concederia acesso total no clique
/// seguinte. O topo da hierarquia nao se delega.
///
/// Acima disso valem as travas de integridade: ninguem altera as proprias
/// permissoes (nem o Administrador Geral), ninguem desativa ou exclui a si
/// mesmo, e o ultimo Administrador Geral nao pode ser removido.
/// </summary>
[Route("api/admin/usuarios")]
public class UsuariosAdminController(LanePetsDbContext db, PermissaoService permissoes, SessionService sessions) : ApiControllerBase
{
    // =======================================================================
    // CATALOGO DE MODULOS — a tela de permissoes se monta a partir daqui
    // =======================================================================
    [HttpGet("modulos")]
    public async Task<IActionResult> Modulos([FromQuery] string token = "")
    {
        try
        {
            await permissoes.ExigirGeralAsync(token);
            return OkApi(new { modulos = ModulosAdmin.Todos.Select(m => new { chave = m.Chave, rotulo = m.Rotulo, grupo = m.Grupo }) });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // =======================================================================
    // LISTAGEM
    // =======================================================================
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string token = "")
    {
        try
        {
            var contexto = await permissoes.ExigirGeralAsync(token);

            var usuarios = await db.UsuariosAdministradores.AsNoTracking().OrderBy(u => u.Nome == "" ? u.Email : u.Nome).ToListAsync();
            var todasPermissoes = await db.UsuariosAdminPermissoes.AsNoTracking().ToListAsync();

            var linhas = usuarios.Select(u =>
            {
                var doUsuario = todasPermissoes.Where(p => p.UsuarioAdminId == u.Id).ToList();
                // "5 permissões" na coluna Acesso = modulos com ao menos uma
                // acao liberada. Modulo listado com tudo desmarcado nao conta.
                var liberados = doUsuario.Count(p => p.PodeVisualizar || p.PodeCriar || p.PodeEditar || p.PodeExcluir);
                return new
                {
                    id = u.Id,
                    nome = string.IsNullOrWhiteSpace(u.Nome) ? u.Email : u.Nome,
                    email = u.Email,
                    telefone = u.Telefone,
                    perfil = u.Perfil,
                    perfilRotulo = PerfilAdmin.Rotulo(u.Perfil),
                    adminGeral = u.Perfil == PerfilAdmin.Geral,
                    ativo = u.Ativo,
                    acessoTotal = u.AcessoTotal || u.Perfil == PerfilAdmin.Geral,
                    modulosLiberados = liberados,
                    acessoResumo = (u.Perfil == PerfilAdmin.Geral || u.AcessoTotal)
                        ? "Acesso total"
                        : liberados == 0 ? "Sem permissões" : $"{liberados} permiss{(liberados == 1 ? "ão" : "ões")}",
                    criadoEm = u.CriadoEm.ToString("O"),
                    ultimoAcesso = u.UltimoAcesso?.ToString("O"),
                    // O frontend usa isto so para desabilitar botao. O bloqueio
                    // real esta nos endpoints abaixo.
                    editavel = contexto.EhGeral || u.Perfil != PerfilAdmin.Geral,
                    ehVoce = u.Id == contexto.Usuario.Id
                };
            }).ToList();

            return OkApi(new { usuarios = linhas, souAdminGeral = contexto.EhGeral, meuId = contexto.Usuario.Id });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // =======================================================================
    // CRIACAO
    // =======================================================================
    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarUsuarioRequest req)
    {
        try
        {
            var contexto = await permissoes.ExigirGeralAsync(req.Token ?? "");

            var nome = (req.Nome ?? "").Trim();
            var email = (req.Email ?? "").Trim().ToLowerInvariant();
            var senha = req.Senha ?? "";

            if (string.IsNullOrWhiteSpace(nome)) throw new Exception("Informe o nome completo.");
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) throw new Exception("Informe um e-mail válido.");
            if (senha.Length < 6) throw new Exception("A senha precisa ter ao menos 6 caracteres.");
            if (senha != (req.ConfirmarSenha ?? senha)) throw new Exception("A confirmação de senha não confere.");
            if (await db.UsuariosAdministradores.AnyAsync(u => u.Email == email)) throw new Exception("Já existe um administrador com este e-mail.");

            // Criar um Administrador Geral e privilegio de Administrador Geral.
            var perfil = req.AdminGeral ? PerfilAdmin.Geral : PerfilAdmin.Comum;
            if (req.AdminGeral && !contexto.EhGeral)
                throw new AcessoNegadoException("Somente o Administrador Geral pode criar outro Administrador Geral.");
            if (req.AcessoTotal && !contexto.EhGeral)
                throw new AcessoNegadoException("Somente o Administrador Geral pode conceder acesso total.");

            // A senha vai para o banco em BCrypt, pelo MESMO mecanismo que o
            // projeto ja usava (SessionService.HashPassword). Em momento nenhum
            // a senha em texto puro e gravada ou devolvida.
            var (hash, salt) = SessionService.HashPassword(senha);

            var novo = new UsuarioAdministrador
            {
                Id = "ADM-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                Nome = nome,
                Email = email,
                Telefone = (req.Telefone ?? "").Trim(),
                SenhaHash = hash,
                SenhaSalt = salt,
                Ativo = req.Ativo,
                Perfil = perfil,
                // Regra central do pedido: administrador novo NASCE SEM ACESSO.
                // Nenhuma permissao e criada aqui de proposito.
                AcessoTotal = req.AdminGeral || req.AcessoTotal,
                CriadoEm = DateTime.UtcNow
            };

            db.UsuariosAdministradores.Add(novo);
            await db.SaveChangesAsync();
            await permissoes.RegistrarAsync(contexto, "Criou administrador", novo,
                $"Perfil {PerfilAdmin.Rotulo(novo.Perfil)}; acesso total {(novo.AcessoTotal ? "sim" : "não")}; sem permissões individuais.");

            return OkApi(new { id = novo.Id, nome = novo.Nome, email = novo.Email, criado = true });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // =======================================================================
    // EDICAO DE DADOS CADASTRAIS
    // =======================================================================
    [HttpPut("{id}")]
    public async Task<IActionResult> Editar(string id, [FromBody] EditarUsuarioRequest req)
    {
        try
        {
            var contexto = await permissoes.ExigirGeralAsync(req.Token ?? "");
            var alvo = await CarregarAlvoAsync(id);
            GarantirPodeMexerNoAlvo(contexto, alvo);

            var nome = (req.Nome ?? "").Trim();
            var email = (req.Email ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(nome)) throw new Exception("Informe o nome completo.");
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) throw new Exception("Informe um e-mail válido.");
            if (await db.UsuariosAdministradores.AnyAsync(u => u.Email == email && u.Id != alvo.Id))
                throw new Exception("Já existe outro administrador com este e-mail.");

            alvo.Nome = nome;
            alvo.Email = email;
            alvo.Telefone = (req.Telefone ?? "").Trim();
            await db.SaveChangesAsync();
            await permissoes.RegistrarAsync(contexto, "Editou administrador", alvo, "Dados cadastrais atualizados.");

            return OkApi(new { atualizado = true });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // =======================================================================
    // ATIVAR / DESATIVAR
    // =======================================================================
    [HttpPost("{id}/status")]
    public async Task<IActionResult> Status(string id, [FromBody] StatusUsuarioRequest req)
    {
        try
        {
            var contexto = await permissoes.ExigirGeralAsync(req.Token ?? "");
            var alvo = await CarregarAlvoAsync(id);
            GarantirPodeMexerNoAlvo(contexto, alvo);

            if (alvo.Id == contexto.Usuario.Id)
                throw new AcessoNegadoException("Você não pode desativar a sua própria conta.");

            if (!req.Ativo && alvo.Perfil == PerfilAdmin.Geral && await ContarGeraisAtivosAsync() <= 1)
                throw new AcessoNegadoException("Este é o único Administrador Geral ativo do sistema e não pode ser desativado.");

            alvo.Ativo = req.Ativo;
            await db.SaveChangesAsync();

            // Desativou: as sessoes abertas desse usuario caem agora. Sem isso
            // ele continuaria operando ate o token expirar sozinho.
            if (!req.Ativo) sessions.EncerrarSessoesDoUsuario(alvo.Id);

            await permissoes.RegistrarAsync(contexto, req.Ativo ? "Ativou administrador" : "Desativou administrador", alvo,
                req.Ativo ? "Conta reativada." : "Conta desativada e sessões encerradas.");

            return OkApi(new { ativo = alvo.Ativo });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // =======================================================================
    // REDEFINIR SENHA
    // =======================================================================
    [HttpPost("{id}/senha")]
    public async Task<IActionResult> RedefinirSenha(string id, [FromBody] SenhaUsuarioRequest req)
    {
        try
        {
            var contexto = await permissoes.ExigirGeralAsync(req.Token ?? "");
            var alvo = await CarregarAlvoAsync(id);
            GarantirPodeMexerNoAlvo(contexto, alvo);

            var senha = req.Senha ?? "";
            if (senha.Length < 6) throw new Exception("A senha precisa ter ao menos 6 caracteres.");
            if (senha != (req.ConfirmarSenha ?? senha)) throw new Exception("A confirmação de senha não confere.");

            var (hash, salt) = SessionService.HashPassword(senha);
            alvo.SenhaHash = hash;
            alvo.SenhaSalt = salt;
            await db.SaveChangesAsync();

            if (alvo.Id != contexto.Usuario.Id) sessions.EncerrarSessoesDoUsuario(alvo.Id);
            await permissoes.RegistrarAsync(contexto, "Redefiniu senha", alvo, "Nova senha gravada em BCrypt.");

            return OkApi(new { redefinida = true });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // =======================================================================
    // PERMISSOES — leitura
    // =======================================================================
    [HttpGet("{id}/permissoes")]
    public async Task<IActionResult> LerPermissoes(string id, [FromQuery] string token = "")
    {
        try
        {
            await permissoes.ExigirGeralAsync(token);
            var alvo = await CarregarAlvoAsync(id);
            var atuais = await db.UsuariosAdminPermissoes.AsNoTracking().Where(p => p.UsuarioAdminId == alvo.Id).ToListAsync();
            var porModulo = atuais.ToDictionary(p => p.Modulo, StringComparer.OrdinalIgnoreCase);

            return OkApi(new
            {
                usuario = new { id = alvo.Id, nome = string.IsNullOrWhiteSpace(alvo.Nome) ? alvo.Email : alvo.Nome, email = alvo.Email, perfil = alvo.Perfil, adminGeral = alvo.Perfil == PerfilAdmin.Geral },
                acessoTotal = alvo.AcessoTotal || alvo.Perfil == PerfilAdmin.Geral,
                modulos = ModulosAdmin.Todos.Select(m =>
                {
                    porModulo.TryGetValue(m.Chave, out var p);
                    return new
                    {
                        chave = m.Chave,
                        rotulo = m.Rotulo,
                        grupo = m.Grupo,
                        somenteGeral = m.SomenteGeral,
                        visualizar = p?.PodeVisualizar ?? false,
                        criar = p?.PodeCriar ?? false,
                        editar = p?.PodeEditar ?? false,
                        excluir = p?.PodeExcluir ?? false
                    };
                })
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // =======================================================================
    // PERMISSOES — gravacao  (EXCLUSIVO DO ADMINISTRADOR GERAL)
    // =======================================================================
    [HttpPut("{id}/permissoes")]
    public async Task<IActionResult> SalvarPermissoes(string id, [FromBody] SalvarPermissoesRequest req)
    {
        try
        {
            // Nao basta ter o modulo "Usuários Administrativos" liberado: conceder
            // poder e privilegio do Administrador Geral. Sem esta linha, um
            // administrador comum com acesso a esta tela se promoveria sozinho.
            var contexto = await permissoes.ExigirGeralAsync(req.Token ?? "");
            var alvo = await CarregarAlvoAsync(id);

            if (alvo.Id == contexto.Usuario.Id)
                throw new AcessoNegadoException("Você não pode alterar as suas próprias permissões.");

            if (alvo.Perfil == PerfilAdmin.Geral)
                throw new AcessoNegadoException("O Administrador Geral já possui acesso total e suas permissões não são editáveis.");

            var anterior = await db.UsuariosAdminPermissoes.Where(p => p.UsuarioAdminId == alvo.Id).ToListAsync();
            var resumoAnterior = Resumir(alvo.AcessoTotal, anterior);

            alvo.AcessoTotal = req.AcessoTotal;

            // Regravacao completa: a tela manda o estado final de todos os
            // modulos, entao o que nao vier marcado fica sem permissao. Isso
            // torna "remover permissão" tao simples quanto desmarcar a caixa.
            db.UsuariosAdminPermissoes.RemoveRange(anterior);

            var novas = new List<UsuarioAdminPermissao>();
            foreach (var item in req.Modulos ?? new())
            {
                if (!ModulosAdmin.Existe(item.Chave)) continue;            // modulo inventado pelo cliente e ignorado
                if (ModulosAdmin.EhExclusivoDoGeral(item.Chave)) continue; // "Usuários Administrativos" nao se delega,
                                                                          // nem com a requisicao forjada pedindo isso
                if (!item.Visualizar && !item.Criar && !item.Editar && !item.Excluir) continue;

                novas.Add(new UsuarioAdminPermissao
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UsuarioAdminId = alvo.Id,
                    Modulo = item.Chave.ToLowerInvariant(),
                    // Criar/editar/excluir sem poder visualizar nao existe na
                    // pratica: quem pode mexer, pode ver.
                    PodeVisualizar = item.Visualizar || item.Criar || item.Editar || item.Excluir,
                    PodeCriar = item.Criar,
                    PodeEditar = item.Editar,
                    PodeExcluir = item.Excluir
                });
            }

            db.UsuariosAdminPermissoes.AddRange(novas);
            await db.SaveChangesAsync();

            await permissoes.RegistrarAsync(contexto, "Alterou permissões", alvo,
                $"De [{resumoAnterior}] para [{Resumir(alvo.AcessoTotal, novas)}].");

            return OkApi(new { salvo = true, acessoTotal = alvo.AcessoTotal, modulos = novas.Count });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // =======================================================================
    // EXCLUSAO
    // =======================================================================
    [HttpDelete("{id}")]
    public async Task<IActionResult> Excluir(string id, [FromQuery] string token = "")
    {
        try
        {
            var contexto = await permissoes.ExigirGeralAsync(token);
            var alvo = await CarregarAlvoAsync(id);
            GarantirPodeMexerNoAlvo(contexto, alvo);

            if (alvo.Id == contexto.Usuario.Id)
                throw new AcessoNegadoException("Você não pode excluir a sua própria conta.");

            if (alvo.Perfil == PerfilAdmin.Geral && await db.UsuariosAdministradores.CountAsync(u => u.Perfil == PerfilAdmin.Geral) <= 1)
                throw new AcessoNegadoException("Este é o único Administrador Geral do sistema e não pode ser excluído.");

            db.UsuariosAdminPermissoes.RemoveRange(db.UsuariosAdminPermissoes.Where(p => p.UsuarioAdminId == alvo.Id));
            db.UsuariosAdministradores.Remove(alvo);
            await db.SaveChangesAsync();
            sessions.EncerrarSessoesDoUsuario(alvo.Id);

            // O registro do alvo saiu, mas a auditoria guarda id e e-mail dele.
            await permissoes.RegistrarAsync(contexto, "Excluiu administrador", alvo, "Usuário e permissões removidos.");

            return OkApi(new { excluido = true });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // =======================================================================
    // AUDITORIA
    // =======================================================================
    [HttpGet("auditoria")]
    public async Task<IActionResult> Auditoria([FromQuery] string token = "", [FromQuery] int limite = 100)
    {
        try
        {
            await permissoes.ExigirGeralAsync(token);
            var registros = await db.AuditoriasAdmin.AsNoTracking()
                .OrderByDescending(a => a.DataHora)
                .Take(Math.Clamp(limite, 1, 500))
                .ToListAsync();
            return OkApi(new { registros = registros.Select(a => new { a.Id, dataHora = a.DataHora.ToString("O"), a.AutorEmail, a.Acao, a.AlvoEmail, a.Detalhes }) });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // =======================================================================
    // APOIO
    // =======================================================================
    private async Task<UsuarioAdministrador> CarregarAlvoAsync(string id)
        => await db.UsuariosAdministradores.FirstOrDefaultAsync(u => u.Id == id)
           ?? throw new Exception("Usuário administrativo não encontrado.");

    /// <summary>
    /// Protecao do Administrador Geral (regra 20): administrador comum nao
    /// encosta em um Administrador Geral — nem para editar, nem para desativar,
    /// nem para excluir, nem para redefinir a senha dele.
    /// </summary>
    private static void GarantirPodeMexerNoAlvo(ContextoAdmin contexto, UsuarioAdministrador alvo)
    {
        if (alvo.Perfil == PerfilAdmin.Geral && !contexto.EhGeral)
            throw new AcessoNegadoException("Somente o Administrador Geral pode alterar a conta de um Administrador Geral.");
    }

    private async Task<int> ContarGeraisAtivosAsync()
        => await db.UsuariosAdministradores.CountAsync(u => u.Perfil == PerfilAdmin.Geral && u.Ativo);

    private static string Resumir(bool acessoTotal, IEnumerable<UsuarioAdminPermissao> permissoes)
    {
        if (acessoTotal) return "acesso total";
        var lista = permissoes.Select(p => p.Modulo).OrderBy(m => m).ToList();
        return lista.Count == 0 ? "sem permissões" : string.Join(", ", lista);
    }

    // =======================================================================
    // CORPOS DE REQUISICAO
    // =======================================================================
    public record CriarUsuarioRequest(string? Token, string? Nome, string? Email, string? Telefone, string? Senha, string? ConfirmarSenha, bool Ativo = true, bool AcessoTotal = false, bool AdminGeral = false);
    public record EditarUsuarioRequest(string? Token, string? Nome, string? Email, string? Telefone);
    public record StatusUsuarioRequest(string? Token, bool Ativo);
    public record SenhaUsuarioRequest(string? Token, string? Senha, string? ConfirmarSenha);
    public record ModuloPermissaoRequest(string Chave, bool Visualizar, bool Criar, bool Editar, bool Excluir);
    public record SalvarPermissoesRequest(string? Token, bool AcessoTotal, List<ModuloPermissaoRequest>? Modulos);
}
