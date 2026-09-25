using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Perfis gravados na coluna UsuariosAdministradores.Perfil.
///
/// O Administrador Geral e identificado POR ESTE VALOR, no banco. Em lugar
/// nenhum do sistema a autorizacao depende de comparar o e-mail com
/// "admin@gmail.com": trocar o e-mail do administrador nao derruba nem concede
/// privilegio nenhum.
/// </summary>
public static class PerfilAdmin
{
    public const string Geral = "AdminGeral";
    public const string Comum = "Admin";

    /// <summary>
    /// Funcionario (item 1 do roadmap, 24/09): terceiro perfil, MAIS RESTRITO
    /// que o Admin comum. Tres travas valem no backend, qualquer que seja o
    /// que a tela mande:
    ///
    ///   1. nunca tem acesso total (a coluna AcessoTotal e ignorada);
    ///   2. so pode receber permissao nos modulos de operacao
    ///      (ModulosAdmin.LimiteFuncionario) — financeiro, relatorios,
    ///      configuracoes, site publico e usuarios ficam fechados;
    ///   3. fica preso a UMA unidade (UsuarioAdministrador.Unidade):
    ///      agendamentos e pets de outra unidade nao chegam para ele.
    /// </summary>
    public const string Funcionario = "Funcionario";

    public static readonly string[] Todos = [Geral, Comum, Funcionario];

    public static string Rotulo(string perfil) => perfil switch
    {
        Geral => "Administrador Geral",
        Funcionario => "Funcionário",
        _ => "Administrador"
    };
}

/// <summary>As quatro acoes que uma permissao pode liberar.</summary>
public enum AcaoPermissao { Visualizar, Criar, Editar, Excluir }

/// <summary>
/// Catalogo dos modulos administrativos. E a unica lista: o menu do painel, a
/// tela de permissoes e a verificacao dos endpoints leem daqui, entao nao
/// existe modulo protegido no backend que a tela nao saiba mostrar, nem o
/// contrario.
/// </summary>
public static class ModulosAdmin
{
    public const string Dashboard = "dashboard";
    public const string Clientes = "clientes";
    public const string Pets = "pets";
    public const string Agendamentos = "agendamentos";
    public const string Produtos = "produtos";
    public const string Servicos = "servicos";
    public const string Pedidos = "pedidos";
    public const string Pagamentos = "pagamentos";
    public const string Seguros = "seguros";
    public const string Avaliacoes = "avaliacoes";
    public const string Relatorios = "relatorios";
    public const string Unidades = "unidades";
    public const string Usuarios = "usuarios";
    public const string Configuracoes = "configuracoes";

    /// <param name="SomenteGeral">
    /// Modulo que NAO pode ser delegado. Ele pertence ao Administrador Geral e
    /// so a ele — nem uma permissao individual, nem o acesso total abrem esta
    /// porta para um administrador comum. E o que impede a hierarquia de se
    /// desfazer: quem administra administradores e sempre o topo.
    /// </param>
    public record Definicao(string Chave, string Rotulo, string Grupo, bool SomenteGeral = false);

    public static readonly IReadOnlyList<Definicao> Todos =
    [
        new(Dashboard,     "Dashboard",                "Operação"),
        new(Agendamentos,  "Agendamentos",             "Operação"),
        new(Pedidos,       "Pedidos",                  "Operação"),
        new(Clientes,      "Clientes",                 "Operação"),
        new(Pets,          "Pets e pacotes",           "Operação"),
        new(Produtos,      "Produtos",                 "Catálogo"),
        new(Servicos,      "Serviços",                 "Catálogo"),
        new(Seguros,       "Seguros Pet",              "Site público"),
        new(Avaliacoes,    "Avaliações",               "Site público"),
        new(Pagamentos,    "Pagamentos e financeiro",  "Financeiro"),
        new(Relatorios,    "Relatórios",               "Financeiro"),
        new(Unidades,      "Unidades",                 "Administração"),
        new(Usuarios,      "Usuários Administrativos", "Administração", SomenteGeral: true),
        new(Configuracoes, "Configurações",            "Administração"),
    ];

    /// <summary>Modulos exclusivos do Administrador Geral.</summary>
    public static readonly HashSet<string> ExclusivosDoGeral =
        Todos.Where(m => m.SomenteGeral).Select(m => m.Chave).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool EhExclusivoDoGeral(string chave) => ExclusivosDoGeral.Contains(chave ?? "");

