using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;

[Route("api")]
public class PublicController(LanePetsDbContext db, PermissaoService permissoes, RealtimeNotifier realtime) : ApiControllerBase
{
    [HttpGet("public/servicos")]
    public async Task<IActionResult> Servicos() => OkApi((await db.Servicos.AsNoTracking().OrderBy(s => s.Nome).ToListAsync()).Select(s => new { s.Id, s.Nome, s.Preco, s.Porte }));

    /// <summary>
    /// Catalogo publico. Alem do que ja era devolvido, informa a
    /// disponibilidade calculada a partir do MESMO estoque que o painel
    /// enxerga: produto com controle de estoque e saldo zerado deixa de
    /// aparecer como disponivel para compra.
    /// </summary>
    [HttpGet("public/produtos")]
    public async Task<IActionResult> Produtos() => OkApi((await db.Produtos.AsNoTracking().OrderBy(p => p.Nome).ToListAsync()).Select(p => new
    {
        p.Id,
        p.Nome,
        p.Categoria,
        p.ValorVenda,
        p.Estoque,
        p.ControlaEstoque,
        disponivel = !p.ControlaEstoque || p.Estoque > 0
    }));

    [HttpGet("public/depoimentos")]
    public async Task<IActionResult> Depoimentos() => OkApi((await db.Depoimentos.AsNoTracking().Where(d => d.Status == "Aprovado").OrderByDescending(d => d.CriadoEm).ToListAsync()).Select(d => new { d.Id, d.NomeCliente, d.NomePet, d.Avaliacao, d.Comentario, d.CriadoEm }));

    [HttpGet("public/seguros")]
    public async Task<IActionResult> Seguros() => OkApi(await db.PlanosSeguro.AsNoTracking().Where(p => p.Ativo).OrderBy(p => p.ValorMensal).ToListAsync());


