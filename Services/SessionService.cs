using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace LanePets.Services;

// UsuarioId liga a sessao administrativa ao registro em
// UsuariosAdministradores. Sem isso o backend saberia que existe UM admin
// logado, mas nao QUAL — e sem saber qual, nao ha como aplicar permissao.
// AdminToken continua com o significado antigo (sessao financeira aponta para
// a sessao admin que a autorizou; sessao de cliente guarda o ClienteId).
public record Session(string Profile, DateTime CreatedAt, DateTime ExpiresAt, string? AdminToken = null, string UsuarioId = "");

public class SessionService(IConfiguration config)
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, Session> _financial = new();
    private readonly ConcurrentDictionary<string, Session> _clients = new();
    private int SessionMinutes => config.GetValue("LanePets:SessionMinutes", 120);
    private int FinancialMinutes => config.GetValue("LanePets:FinancialSessionMinutes", 30);

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public bool ValidateAdmin(string password) => Hash(password) == Hash(config["LanePets:AdminPassword"] ?? "");
    public bool ValidateFinance(string password) => Hash(password) == Hash(config["LanePets:FinancePassword"] ?? "");

    public string CreateAdmin(string usuarioId)
    {
        var token = Guid.NewGuid().ToString(); var now = DateTime.UtcNow;
        _sessions[token] = new Session("admin", now, now.AddMinutes(SessionMinutes), null, usuarioId); return token;
    }

    /// <summary>
    /// Derruba todas as sessoes de um administrador. Usado quando o
    /// Administrador Geral desativa a conta ou redefine a senha: o usuario
    /// perde o painel na hora, sem esperar a sessao expirar.
    /// </summary>
    public void EncerrarSessoesDoUsuario(string usuarioId)
    {
        foreach (var par in _sessions.Where(x => x.Value.UsuarioId == usuarioId).ToList())
        {
            _sessions.TryRemove(par.Key, out _);
            foreach (var fin in _financial.Where(f => f.Value.AdminToken == par.Key).ToList())
                _financial.TryRemove(fin.Key, out _);
        }
    }
    public Session RequireAdmin(string token)
    {
        if (!_sessions.TryGetValue(token, out var s) || s.ExpiresAt <= DateTime.UtcNow) { _sessions.TryRemove(token, out _); throw new UnauthorizedAccessException("Sessão inválida ou expirada."); }
        if (s.Profile != "admin") throw new UnauthorizedAccessException("Acesso negado. Requer perfil administrador.");
        return s;
    }
    public void Logout(string token) => _sessions.TryRemove(token, out _);
    public string CreateFinancial(string adminToken)
    {
        RequireAdmin(adminToken); var token = Guid.NewGuid().ToString(); var now = DateTime.UtcNow;
        _financial[token] = new Session("financeiro", now, now.AddMinutes(FinancialMinutes), adminToken); return token;
    }
    public Session RequireFinancial(string token)
    {
        if (!_financial.TryGetValue(token, out var s) || s.ExpiresAt <= DateTime.UtcNow) { _financial.TryRemove(token, out _); throw new UnauthorizedAccessException("Autorização financeira inválida ou expirada."); }
        RequireAdmin(s.AdminToken ?? ""); return s;
    }
    public void LogoutFinancial(string token) => _financial.TryRemove(token, out _);
    public int RemainingMinutes(Session s) => Math.Max(0, (int)Math.Ceiling((s.ExpiresAt - DateTime.UtcNow).TotalMinutes));
    public string CreateClient(string clienteId) { var token = Guid.NewGuid().ToString(); var now = DateTime.UtcNow; _clients[token] = new Session("cliente", now, now.AddDays(7), clienteId); return token; }
    public Session RequireClient(string token) { if (!_clients.TryGetValue(token, out var s) || s.ExpiresAt <= DateTime.UtcNow) { _clients.TryRemove(token, out _); throw new UnauthorizedAccessException("Sessão de cliente inválida ou expirada."); } return s; }
    public void LogoutClient(string token) => _clients.TryRemove(token, out _);

    // -----------------------------------------------------------------------
    // Senhas: novos cadastros usam BCrypt. A verificacao continua aceitando os
    // hashes PBKDF2 gravados pelas versoes anteriores, para nao invalidar as
    // contas que ja existem no lanepets.db.
    // -----------------------------------------------------------------------
    private const string BCryptSalt = "bcrypt";

    public static (string Hash, string Salt) HashPassword(string password)
        => (BCrypt.Net.BCrypt.HashPassword(password), BCryptSalt);

    public static bool VerifyPassword(string password, string hash, string salt)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        if (hash.StartsWith("$2", StringComparison.Ordinal))
        {
            try { return BCrypt.Net.BCrypt.Verify(password, hash); }
            catch { return false; }
        }

        try
        {
            var calculated = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), 120000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(calculated, Convert.FromBase64String(hash));
        }
        catch
        {
            return false;
        }
    }
}