    public static readonly HashSet<string> Chaves =
        Todos.Select(m => m.Chave).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool Existe(string chave) => Chaves.Contains(chave ?? "");

    /// <summary>
    /// Teto do perfil Funcionario: modulo -> acoes que PODEM ser concedidas a
    /// ele. Fora desta lista, nem a permissao gravada no banco abre a porta.
    /// Produtos e so consulta (o funcionario precisa ver preco e estoque para
    /// atender, nao alterar catalogo).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, AcaoPermissao[]> LimiteFuncionario =
        new Dictionary<string, AcaoPermissao[]>(StringComparer.OrdinalIgnoreCase)
        {
            [Dashboard]    = [AcaoPermissao.Visualizar],
            [Agendamentos] = [AcaoPermissao.Visualizar, AcaoPermissao.Criar, AcaoPermissao.Editar, AcaoPermissao.Excluir],
            [Pedidos]      = [AcaoPermissao.Visualizar, AcaoPermissao.Criar, AcaoPermissao.Editar],
            [Clientes]     = [AcaoPermissao.Visualizar, AcaoPermissao.Criar, AcaoPermissao.Editar],
            [Pets]         = [AcaoPermissao.Visualizar, AcaoPermissao.Criar, AcaoPermissao.Editar],
            [Produtos]     = [AcaoPermissao.Visualizar],
        };

    public static bool FuncionarioPode(string modulo, AcaoPermissao acao)
        => LimiteFuncionario.TryGetValue(modulo ?? "", out var acoes) && acoes.Contains(acao);
}

/// <summary>
/// Lancada quando o usuario ESTA autenticado mas nao tem a permissao exigida.
/// E diferente de UnauthorizedAccessException (sessao invalida): esta vira
/// HTTP 403, aquela vira 401. ApiControllerBase faz essa traducao.
/// </summary>
public class AcessoNegadoException(string mensagem) : Exception(mensagem) { }

/// <summary>
/// O que o backend sabe sobre quem esta chamando o endpoint.
/// </summary>
public class ContextoAdmin
{
    public required UsuarioAdministrador Usuario { get; init; }
    public required IReadOnlyDictionary<string, UsuarioAdminPermissao> Permissoes { get; init; }

    public bool EhGeral => Usuario.Perfil == PerfilAdmin.Geral;

    public bool EhFuncionario => Usuario.Perfil == PerfilAdmin.Funcionario;

    /// <summary>
    /// Acesso irrestrito: Administrador Geral ou usuario com acesso total
    /// ligado. Funcionario NUNCA tem acesso irrestrito, mesmo que a coluna
    /// AcessoTotal tenha ficado ligada por engano no banco.
    /// </summary>
    public bool AcessoIrrestrito => EhGeral || (Usuario.AcessoTotal && !EhFuncionario);

    /// <summary>Unidade do funcionario, no formato do Normalizador ("Franco", "Caieiras").</summary>
    public string UnidadeDoFuncionario => EhFuncionario ? Normalizador.Unidade(Usuario.Unidade) : "";

    /// <summary>
    /// true quando o registro da unidade informada pode chegar a este usuario.
    /// Administradores veem todas. Funcionario ve so a dele — e, sem unidade
    /// valida gravada, nao ve nenhuma (falha fechada, nunca aberta).
    /// </summary>
    public bool VeUnidade(string? unidade)
    {
        if (!EhFuncionario) return true;
        var minha = UnidadeDoFuncionario;
        return minha.Length > 0 && Normalizador.Unidade(unidade) == minha;
    }

    public bool Pode(string modulo, AcaoPermissao acao)
    {
        // Modulo exclusivo do Geral: nem permissao individual nem acesso total
        // abrem. "Acesso total" libera as FERRAMENTAS do petshop, nunca a
        // autoridade sobre os outros administradores.
        if (ModulosAdmin.EhExclusivoDoGeral(modulo)) return EhGeral;

        // Teto do Funcionario: o que esta fora dele nao abre nem com a linha de
        // permissao gravada no banco.
        if (EhFuncionario && !ModulosAdmin.FuncionarioPode(modulo, acao)) return false;

        if (AcessoIrrestrito) return true;
        if (!Permissoes.TryGetValue(modulo, out var p)) return false;
        return acao switch
        {
            AcaoPermissao.Visualizar => p.PodeVisualizar,
            AcaoPermissao.Criar => p.PodeCriar,
            AcaoPermissao.Editar => p.PodeEditar,
            AcaoPermissao.Excluir => p.PodeExcluir,
            _ => false
        };
    }
}