    [HttpPost("public/depoimentos")]
    public async Task<IActionResult> CriarDepoimento([FromBody] DepoimentoRequest request)
    {
        try
        {
            var nome = (request.Nome ?? "").Trim(); var telefone = NormalizarTelefone(request.Telefone);
            if (nome.Length < 3 || telefone.Length < 8 || string.IsNullOrWhiteSpace(request.Comentario)) throw new Exception("Informe seu nome, telefone e comentário.");
            if (request.Avaliacao is < 1 or > 5) throw new Exception("A avaliação deve ter entre 1 e 5 estrelas.");
            var cliente = (await db.Clientes.AsNoTracking().ToListAsync()).FirstOrDefault(c => NormalizarTelefone(c.Telefone) == telefone && string.Equals(c.Nome, nome, StringComparison.OrdinalIgnoreCase));
            if (cliente is null) throw new Exception("Seu cadastro não foi localizado. Use os mesmos nome e telefone cadastrados no LanePets.");
            var pet = await db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.ClienteId == cliente.Id && p.PetNome.ToLower() == (request.Pet ?? "").Trim().ToLower());
            if (pet is null) throw new Exception("Pet não localizado para este cadastro.");
            db.Depoimentos.Add(new Depoimento { Id = "DEP-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), ClienteId = cliente.Id, NomeCliente = cliente.Nome, NomePet = pet.PetNome, Telefone = cliente.Telefone, Avaliacao = request.Avaliacao, Comentario = request.Comentario.Trim(), Status = "Pendente" });
            await db.SaveChangesAsync(); await realtime.NotificarAsync("avaliacoes", "criada"); return OkApi(new { message = "Obrigado! Seu comentário foi enviado para moderação." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpPost("public/seguros/solicitacoes")]
    public async Task<IActionResult> ContratarSeguro([FromBody] SeguroRequest request)
    {
        try
        {
            var plano = await db.PlanosSeguro.FirstOrDefaultAsync(p => p.Id == request.PlanoId && p.Ativo) ?? throw new Exception("Plano indisponível.");
            if (string.IsNullOrWhiteSpace(request.Nome) || NormalizarTelefone(request.Telefone).Length < 8 || string.IsNullOrWhiteSpace(request.Pet)) throw new Exception("Informe seu nome, telefone e nome do pet.");
            // Formulario publico: quem preenche pode nao ter conta. Quando o
            // cadastro e o pet sao localizados, o contrato ja nasce vinculado —
            // e assim ele aparece em "Meu Seguro" se essa pessoa tiver login.
            var telefonePublico = NormalizarTelefone(request.Telefone);
            var clientePublico = (await db.Clientes.AsNoTracking().ToListAsync())
                .FirstOrDefault(c => NormalizarTelefone(c.Telefone) == telefonePublico
                                     && string.Equals(c.Nome, request.Nome.Trim(), StringComparison.OrdinalIgnoreCase));
            var petPublico = clientePublico is null ? null : await db.Pets.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ClienteId == clientePublico.Id && p.PetNome.ToLower() == request.Pet.Trim().ToLower());

            db.SolicitacoesSeguro.Add(new SolicitacaoSeguro { Id = "SOL-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), PlanoSeguroId = plano.Id, NomePlano = plano.Nome, ClienteId = clientePublico?.Id ?? "", PetId = petPublico?.Id ?? "", NomeCliente = request.Nome.Trim(), Telefone = request.Telefone.Trim(), NomePet = request.Pet.Trim(), Observacao = (request.Observacao ?? "").Trim() });
            await db.SaveChangesAsync(); await realtime.NotificarAsync("seguros", "solicitado"); return OkApi(new { message = "Recebemos seu pedido. A equipe LanePets entrará em contato para concluir a contratação." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpGet("admin/depoimentos")]
    public async Task<IActionResult> ListarDepoimentos([FromQuery] string token = "") { try { await permissoes.ExigirAsync(token, ModulosAdmin.Avaliacoes, AcaoPermissao.Visualizar); return OkApi(await db.Depoimentos.AsNoTracking().OrderByDescending(d => d.CriadoEm).ToListAsync()); } catch (Exception ex) { return ErrorApi(ex); } }
    /// <summary>Contratos de seguro para o painel, com o cadastro do cliente e o
    /// pet resolvidos a partir dos ids gravados no contrato.</summary>
    [HttpGet("admin/seguros/solicitacoes")]
    public async Task<IActionResult> ListarSolicitacoes([FromQuery] string token = "")
    {
        try
        {
            await permissoes.ExigirAsync(token, ModulosAdmin.Seguros, AcaoPermissao.Visualizar);
            var contratos = await db.SolicitacoesSeguro.AsNoTracking().OrderByDescending(s => s.CriadoEm).ToListAsync();
            var clientes = await db.Clientes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c);
            var pets = await db.Pets.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p);
            var planos = await db.PlanosSeguro.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p);

            return OkApi(contratos.Select(s => new
            {
                s.Id,
                s.PlanoSeguroId,
                s.NomePlano,
                s.ClienteId,
                s.PetId,
                s.NomeCliente,
                s.Telefone,
                s.NomePet,
                s.Observacao,
                s.Status,
                CriadoEm = s.CriadoEm.ToString("O"),
                // Valor fechado na contratacao, forma de pagamento escolhida
                // pelo cliente e a data do cancelamento (nula enquanto o
                // contrato estiver valendo). Nada disso e calculado na tela.
                s.Valor,
                s.MetodoPagamento,
                s.PagamentoStatus,
                s.CartaoFinal,
                DataCancelamento = s.DataCancelamento.HasValue ? s.DataCancelamento.Value.ToString("O") : null,
                // "true" quando o contrato nasceu de uma conta autenticada.
                temCadastro = s.ClienteId.Length > 0 && clientes.ContainsKey(s.ClienteId),
                clienteAtual = clientes.TryGetValue(s.ClienteId, out var c) ? c.Nome : "",
                telefoneAtual = clientes.TryGetValue(s.ClienteId, out var c2) ? c2.Telefone : "",
                petAtual = pets.TryGetValue(s.PetId, out var pt) ? pt.PetNome : "",
                valorMensal = planos.TryGetValue(s.PlanoSeguroId, out var pl) ? pl.ValorMensal : 0m
            }));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
    [HttpGet("admin/seguros")]
    public async Task<IActionResult> ListarPlanos([FromQuery] string token = "") { try { await permissoes.ExigirAsync(token, ModulosAdmin.Seguros, AcaoPermissao.Visualizar); return OkApi(await db.PlanosSeguro.AsNoTracking().OrderBy(p => p.ValorMensal).ToListAsync()); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpPost("admin/seguros")]
    public async Task<IActionResult> CriarPlano([FromBody] PlanoRequest request) { try { await permissoes.ExigirAsync(request.Token ?? "", ModulosAdmin.Seguros, AcaoPermissao.Criar); if (string.IsNullOrWhiteSpace(request.Nome) || request.ValorMensal <= 0) throw new Exception("Informe nome e valor do plano."); var plano = new PlanoSeguro { Id = "SEG-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), Nome = request.Nome.Trim(), Descricao = (request.Descricao ?? "").Trim(), Coberturas = (request.Coberturas ?? "").Trim(), Beneficios = (request.Beneficios ?? "").Trim(), Condicoes = (request.Condicoes ?? "").Trim(), ValorMensal = request.ValorMensal, Ativo = true }; db.PlanosSeguro.Add(plano); await db.SaveChangesAsync(); await realtime.NotificarAsync("seguros", "plano_criado", new { plano.Id, plano.Nome }); return OkApi(plano); } catch (Exception ex) { return ErrorApi(ex); } }
    /// <summary>Edita um plano do catalogo. O que muda aqui aparece na hora para
    /// o cliente, porque a area publica le a mesma tabela.</summary>
    [HttpPut("admin/seguros/{id}")]
    public async Task<IActionResult> EditarPlano(string id, [FromBody] PlanoRequest request)
    {
        try
        {
            await permissoes.ExigirAsync(request.Token ?? "", ModulosAdmin.Seguros, AcaoPermissao.Editar);
            var plano = await db.PlanosSeguro.FindAsync(id) ?? throw new Exception("Plano nao encontrado.");
            if (string.IsNullOrWhiteSpace(request.Nome) || request.ValorMensal <= 0) throw new Exception("Informe nome e valor do plano.");
            plano.Nome = request.Nome.Trim();
            plano.Descricao = (request.Descricao ?? "").Trim();
            plano.Coberturas = (request.Coberturas ?? "").Trim();
            plano.Beneficios = (request.Beneficios ?? "").Trim();
            plano.Condicoes = (request.Condicoes ?? "").Trim();
            plano.ValorMensal = request.ValorMensal;
            await db.SaveChangesAsync();
            await realtime.NotificarAsync("seguros", "plano_alterado", new { plano.Id, plano.Nome });
            return OkApi(plano);
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>
    /// Exclui um plano — somente enquanto ninguem o contratou. Com contrato
    /// associado, apagar o plano deixaria o contrato do cliente apontando para
    /// o vazio; nesse caso o caminho e desativar, que tira o plano do site e
    /// preserva o historico.
    /// </summary>
    [HttpDelete("admin/seguros/{id}")]
    public async Task<IActionResult> ExcluirPlano(string id, [FromQuery] string token = "")
    {
        try
        {
            await permissoes.ExigirAsync(token, ModulosAdmin.Seguros, AcaoPermissao.Excluir);
            var plano = await db.PlanosSeguro.FindAsync(id) ?? throw new Exception("Plano nao encontrado.");
            var contratos = await db.SolicitacoesSeguro.CountAsync(s => s.PlanoSeguroId == id);
            if (contratos > 0) throw new Exception($"Este plano tem {contratos} contrato(s) e nao pode ser excluido. Desative-o para tira-lo do site sem perder o historico.");
            db.PlanosSeguro.Remove(plano);
            await db.SaveChangesAsync();
            await realtime.NotificarAsync("seguros", "plano_excluido", new { id });
            return OkApi(new { id, message = "Plano excluido." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpPost("admin/seguros/{id}/ativo")]
    public async Task<IActionResult> AtualizarPlano(string id, [FromBody] PlanoStatusRequest request) { try { await permissoes.ExigirAsync(request.Token ?? "", ModulosAdmin.Seguros, AcaoPermissao.Editar); var plano = await db.PlanosSeguro.FindAsync(id) ?? throw new Exception("Plano não encontrado."); plano.Ativo = request.Ativo; await db.SaveChangesAsync(); return OkApi(plano); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpPost("admin/depoimentos/{id}/status")]
    public async Task<IActionResult> Moderar(string id, [FromBody] StatusRequest request) { try { await permissoes.ExigirAsync(request.Token ?? "", ModulosAdmin.Avaliacoes, AcaoPermissao.Editar); var item = await db.Depoimentos.FindAsync(id) ?? throw new Exception("Depoimento não encontrado."); item.Status = request.Status is "Aprovado" or "Recusado" ? request.Status : throw new Exception("Status inválido."); await db.SaveChangesAsync(); return OkApi(item); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpPost("admin/solicitacoes/{id}/status")]
    public async Task<IActionResult> AtualizarSolicitacao(string id, [FromBody] StatusRequest request) { try { await permissoes.ExigirAsync(request.Token ?? "", ModulosAdmin.Seguros, AcaoPermissao.Editar); var item = await db.SolicitacoesSeguro.FindAsync(id) ?? throw new Exception("Solicitação não encontrada."); item.Status = request.Status is "Pendente" or "Em contato" or "Concluída" or "Cancelada" ? request.Status : throw new Exception("Status inválido."); await db.SaveChangesAsync(); return OkApi(item); } catch (Exception ex) { return ErrorApi(ex); } }
    private static string NormalizarTelefone(string? value) => new string((value ?? "").Where(char.IsDigit).ToArray());
    public record DepoimentoRequest(string? Nome, string? Telefone, string? Pet, int Avaliacao, string? Comentario);
    public record SeguroRequest(string PlanoId, string? Nome, string? Telefone, string? Pet, string? Observacao);
    public record StatusRequest(string? Token, string Status);
    public record PlanoRequest(string? Token, string? Nome, string? Descricao, string? Coberturas, string? Beneficios, string? Condicoes, decimal ValorMensal);
    public record PlanoStatusRequest(string? Token, bool Ativo);
}
