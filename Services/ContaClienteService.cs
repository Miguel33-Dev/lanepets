using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Item 11.4b (26/09): conta do cliente na area do cliente — cadastro, login, perfil,
/// troca de e-mail e de senha. Estava no ClientPortalController; veio para ca sem mudar
/// regra nem mensagem.
///
/// Regras que valem aqui (CONTEXTO §6.5, §6.20, §6.23):
///   - validacao pela regra compartilhada (Validacao: e-mail, telefone, senha 8+ com letra e numero);
///   - trocar e-mail ou senha exige a SENHA ATUAL, mesmo com a sessao valida;
///   - o login responde sempre "E-mail ou senha inválidos." (nao revela se o e-mail existe);
///     o motivo real vai so para o Log de eventos.
///
/// Sessao (token), evento de sucesso e aviso em tempo real ficam no controller. As RECUSAS
/// precisam virar evento antes da excecao subir; para isso o controller passa `aoRecusar`,
/// chamado aqui um instante antes do throw.
/// </summary>
public static class ContaClienteService
{
    /// <summary>Recusa que vira evento de nivel "aviso" no Log (ex.: senha incorreta).</summary>
    public sealed record Recusa(string Categoria, string Acao, string ClienteId, string Autor, string AlvoId, string Detalhes);

    public sealed record DadosCadastro(string? Nome, string? Email, string? Senha, string? Telefone, string? Endereco,
        string? Pet, string? Tipo, string? Raca);

    private static string NovoId(string prefixo) => prefixo + "-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    /// <summary>
    /// Cria cliente + primeiro pet + acesso (UsuarioCliente, senha em BCrypt) num SaveChanges so.
    /// Devolve o cliente, o pet e o e-mail ja normalizado.
    /// </summary>
    public static async Task<(Cliente Cliente, Pet Pet, string Email)> CadastrarAsync(LanePetsDbContext db, DadosCadastro d)
    {
        // Item 13 do roadmap (24/09): cada campo validado pela regra
        // compartilhada (Services/Validacao.cs), com mensagem propria.
        var nome = (d.Nome ?? "").Trim();
        if (nome.Length < 3) throw new Exception("Informe seu nome completo (mínimo 3 letras).");
        var email = Validacao.Email(d.Email);
        Validacao.Senha(d.Senha);
        var telefone = Validacao.Telefone(d.Telefone);
        var nomePet = (d.Pet ?? "").Trim();
        if (nomePet.Length < 2) throw new Exception("Informe o nome do pet (mínimo 2 letras).");
        if (await db.UsuariosClientes.AnyAsync(u => u.Email == email)) throw new Exception("Já existe uma conta com este e-mail.");

        var cliente = new Cliente { Id = NovoId("CLI"), Nome = nome, Telefone = telefone, Endereco = (d.Endereco ?? "").Trim(), Origem = "portal_cliente", Status = "ativo" };
        var pet = new Pet { Id = NovoId("PET"), ClienteId = cliente.Id, Dono = cliente.Nome, PetNome = nomePet, Tipo = (d.Tipo ?? "Não informado").Trim(), Raca = (d.Raca ?? "Não informada").Trim(), Telefone = cliente.Telefone, Endereco = cliente.Endereco };
        var (hash, salt) = SessionService.HashPassword(d.Senha!);
        db.Clientes.Add(cliente);
        db.Pets.Add(pet);
        db.UsuariosClientes.Add(new UsuarioCliente { Id = NovoId("USR"), ClienteId = cliente.Id, Email = email, SenhaHash = hash, SenhaSalt = salt });
        await db.SaveChangesAsync();
        return (cliente, pet, email);
    }