/// <summary>
/// ONDE A AUTORIZACAO ACONTECE DE VERDADE.
///
///     requisicao -> token -> sessao valida? -> usuario ativo? -> pode a acao?
///                     401          401              401             403
///
/// Esconder um botao no HTML nao protege nada: quem chamar
/// DELETE /api/... na mao passa pelo mesmo caminho acima e leva 403 igual.
/// </summary>
public class PermissaoService(LanePetsDbContext db, SessionService sessions, EventosService eventos)
{
    private ContextoAdmin? _cache;
    private string _cacheToken = "";

    /// <summary>
    /// Resolve o administrador da requisicao. Faz, nesta ordem: valida a
    /// sessao, carrega o usuario do banco e recusa conta desativada.
    ///
    /// O usuario e relido do banco a cada requisicao de proposito: permissao
    /// alterada agora vale na proxima chamada, sem precisar deslogar. E um
    /// administrador desativado para de operar mesmo que continue com o token
    /// na mao (regra 19).
    /// </summary>
    public async Task<ContextoAdmin> ResolverAsync(string token)
    {
        if (_cache is not null && _cacheToken == token) return _cache;

        var sessao = sessions.RequireAdmin(token);

        if (string.IsNullOrWhiteSpace(sessao.UsuarioId))
            throw new UnauthorizedAccessException("Sessão sem usuário associado. Faça login novamente.");

        var usuario = await db.UsuariosAdministradores.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == sessao.UsuarioId)
            ?? throw new UnauthorizedAccessException("Usuário administrativo não encontrado.");

        if (!usuario.Ativo)
        {
            sessions.Logout(token);
            throw new UnauthorizedAccessException("Esta conta administrativa está desativada.");
        }

        var permissoes = await db.UsuariosAdminPermissoes.AsNoTracking()
            .Where(p => p.UsuarioAdminId == usuario.Id)
            .ToListAsync();

