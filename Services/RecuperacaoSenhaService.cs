using System.Security.Cryptography;
using System.Text;
using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// 28/09: "Esqueci minha senha" da area do cliente e (29/09) do painel administrativo. Regras (as mesmas nos dois):
/// - A resposta ao pedido e SEMPRE a mesma (<see cref="MensagemPedido"/>): nao revela se o e-mail existe (§6.5).
/// - Codigo de 6 digitos, gerado com RandomNumberGenerator, valido por <see cref="MinutosValidade"/> minutos,
///   de uso unico; so o hash SHA-256(Id + ":" + codigo) vai para o banco (§6.42).
/// - Ate <see cref="MaxTentativas"/> tentativas por codigo; ao estourar, o codigo morre.
/// - Ate <see cref="MaxPedidosPorHora"/> pedidos por conta por hora (os excedentes nao geram codigo).
/// - Pedido novo invalida os codigos anteriores ainda abertos.
/// - Redefinir exige a regra de senha de sempre (Validacao.Senha) e derruba as sessoes da conta (quem chama).
/// - Painel: conta desativada nao recebe codigo nem redefine (mesma resposta; motivo so no Log).
/// A tabela RecuperacoesSenha e a mesma: linha do cliente tem UsuarioClienteId, linha do painel tem
/// UsuarioAdminId (a outra coluna fica vazia, entao uma consulta nunca enxerga a linha da outra).
/// Classe static (§6.34): regra pura sobre o DbContext; envio de e-mail e eventos ficam no controller.
/// </summary>
public static class RecuperacaoSenhaService
{
    public const int MinutosValidade = 15;
    public const int MaxTentativas = 5;
    public const int MaxPedidosPorHora = 3;
    public const string MensagemPedido = "Se este e-mail estiver cadastrado, enviamos um código de 6 dígitos para redefinir a senha. Ele vale por 15 minutos.";
    public const string CodigoInvalido = "Código inválido ou expirado. Confira o código ou peça um novo.";

    public sealed record Pedido(UsuarioCliente? Usuario, string? Codigo, string Motivo);
    public sealed record PedidoAdmin(UsuarioAdministrador? Usuario, string? Codigo, string Motivo);

