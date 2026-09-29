using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Tarefa 1, etapa 3 (28/09): primeiro acesso e vinculacao da conta Google. So recebe identidade JA conferida
/// pelo <see cref="GoogleTokenService"/>. Decisoes do Fabricio (28/09):
///   - e-mail que JA tem conta com senha: pede a senha UMA vez para vincular (protege contra quem cadastrou o
///     e-mail de outra pessoa antes); depois entra so com o Google;
///   - cliente novo: conta criada direto com nome e e-mail do Google, SEM senha e SEM pet — completa telefone
///     e o primeiro pet depois, na Minha Conta (agendar continua exigindo pet).
/// A conta e achada primeiro pelo "sub" (id fixo da conta Google): se a pessoa trocar o e-mail no Google, continua
/// entrando na mesma conta LanePets, com o e-mail de acesso de sempre.
/// Sessao, eventos de sucesso e tempo real ficam no controller (etapa 4); recusas viram evento por `aoRecusar`.
/// </summary>
public static class ContaGoogleService
{
    public enum Situacao { Entrou, ContaCriada, PrecisaVincular }

    public sealed record Resultado(Situacao Situacao, Cliente? Cliente, UsuarioCliente? Usuario, string Email);

    /// <summary>Mensagem unica de recusa: nao revela qual conta existe nem por que falhou (motivo so no Log).</summary>
    public const string MensagemRecusa = "Não foi possível entrar com o Google. Entre com e-mail e senha ou fale com a loja.";

    private static string NovoId(string prefixo) => prefixo + "-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    /// <summary>
    /// Entra (conta ja vinculada), cria a conta (e-mail novo) ou devolve PrecisaVincular (e-mail com conta de senha,
    /// ainda sem Google) — neste ultimo caso NADA e gravado.
    /// </summary>
    public static async Task<Resultado> EntrarAsync(LanePetsDbContext db, GoogleIdentidade g, Func<ContaClienteService.Recusa, Task> aoRecusar)
    {
        var porSub = await db.UsuariosClientes.FirstOrDefaultAsync(u => u.GoogleSub == g.Sub);
        if (porSub is not null)
        {
            var cliente = await db.Clientes.FindAsync(porSub.ClienteId);
            if (cliente is null) await Recusar(aoRecusar, porSub.ClienteId, g.Email, "Conta Google vinculada a um cadastro de cliente que não existe mais.");
            return new(Situacao.Entrou, cliente, porSub, porSub.Email);
        }

        var porEmail = await db.UsuariosClientes.FirstOrDefaultAsync(u => u.Email == g.Email);
        if (porEmail is not null)
        {
            if (porEmail.GoogleSub.Length > 0)
                await Recusar(aoRecusar, porEmail.ClienteId, g.Email, "E-mail já vinculado a OUTRA conta Google.");
            if (porEmail.SenhaHash.Length == 0)
                await Recusar(aoRecusar, porEmail.ClienteId, g.Email, "Conta sem senha e sem Google (estado inesperado).");
            return new(Situacao.PrecisaVincular, null, null, porEmail.Email);
        }

        var nome = NomeDoGoogle(g);
        var novo = new Cliente { Id = NovoId("CLI"), Nome = nome, Telefone = "", Endereco = "", Origem = "google", Status = "ativo" };
        var usuario = new UsuarioCliente { Id = NovoId("USR"), ClienteId = novo.Id, Email = g.Email, SenhaHash = "", SenhaSalt = "", GoogleSub = g.Sub };
        db.Clientes.Add(novo);
        db.UsuariosClientes.Add(usuario);
        await db.SaveChangesAsync();
        return new(Situacao.ContaCriada, novo, usuario, g.Email);
    }

    /// <summary>
    /// Vincula o Google a uma conta que ja existe com senha: exige a senha dessa conta (uma vez so).
    /// Senha errada = recusa com a mensagem do login normal.
    /// </summary>
    public static async Task<Resultado> VincularAsync(LanePetsDbContext db, GoogleIdentidade g, string? senha, Func<ContaClienteService.Recusa, Task> aoRecusar)
    {
        if (await db.UsuariosClientes.AnyAsync(u => u.GoogleSub == g.Sub))
            await Recusar(aoRecusar, "", g.Email, "Conta Google já vinculada a outro cadastro.");

        var usuario = await db.UsuariosClientes.FirstOrDefaultAsync(u => u.Email == g.Email);
        if (usuario is null || usuario.GoogleSub.Length > 0 || usuario.SenhaHash.Length == 0)
            await Recusar(aoRecusar, usuario?.ClienteId ?? "", g.Email, "Vinculação sem conta de senha disponível para este e-mail.");

        if (!SessionService.VerifyPassword(senha ?? "", usuario!.SenhaHash, usuario.SenhaSalt))
        {
            await aoRecusar(new("autenticacao", "Vinculação do Google recusada", usuario.ClienteId, g.Email, "", "Senha incorreta."));
            throw new Exception("Senha incorreta. Use a senha da sua conta LanePets.");
        }

        var cliente = await db.Clientes.FindAsync(usuario.ClienteId);
        if (cliente is null) await Recusar(aoRecusar, usuario.ClienteId, g.Email, "Cadastro de cliente não localizado.");

        usuario.GoogleSub = g.Sub;
        await db.SaveChangesAsync();
        return new(Situacao.Entrou, cliente, usuario, usuario.Email);
    }

    /// <summary>Nome do Google; sem nome (ou curto demais), a parte do e-mail antes do @; em ultimo caso "Cliente".</summary>
    public static string NomeDoGoogle(GoogleIdentidade g)
    {
        var nome = string.Join(' ', Validacao.TirarMarcacao(g.Nome).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (nome.Length >= 3) return nome.Length > 120 ? nome[..120] : nome;
        var parte = Validacao.TirarMarcacao(g.Email.Split('@')[0]);
        return parte.Length >= 3 ? parte : "Cliente";
    }

    private static async Task Recusar(Func<ContaClienteService.Recusa, Task> aoRecusar, string clienteId, string email, string motivo)
    {
        await aoRecusar(new("autenticacao", "Login com Google recusado", clienteId, email, "", motivo));
        throw new Exception(MensagemRecusa);
    }
}