        _cacheToken = token;
        return _cache = new ContextoAdmin
        {
            Usuario = usuario,
            Permissoes = permissoes.ToDictionary(p => p.Modulo, StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Resolve E exige a permissao. Sem ela, AcessoNegadoException -> HTTP 403.
    /// Este e o metodo que os endpoints chamam no lugar de RequireAdmin.
    /// </summary>
    public async Task<ContextoAdmin> ExigirAsync(string token, string modulo, AcaoPermissao acao)
    {
        var contexto = await ResolverAsync(token);
        if (contexto.Pode(modulo, acao)) return contexto;

        var rotulo = ModulosAdmin.Todos.FirstOrDefault(m => m.Chave == modulo)?.Rotulo ?? modulo;
        if (ModulosAdmin.EhExclusivoDoGeral(modulo))
            throw new AcessoNegadoException($"{rotulo} é uma área exclusiva do Administrador Geral.");
        throw new AcessoNegadoException($"Você não possui permissão para {Verbo(acao)} em {rotulo}.");
    }

    /// <summary>
    /// Garante que um funcionario so grave registro da propria unidade.
    /// Para administradores nao faz nada.
    /// </summary>
    public static void ExigirUnidade(ContextoAdmin contexto, string? unidade, string oQue)
    {
        if (contexto.VeUnidade(unidade)) return;
        throw new AcessoNegadoException(contexto.UnidadeDoFuncionario.Length == 0
            ? "Sua conta de funcionário não está vinculada a nenhuma unidade. Procure o Administrador Geral."
            : $"Você só pode operar {oQue} da unidade {contexto.UnidadeDoFuncionario}.");
    }

    /// <summary>
    /// Pets que um funcionario pode ver: os marcados com a unidade dele e os
    /// que tem agendamento nela (muitos pets cadastrados pelo cliente nascem
    /// sem unidade — o agendamento e que diz onde ele e atendido).
    /// Devolve null para administradores, que veem todos.
    /// </summary>
    public async Task<HashSet<string>?> PetsVisiveisAsync(ContextoAdmin contexto)
    {
        if (!contexto.EhFuncionario) return null;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (contexto.UnidadeDoFuncionario.Length == 0) return ids;

        foreach (var a in await db.Agendamentos.AsNoTracking().Select(a => new { a.PetId, a.Unidade }).ToListAsync())
            if (!string.IsNullOrWhiteSpace(a.PetId) && contexto.VeUnidade(a.Unidade)) ids.Add(a.PetId);
        foreach (var p in await db.Pets.AsNoTracking().Select(p => new { p.Id, p.Unidade }).ToListAsync())
            if (contexto.VeUnidade(p.Unidade)) ids.Add(p.Id);
        return ids;
    }

    /// <summary>Exige que quem chama seja Administrador Geral (regra 20).</summary>
    public async Task<ContextoAdmin> ExigirGeralAsync(string token)
    {
        var contexto = await ResolverAsync(token);
        if (!contexto.EhGeral)
            throw new AcessoNegadoException("Somente o Administrador Geral pode realizar esta operação.");
        return contexto;
    }

    private static string Verbo(AcaoPermissao acao) => acao switch
    {
        AcaoPermissao.Visualizar => "visualizar",
        AcaoPermissao.Criar => "criar",
        AcaoPermissao.Editar => "editar",
        AcaoPermissao.Excluir => "excluir",
        _ => "operar"
    };

    /// <summary>
    /// Permissoes do usuario no formato que o painel consome para montar o
    /// menu e habilitar botoes. O frontend usa isto para ESCONDER; o backend
    /// continua bloqueando por conta propria.
    /// </summary>
    public static object Mapear(ContextoAdmin contexto) => new
    {
        usuario = new
        {
            id = contexto.Usuario.Id,
            nome = string.IsNullOrWhiteSpace(contexto.Usuario.Nome) ? contexto.Usuario.Email : contexto.Usuario.Nome,
            email = contexto.Usuario.Email,
            perfil = contexto.Usuario.Perfil,
            perfilRotulo = PerfilAdmin.Rotulo(contexto.Usuario.Perfil),
            acessoTotal = contexto.AcessoIrrestrito,
            adminGeral = contexto.EhGeral,
            funcionario = contexto.EhFuncionario,
            unidade = contexto.UnidadeDoFuncionario,
            ultimoAcesso = contexto.Usuario.UltimoAcesso?.ToString("O")
        },
        acessoIrrestrito = contexto.AcessoIrrestrito,
        modulos = ModulosAdmin.Todos.ToDictionary(
            m => m.Chave,
            m => (object)new
            {
                visualizar = contexto.Pode(m.Chave, AcaoPermissao.Visualizar),
                criar = contexto.Pode(m.Chave, AcaoPermissao.Criar),
                editar = contexto.Pode(m.Chave, AcaoPermissao.Editar),
                excluir = contexto.Pode(m.Chave, AcaoPermissao.Excluir),
                somenteGeral = m.SomenteGeral
            })
    };

    /// <summary>Registra uma acao administrativa sensivel (regra 21).</summary>
    public async Task RegistrarAsync(ContextoAdmin autor, string acao, UsuarioAdministrador? alvo, string detalhes)
    {
        db.AuditoriasAdmin.Add(new AuditoriaAdmin
        {
            Id = Guid.NewGuid().ToString("N"),
            DataHora = DateTime.UtcNow,
            AutorId = autor.Usuario.Id,
            AutorEmail = autor.Usuario.Email,
            Acao = acao,
            AlvoId = alvo?.Id ?? "",
            AlvoEmail = alvo?.Email ?? "",
            Detalhes = detalhes
        });
        await db.SaveChangesAsync();

        // Item 15: a mesma acao aparece no log de eventos, para existir um
        // lugar so de consulta. A AuditoriaAdmin continua sendo gravada.
        await eventos.RegistrarAsync(new("administracao", acao, acao.StartsWith("Excluiu") || acao.StartsWith("Desativou") ? "aviso" : "info",
            "admin", autor.Usuario.Id, autor.Usuario.Email, alvo?.Id ?? "", $"{(alvo is null ? "" : alvo.Email + ": ")}{detalhes}"));
    }
}