    public static string Hash(string id, string codigo)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id + ":" + codigo.Trim())));

    private static bool Confere(string id, string codigo, string hash)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(id, codigo)), Encoding.ASCII.GetBytes(hash));

    // ------------------------------------------------------------------ nucleo comum

    /// <summary>Gera o codigo (ou null se estourou o limite por hora), invalidando os abertos.</summary>
    private static async Task<string?> GerarAsync(LanePetsDbContext db, List<RecuperacaoSenha> daConta, DateTime agora, Action<RecuperacaoSenha> dono)
    {
        if (daConta.Count(r => r.CriadoEm > agora.AddHours(-1)) >= MaxPedidosPorHora) return null;
        foreach (var aberto in daConta.Where(r => r.UsadoEm is null && r.ExpiraEm > agora)) aberto.ExpiraEm = agora;

        var codigo = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var id = "REC-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var rec = new RecuperacaoSenha { Id = id, CodigoHash = Hash(id, codigo), CriadoEm = agora, ExpiraEm = agora.AddMinutes(MinutosValidade) };
        dono(rec);
        db.RecuperacoesSenha.Add(rec);
        await db.SaveChangesAsync();
        return codigo;
    }

    /// <summary>Formato do codigo e regra da senha — antes de tocar no banco (senha fraca nao gasta tentativa).</summary>
    private static string ValidarEntrada(string? codigo, string? novaSenha, string? confirmarSenha)
    {
        var digitos = new string((codigo ?? "").Where(char.IsDigit).ToArray());
        if (digitos.Length != 6) throw new Validacao.ValidacaoException("Informe o código de 6 dígitos enviado por e-mail.");
        Validacao.Senha(novaSenha, confirmarSenha ?? "");
        return digitos;
    }

    private static RecuperacaoSenha? Aberto(IEnumerable<RecuperacaoSenha> daConta, DateTime agora)
        => daConta.Where(r => r.UsadoEm == null && r.ExpiraEm > agora && r.Tentativas < MaxTentativas)
                  .OrderByDescending(r => r.CriadoEm).FirstOrDefault();

    /// <summary>Confere o codigo; errado conta tentativa (e mata o codigo na ultima) e devolve o texto para o Log.</summary>
    private static async Task<string?> RecusaDoCodigoAsync(LanePetsDbContext db, RecuperacaoSenha? rec, string digitos, DateTime agora)
    {
        if (rec is null) return "Nenhum código válido em aberto.";
        if (Confere(rec.Id, digitos, rec.CodigoHash)) return null;
        rec.Tentativas++;
        if (rec.Tentativas >= MaxTentativas) rec.ExpiraEm = agora;
        await db.SaveChangesAsync();
        return $"Código incorreto (tentativa {rec.Tentativas} de {MaxTentativas}).";
    }

    // ------------------------------------------------------------------ area do cliente

    public static async Task<Pedido> SolicitarAsync(LanePetsDbContext db, string? email, DateTime agora)
    {
        var endereco = Validacao.Email(email);
        var usuario = await db.UsuariosClientes.AsNoTracking().FirstOrDefaultAsync(u => u.Email == endereco);
        if (usuario is null) return new Pedido(null, null, "E-mail não cadastrado.");

        var daConta = await db.RecuperacoesSenha.Where(r => r.UsuarioClienteId == usuario.Id).ToListAsync();
        var codigo = await GerarAsync(db, daConta, agora, r => { r.UsuarioClienteId = usuario.Id; r.ClienteId = usuario.ClienteId; });
        return codigo is null
            ? new Pedido(usuario, null, $"Limite de {MaxPedidosPorHora} pedidos por hora.")
            : new Pedido(usuario, codigo, "Código enviado.");
    }

    public static string TextoEmail(string nome, string codigo) =>
        $"""
        Olá, {(string.IsNullOrWhiteSpace(nome) ? "tutor(a)" : nome.Split(' ')[0])}!

        Recebemos um pedido para redefinir a senha da sua conta LanePets.

        Seu código: {codigo}

        Ele vale por {MinutosValidade} minutos e só pode ser usado uma vez. Digite-o na tela
        "Esqueci minha senha" da área do cliente e escolha a nova senha.

        Se não foi você, ignore este e-mail: sua senha continua a mesma.

        Equipe LanePets
        """;

    /// <summary>Troca a senha com o codigo. Devolve o usuario (para derrubar as sessoes e registrar o evento).</summary>
    public static async Task<UsuarioCliente> RedefinirAsync(LanePetsDbContext db, string? email, string? codigo, string? novaSenha, string? confirmarSenha,
        DateTime agora, Func<ContaClienteService.Recusa, Task> aoRecusar)
    {
        var endereco = Validacao.Email(email);
        var digitos = ValidarEntrada(codigo, novaSenha, confirmarSenha);

        var usuario = await db.UsuariosClientes.FirstOrDefaultAsync(u => u.Email == endereco) ?? throw new Exception(CodigoInvalido);
        var rec = Aberto(await db.RecuperacoesSenha.Where(r => r.UsuarioClienteId == usuario.Id && r.UsadoEm == null).ToListAsync(), agora);
        if (await RecusaDoCodigoAsync(db, rec, digitos, agora) is { } motivo)
        {
            await aoRecusar(new("seguranca", "Redefinição de senha recusada", usuario.ClienteId, usuario.Email, usuario.ClienteId, motivo));
            throw new Exception(CodigoInvalido);
        }

        var (hash, salt) = SessionService.HashPassword(novaSenha!);
        usuario.SenhaHash = hash;
        usuario.SenhaSalt = salt;
        rec!.UsadoEm = agora;
        await db.SaveChangesAsync();
        return usuario;
    }

    // ------------------------------------------------------------------ painel administrativo (29/09)

    public static async Task<PedidoAdmin> SolicitarAdminAsync(LanePetsDbContext db, string? email, DateTime agora)
    {
        var endereco = Validacao.Email(email);
        var admin = await db.UsuariosAdministradores.AsNoTracking().FirstOrDefaultAsync(u => u.Email == endereco);
        if (admin is null) return new PedidoAdmin(null, null, "E-mail não cadastrado.");
        if (!admin.Ativo) return new PedidoAdmin(admin, null, "Conta administrativa desativada.");

        var daConta = await db.RecuperacoesSenha.Where(r => r.UsuarioAdminId == admin.Id).ToListAsync();
        var codigo = await GerarAsync(db, daConta, agora, r => r.UsuarioAdminId = admin.Id);
        return codigo is null
            ? new PedidoAdmin(admin, null, $"Limite de {MaxPedidosPorHora} pedidos por hora.")
            : new PedidoAdmin(admin, codigo, "Código enviado.");
    }

    public static string TextoEmailAdmin(string nome, string codigo) =>
        $"""
        Olá, {(string.IsNullOrWhiteSpace(nome) ? "equipe" : nome.Split(' ')[0])}!

        Recebemos um pedido para redefinir a senha do seu acesso ao painel administrativo da LanePets.

        Seu código: {codigo}

        Ele vale por {MinutosValidade} minutos e só pode ser usado uma vez. Digite-o na tela
        "Esqueceu a senha?" do acesso administrativo e escolha a nova senha.

        Se não foi você, ignore este e-mail e avise o Administrador Geral: sua senha continua a mesma.

        Equipe LanePets
        """;

    /// <summary>Troca a senha do painel com o codigo. Devolve o administrador (para derrubar as sessoes e registrar o evento).</summary>
    public static async Task<UsuarioAdministrador> RedefinirAdminAsync(LanePetsDbContext db, string? email, string? codigo, string? novaSenha, string? confirmarSenha,
        DateTime agora, Func<UsuarioAdministrador, string, Task> aoRecusar)
    {
        var endereco = Validacao.Email(email);
        var digitos = ValidarEntrada(codigo, novaSenha, confirmarSenha);

        var admin = await db.UsuariosAdministradores.FirstOrDefaultAsync(u => u.Email == endereco) ?? throw new Exception(CodigoInvalido);
        var rec = Aberto(await db.RecuperacoesSenha.Where(r => r.UsuarioAdminId == admin.Id && r.UsadoEm == null).ToListAsync(), agora);
        var motivo = !admin.Ativo ? "Conta administrativa desativada." : await RecusaDoCodigoAsync(db, rec, digitos, agora);
        if (motivo is not null)
        {
            await aoRecusar(admin, motivo);
            throw new Exception(CodigoInvalido);
        }

        var (hash, salt) = SessionService.HashPassword(novaSenha!);
        admin.SenhaHash = hash;
        admin.SenhaSalt = salt;
        rec!.UsadoEm = agora;
        await db.SaveChangesAsync();
        return admin;
    }
}