    /// <summary>
    /// Confere e-mail e senha. Recusa (e-mail inexistente ou senha errada) vira evento com o
    /// motivo real e a excecao sobe com a mensagem generica.
    /// </summary>
    public static async Task<(Cliente Cliente, UsuarioCliente Usuario)> AutenticarAsync(LanePetsDbContext db, string? emailInformado, string? senha, Func<Recusa, Task> aoRecusar)
    {
        var email = (emailInformado ?? "").Trim().ToLowerInvariant();
        var user = await db.UsuariosClientes.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            await aoRecusar(new("autenticacao", "Login de cliente recusado", "", email, "", "E-mail não cadastrado."));
            throw new Exception("E-mail ou senha inválidos.");
        }
        if (!SessionService.VerifyPassword(senha ?? "", user.SenhaHash, user.SenhaSalt))
        {
            await aoRecusar(new("autenticacao", "Login de cliente recusado", user.ClienteId, email, "", "Senha incorreta."));
            throw new Exception("E-mail ou senha inválidos.");
        }
        var cliente = await db.Clientes.FindAsync(user.ClienteId) ?? throw new Exception("Cadastro de cliente não localizado.");
        return (cliente, user);
    }

    /// <summary>
    /// Atualiza nome, telefone e endereco do proprio cliente. O nome e o telefone do dono
    /// tambem aparecem nos pets dele, que acompanham. Devolve o cliente e o e-mail de acesso.
    /// </summary>
    public static async Task<(Cliente Cliente, string Email)> AtualizarPerfilAsync(LanePetsDbContext db, string clienteId, string? nomeInformado, string? telefoneInformado, string? endereco)
    {
        var cliente = await db.Clientes.FindAsync(clienteId) ?? throw new Exception("Cadastro nao localizado.");
        var nome = (nomeInformado ?? "").Trim();
        if (nome.Length < 3) throw new Exception("Informe seu nome completo (minimo 3 letras).");
        var telefone = Validacao.Telefone(telefoneInformado);

        cliente.Nome = nome;
        cliente.Telefone = telefone;
        cliente.Endereco = (endereco ?? "").Trim();

        foreach (var pet in await db.Pets.Where(p => p.ClienteId == cliente.Id).ToListAsync())
        {
            pet.Dono = cliente.Nome;
            pet.Telefone = cliente.Telefone;
        }
        await db.SaveChangesAsync();

        var usuario = await db.UsuariosClientes.AsNoTracking().FirstOrDefaultAsync(u => u.ClienteId == cliente.Id);
        return (cliente, usuario?.Email ?? "");
    }

    /// <summary>Troca o e-mail de acesso (chave do login, unico). Exige a senha atual. Devolve (antigo, novo).</summary>
    public static async Task<(string Antigo, string Novo)> AlterarEmailAsync(LanePetsDbContext db, Cliente cliente, string? novoEmail, string? senhaAtual, Func<Recusa, Task> aoRecusar)
    {
        var usuario = await db.UsuariosClientes.FirstOrDefaultAsync(u => u.ClienteId == cliente.Id)
                      ?? throw new Exception("Conta de acesso não localizada.");

        var novo = Validacao.Email(novoEmail);
        if (novo == usuario.Email) throw new Exception("Este já é o e-mail da sua conta.");
        if (!SessionService.VerifyPassword(senhaAtual ?? "", usuario.SenhaHash, usuario.SenhaSalt))
        {
            await aoRecusar(new("seguranca", "Troca de e-mail recusada", cliente.Id, usuario.Email, cliente.Id, "Senha atual incorreta."));
            throw new Exception("Senha atual incorreta.");
        }
        var antigo = usuario.Email;
        if (await db.UsuariosClientes.AnyAsync(u => u.Email == novo && u.Id != usuario.Id))
            throw new Exception("Já existe uma conta com este e-mail.");

        usuario.Email = novo;
        await db.SaveChangesAsync();
        return (antigo, novo);
    }

    /// <summary>
    /// Troca a senha (BCrypt; senha antiga em PBKDF2 vira BCrypt aqui). Exige a senha atual,
    /// segue a regra de senha nova e nao aceita repetir a atual. Devolve o e-mail de acesso.
    /// Derrubar as outras sessoes e papel do controller (SessionService).
    /// </summary>
    public static async Task<string> AlterarSenhaAsync(LanePetsDbContext db, Cliente cliente, string? senhaAtual, string? novaSenha, string? confirmarSenha, Func<Recusa, Task> aoRecusar)
    {
        var usuario = await db.UsuariosClientes.FirstOrDefaultAsync(u => u.ClienteId == cliente.Id)
                      ?? throw new Exception("Conta de acesso não localizada.");

        var nova = novaSenha ?? "";
        if (!SessionService.VerifyPassword(senhaAtual ?? "", usuario.SenhaHash, usuario.SenhaSalt))
        {
            await aoRecusar(new("seguranca", "Troca de senha recusada", cliente.Id, usuario.Email, cliente.Id, "Senha atual incorreta."));
            throw new Exception("Senha atual incorreta.");
        }
        // Mesma regra do cadastro (Validacao.Senha: 8+, letra e numero).
        Validacao.Senha(nova, confirmarSenha ?? "");
        if (SessionService.VerifyPassword(nova, usuario.SenhaHash, usuario.SenhaSalt))
            throw new Exception("A nova senha precisa ser diferente da atual.");

        var (hash, salt) = SessionService.HashPassword(nova);
        usuario.SenhaHash = hash;
        usuario.SenhaSalt = salt;
        await db.SaveChangesAsync();
        return usuario.Email;
    }
}
