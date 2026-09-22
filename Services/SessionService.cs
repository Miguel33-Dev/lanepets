using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace LanePets.Services;

public record Session(string Profile, DateTime CreatedAt, DateTime ExpiresAt, string? AdminToken = null);

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

    public string CreateAdmin()
    {
        var token = Guid.NewGuid().ToString(); var now = DateTime.UtcNow;
        _sessions[token] = new Session("admin", now, now.AddMinutes(SessionMinutes)); return token;
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
    public static (string Hash, string Salt) HashPassword(string password) { var salt = RandomNumberGenerator.GetBytes(16); var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 120000, HashAlgorithmName.SHA256, 32); return (Convert.ToBase64String(hash), Convert.ToBase64String(salt)); }
    public static bool VerifyPassword(string password, string hash, string salt) { var calculated = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), 120000, HashAlgorithmName.SHA256, 32); return CryptographicOperations.FixedTimeEquals(calculated, Convert.FromBase64String(hash)); }
}
