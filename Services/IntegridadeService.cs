using LanePets.Data;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Item 19 do roadmap (25/09): varredura de integridade do banco.
///
/// Como nao ha foreign key (decisao: nao recriar tabelas), esta varredura e
/// quem aponta vinculo quebrado e dado duplicado. SOMENTE LEITURA — decisao
/// do Fabricio: relatar, e a correcao e manual, registro por registro.
/// Cada verificacao devolve ate 20 exemplos para a equipe achar o registro.
/// </summary>
public static class IntegridadeService
{
    public const int Exemplos = 20;

    public record Exemplo(string Id, string Descricao);
    public record Verificacao(string Codigo, string Titulo, string Nivel, int Total, IReadOnlyList<Exemplo> Exemplos, string Dica);

    private static Verificacao V(string codigo, string titulo, string nivel, IEnumerable<Exemplo> itens, string dica)
    {
        var lista = itens.ToList();
        return new(codigo, titulo, lista.Count == 0 ? "ok" : nivel, lista.Count, lista.Take(Exemplos).ToList(), dica);
    }

    private static string Digitos(string? v) => new((v ?? "").Where(char.IsDigit).ToArray());

    public static async Task<List<Verificacao>> VerificarAsync(LanePetsDbContext db)
    {
        var clientes = await db.Clientes.AsNoTracking().ToListAsync();
        var pets = await db.Pets.AsNoTracking().ToListAsync();
        var ags = await db.Agendamentos.AsNoTracking().ToListAsync();
        var pedidos = await db.Pedidos.AsNoTracking().ToListAsync();
        var produtos = await db.Produtos.AsNoTracking().ToListAsync();
        var contas = await db.UsuariosClientes.AsNoTracking().ToListAsync();
        var admins = await db.UsuariosAdministradores.AsNoTracking().ToListAsync();
        var seguros = await db.SolicitacoesSeguro.AsNoTracking().ToListAsync();
        var pagamentos = await db.Pagamentos.AsNoTracking().ToListAsync();
        var movs = await db.MovimentacoesEstoque.AsNoTracking().ToListAsync();

        var idsClientes = clientes.Select(c => c.Id).ToHashSet();
        var idsPets = pets.Select(p => p.Id).ToHashSet();
        var idsProdutos = produtos.Select(p => p.Id).ToHashSet();
        var idsAdmins = admins.Select(a => a.Id).ToHashSet();
        var nomeCliente = clientes.ToDictionary(c => c.Id, c => c.Nome);
        string Cli(string id) => nomeCliente.TryGetValue(id, out var n) ? n : id;
        var r = new List<Verificacao>();

        // ------------------------------------------------ vinculos quebrados
        r.Add(V("pet-sem-cliente", "Pets ligados a um cliente que não existe", "erro",
            pets.Where(p => !string.IsNullOrEmpty(p.ClienteId) && !idsClientes.Contains(p.ClienteId!))
                .Select(p => new Exemplo(p.Id, $"{p.PetNome} · dono \"{p.Dono}\" · ClienteId {p.ClienteId}")),
            "O cliente foi excluído ou o vínculo foi gravado errado. Vincule o pet ao cliente certo no Cadastro."));

        r.Add(V("agendamento-sem-cliente", "Agendamentos ligados a um cliente que não existe", "erro",
            ags.Where(a => !string.IsNullOrEmpty(a.ClienteId) && !idsClientes.Contains(a.ClienteId))
                .Select(a => new Exemplo(a.Id, $"{a.Pet} · {a.Dono} · {a.DataHora} · ClienteId {a.ClienteId}")),
            "O agendamento não aparece na área de nenhum cliente. Confira o dono no painel de Agendamentos."));

        r.Add(V("agendamento-sem-pet", "Agendamentos ligados a um pet que não existe", "aviso",
            ags.Where(a => !string.IsNullOrEmpty(a.PetId) && !idsPets.Contains(a.PetId))
                .Select(a => new Exemplo(a.Id, $"{a.Pet} · {a.Dono} · {a.DataHora} · PetId {a.PetId}")),
            "O pet foi excluído depois do agendamento. O histórico continua, mas sem a ficha do pet."));

        r.Add(V("agendamento-responsavel", "Agendamentos com responsável que não existe mais", "aviso",
            ags.Where(a => !string.IsNullOrEmpty(a.ResponsavelId) && !idsAdmins.Contains(a.ResponsavelId))
                .Select(a => new Exemplo(a.Id, $"{a.Pet} · {a.DataHora} · ResponsavelId {a.ResponsavelId}")),
            "O funcionário foi excluído. Edite o agendamento e escolha outro responsável (ou deixe sem)."));

        r.Add(V("pedido-sem-cliente", "Pedidos ligados a um cliente que não existe", "erro",
            pedidos.Where(p => !idsClientes.Contains(p.ClienteId))
                .Select(p => new Exemplo(p.Id, $"{p.Quantidade}× {p.ProdutoNome} · ClienteId {p.ClienteId}")),
            "Pedido sem dono na área do cliente. Confira na tela de Pedidos."));

        r.Add(V("pedido-produto-removido", "Pedidos de produtos que foram excluídos do catálogo", "info",
            pedidos.Where(p => !idsProdutos.Contains(p.ProdutoId))
                .Select(p => new Exemplo(p.Id, $"{p.Quantidade}× {p.ProdutoNome} · {Cli(p.ClienteId)}")),
            "Normal quando um produto sai do catálogo: o pedido guarda o nome. Só informativo."));

        r.Add(V("conta-sem-cliente", "Contas de login de cliente sem cadastro de cliente", "erro",
            contas.Where(u => !idsClientes.Contains(u.ClienteId))
                .Select(u => new Exemplo(u.Id, $"{u.Email} · ClienteId {u.ClienteId}")),
            "Essa pessoa consegue entrar mas a área dela quebra. Recrie o cadastro do cliente ou remova a conta."));

        r.Add(V("seguro-sem-cliente", "Seguros contratados por cliente que não existe", "aviso",
            seguros.Where(s => !string.IsNullOrEmpty(s.ClienteId) && !idsClientes.Contains(s.ClienteId))
                .Select(s => new Exemplo(s.Id, $"{s.NomePlano} · {s.NomeCliente} · ClienteId {s.ClienteId}")),
            "Confira em Área pública > Seguros."));

        var origens = ags.Select(a => ("agendamento", a.Id)).Concat(pedidos.Select(p => ("pedido", p.Id)))
            .Concat(seguros.Select(s => ("seguro", s.Id))).ToHashSet();
        r.Add(V("pagamento-sem-origem", "Pagamentos cuja origem foi excluída", "info",
            pagamentos.Where(p => !origens.Contains((p.Origem, p.OrigemId)))
                .Select(p => new Exemplo(p.Id, $"{p.Origem} {p.OrigemId} · {p.Descricao} · {p.Status}")),
            "O pagamento fica guardado como histórico (Cancelado ou com reembolso pendente)."));

        // ------------------------------------------------ valores fora do padrao
        r.Add(V("agendamento-unidade", "Agendamentos com unidade não reconhecida", "aviso",
            ags.Where(a => !string.IsNullOrWhiteSpace(a.Unidade) && Normalizador.IdUnidade(a.Unidade) == "")
                .Select(a => new Exemplo(a.Id, $"{a.Pet} · {a.DataHora} · unidade \"{a.Unidade}\"")),
            "Não entram no filtro de nenhuma unidade. Edite o agendamento e escolha a unidade."));

        r.Add(V("agendamento-sem-unidade", "Agendamentos sem unidade", "info",
            ags.Where(a => string.IsNullOrWhiteSpace(a.Unidade))
                .Select(a => new Exemplo(a.Id, $"{a.Pet} · {a.Dono} · {a.DataHora}")),
            "Registros antigos, de antes das unidades. Aparecem como \"Sem unidade / antigos\"."));

        r.Add(V("agendamento-unidade-nome", "Agendamentos com a unidade gravada pelo nome (não pelo id)", "info",
            ags.Where(a => Normalizador.IdUnidade(a.Unidade) is { Length: > 0 } id && a.Unidade != id)
                .Select(a => new Exemplo(a.Id, $"{a.Pet} · {a.DataHora} · \"{a.Unidade}\" = {Normalizador.IdUnidade(a.Unidade)}")),
            "Funciona (a comparação é normalizada). A área do cliente grava assim; é só informativo."));

        r.Add(V("agendamento-status", "Agendamentos com status fora da lista oficial", "aviso",
            ags.Where(a => StatusAgendamento.Normalizar(a.Status) == "" || StatusAgendamento.Normalizar(a.Status) != a.Status)
                .Select(a => new Exemplo(a.Id, $"{a.Pet} · {a.DataHora} · status \"{a.Status}\"")),
            "A conversão da subida deveria ter tratado. Edite o status no painel de Agendamentos."));

        r.Add(V("agendamento-data", "Agendamentos com data/hora inválida", "erro",
            ags.Where(a => !DateTime.TryParse(a.DataHora, out _))
                .Select(a => new Exemplo(a.Id, $"{a.Pet} · {a.Dono} · \"{a.DataHora}\"")),
            "Não aparecem no calendário. Edite a data no painel."));

        // ------------------------------------------------ duplicados
        r.Add(V("conta-email-duplicado", "E-mail de login de cliente repetido", "erro",
            contas.GroupBy(u => (u.Email ?? "").Trim().ToLowerInvariant()).Where(g => g.Key.Length > 0 && g.Count() > 1)
                .Select(g => new Exemplo(g.Key, $"{g.Count()} contas: {string.Join(", ", g.Select(u => u.Id))}")),
            "Só uma das contas consegue entrar. Mantenha uma e remova as outras."));

        r.Add(V("admin-email-duplicado", "E-mail de administrador repetido", "erro",
            admins.GroupBy(u => (u.Email ?? "").Trim().ToLowerInvariant()).Where(g => g.Key.Length > 0 && g.Count() > 1)
                .Select(g => new Exemplo(g.Key, $"{g.Count()} usuários: {string.Join(", ", g.Select(u => u.Id))}")),
            "Corrija em Administração > Usuários Administrativos."));

        r.Add(V("cliente-telefone-duplicado", "Clientes diferentes com o mesmo telefone", "aviso",
            clientes.GroupBy(c => Digitos(c.Telefone)).Where(g => g.Key.Length >= 8 && g.Count() > 1)
                .Select(g => new Exemplo(g.Key, string.Join(" · ", g.Select(c => $"{c.Nome} ({c.Id})")))),
            "Pode ser a mesma pessoa cadastrada duas vezes (painel e área do cliente). Confira em Clientes."));

        r.Add(V("pet-duplicado", "Pets repetidos no mesmo cliente (mesmo nome)", "aviso",
            pets.Where(p => !string.IsNullOrEmpty(p.ClienteId))
                .GroupBy(p => (p.ClienteId, Normalizador.Texto(p.PetNome))).Where(g => g.Key.Item2.Length > 0 && g.Count() > 1)
                .Select(g => new Exemplo(g.First().Id, $"{g.First().PetNome} · {Cli(g.Key.ClienteId!)} · {g.Count()} fichas: {string.Join(", ", g.Select(p => p.Id))}")),
            "Pode ser cadastro repetido. Confira no Cadastro antes de remover (agendamentos apontam para um deles)."));

        r.Add(V("produto-codigo-duplicado", "Produtos com o mesmo código", "aviso",
            produtos.GroupBy(p => (p.Codigo ?? "").Trim().ToLowerInvariant()).Where(g => g.Key.Length > 0 && g.Count() > 1)
                .Select(g => new Exemplo(g.Key, string.Join(" · ", g.Select(p => $"{p.Nome} ({p.Id})")))),
            "Corrija o código em Produtos."));

        // ------------------------------------------------ estoque
        var ultimo = movs.GroupBy(m => m.ProdutoId).ToDictionary(g => g.Key, g => g.OrderBy(m => m.DataHora).Last());
        r.Add(V("estoque-divergente", "Saldo do produto diferente do último lançamento do livro de estoque", "aviso",
            produtos.Where(p => p.ControlaEstoque && ultimo.TryGetValue(p.Id, out var m) && m.SaldoNovo != p.Estoque)
                .Select(p => new Exemplo(p.Id, $"{p.Nome}: saldo {p.Estoque} · livro diz {ultimo[p.Id].SaldoNovo}")),
            "Alguém mudou o saldo por fora do livro (importação ou versão antiga). Faça um ajuste de inventário na tela de Produtos."));

        r.Add(V("estoque-negativo", "Produtos com estoque negativo", "erro",
            produtos.Where(p => p.Estoque < 0).Select(p => new Exemplo(p.Id, $"{p.Nome}: {p.Estoque}")),
            "Faça um ajuste de inventário na tela de Produtos."));

        return r;
    }
}
