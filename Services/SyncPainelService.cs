using System.Text.Json;
using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Item 11.5 (26/09): gravacao do painel administrativo — o POST /api/admin/sync/{colecao}
/// que as telas antigas (agendamentos, pets, clientes, servicos, produtos, entradas e saidas)
/// usam para mandar SO o que mudou (criados, atualizados, removidos).
///
/// Estava inteiro dentro do AdminStoreController; veio para ca sem mudar regra nem mensagem.
/// O controller continua dono de: permissao do modulo por acao do delta, aviso em tempo real,
/// eventos no Log (depois do SaveChanges) e resposta. Aqui fica o resto:
///   - Funcionario so grava na propria unidade (item 1) — antes de qualquer escrita;
///   - capacidade por horario da unidade (item 5);
///   - status e responsavel do agendamento (item 4), produto validado (item 13),
///     saldo so pelo livro de estoque (item 7), imagem por ImagemDataUri (item 10);
///   - um SaveChanges so e, para agendamentos, reconciliacao dos pagamentos (item 8).
///
/// Scoped (uma instancia por requisicao, registrada no Program.cs): guarda o autor da
/// requisicao para as movimentacoes de estoque.
/// </summary>
public sealed class SyncPainelService(LanePetsDbContext db, PermissaoService permissoes)
{
    // =======================================================================
    // ENTRADA UNICA — o que o POST /api/admin/sync/{colecao} executa depois que o
    // controller conferiu a permissao do modulo para as acoes do delta
    // =======================================================================

    /// <summary>
    /// Aplica o delta de uma colecao: confere a unidade (Funcionario), a capacidade do horario
    /// (agendamentos), cria/atualiza/remove, grava tudo num SaveChanges so e reconcilia os
    /// pagamentos quando a colecao e agendamentos. Devolve os ids criados e as contagens.
    /// </summary>
    public async Task<(List<string> NovosIds, int Alterados, int Apagados)> AplicarAsync(
        ContextoAdmin contexto, string colecao, List<JsonElement> criados, List<JsonElement> atualizados, List<string> removidos)
    {
        _autorId = contexto.Usuario.Id;          // item 7: autor das movimentacoes de estoque
        _autor = contexto.Usuario.Email;
        var novosIds = new List<string>();
        int alterados = 0, apagados = 0;

        // Funcionario (item 1): so grava agendamento e pet da propria unidade.
        // A checagem acontece ANTES de qualquer gravacao; um item fora da
        // unidade derruba a requisicao inteira com 403.
        if (contexto.EhFuncionario)
            await ExigirUnidadeNoDeltaAsync(contexto, colecao, criados, atualizados, removidos);

        // Item 5: capacidade por horario da unidade (vale para todo mundo).
        if (colecao == "agendamentos")
            await ValidarCapacidadeAsync(criados, atualizados);

        switch (colecao)
        {
            case "agendamentos":
                foreach (var item in criados) novosIds.Add(await CriarAgendamento(item));
                foreach (var item in atualizados) alterados += await AtualizarAgendamento(item);
                apagados += await Remover(db.Agendamentos, removidos, a => a.Id);
                break;

            case "pets":
                foreach (var item in criados) novosIds.Add(await CriarPet(item, contexto.EhFuncionario ? contexto.Usuario.Unidade : ""));
                foreach (var item in atualizados) alterados += await AtualizarPet(item);
                apagados += await Remover(db.Pets, removidos, p => p.Id);
                break;

            case "clientes":
                foreach (var item in criados) novosIds.Add(await CriarCliente(item));
                foreach (var item in atualizados) alterados += await AtualizarCliente(item);
                apagados += await Remover(db.Clientes, removidos, c => c.Id);
                break;

            case "servicos":
                foreach (var item in criados) novosIds.Add(CriarServico(item));
                foreach (var item in atualizados) alterados += await AtualizarServico(item);
                apagados += await Remover(db.Servicos, removidos, s => s.Id);
                break;

            case "produtos":
                foreach (var item in criados) novosIds.Add(CriarProduto(item));
                foreach (var item in atualizados) alterados += await AtualizarProduto(item);
                apagados += await Remover(db.Produtos, removidos, p => p.Id);
                break;

            case "entradasesaidas":
                foreach (var item in criados) novosIds.Add(CriarLancamento(item));
                foreach (var item in atualizados) alterados += await AtualizarLancamento(item);
                apagados += await Remover(db.EntradasESaidas, removidos, e => e.Id);
                break;

            default:
                throw new Exception($"Colecao \"{colecao}\" nao e sincronizavel.");
        }

        await db.SaveChangesAsync();
        // Item 8: agendamento criado/alterado/excluido -> pagamento acompanha.
        if (colecao == "agendamentos") await PagamentosService.ReconciliarAsync(db);
        return (novosIds, alterados, apagados);
    }

    /// <summary>
    /// Transporte "nao" nas varias formas ja gravadas no banco (descricao ou "True"/"False"
    /// dos registros importados). Usado na escrita e na leitura do painel.
    /// </summary>
    public static readonly string[] SemTransporte =
        { "", "false", "0", "nao", "n", "cliente leva", "sem transporte", "nenhum" };

    // ---------------------------------------------------------- CAPACIDADE
    /// <summary>
    /// Item 5: recusa agendamento que passaria da capacidade da unidade naquele
    /// horario. Conta o que ja esta no banco e o que vem no mesmo lote. Edicao
    /// que NAO muda unidade nem horario (ex.: so o status) passa direto — assim
    /// um encaixe antigo acima da capacidade continua editavel.
    /// </summary>
    private async Task ValidarCapacidadeAsync(List<JsonElement> criados, List<JsonElement> atualizados)
    {
        var noLote = new Dictionary<string, int>();
        async Task Conferir(JsonElement item, string? id)
        {
            if (Normalizador.Status(Texto(item, "status")) == "Cancelado") return;
            var unidade = await UnidadesRegras.AcharAsync(db, Texto(item, "unidade"));
            var chave = UnidadesRegras.ChaveHorario(Texto(item, "dataHora"));
            if (unidade is null || chave.Length < 16) return;

            if (id is not null)
            {
                var atual = await db.Agendamentos.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
                if (atual is not null && Normalizador.IdUnidade(atual.Unidade) == unidade.Id
                    && UnidadesRegras.ChaveHorario(atual.DataHora) == chave) return;
            }

            var lote = $"{unidade.Id}|{chave}";
            var ocupados = await UnidadesRegras.OcupadosAsync(db, unidade.Id, chave, id) + noLote.GetValueOrDefault(lote);
            var capacidade = UnidadesRegras.CapacidadeDe(unidade);
            if (ocupados >= capacidade)
                throw new Exception($"O horário {chave[11..16]} de {chave[8..10]}/{chave[5..7]} já está lotado na unidade {unidade.Nome} " +
                                    $"(capacidade {capacidade} por horário). Escolha outro horário ou aumente a capacidade em Unidades.");
            noLote[lote] = noLote.GetValueOrDefault(lote) + 1;
        }

        foreach (var item in criados) await Conferir(item, Texto(item, "id") is { Length: > 0 } novo ? novo : null);
        foreach (var item in atualizados) await Conferir(item, Texto(item, "id"));
    }

    // ----------------------------------------------------------------- AGD
    private async Task ExigirUnidadeNoDeltaAsync(ContextoAdmin contexto, string colecao, List<JsonElement> criados, List<JsonElement> atualizados, List<string> removidos)
    {
        if (colecao == "agendamentos")
        {
            // Criar/editar: a unidade informada tem de ser a dele. Editar e
            // excluir: o registro que ja esta no banco tambem.
            foreach (var item in criados.Concat(atualizados))
                PermissaoService.ExigirUnidade(contexto, Texto(item, "unidade"), "agendamentos");
            var alvos = atualizados.Select(i => Texto(i, "id")).Concat(removidos).ToHashSet();
            if (alvos.Count > 0)
                foreach (var a in await db.Agendamentos.AsNoTracking().Where(a => alvos.Contains(a.Id)).ToListAsync())
                    PermissaoService.ExigirUnidade(contexto, a.Unidade, "agendamentos");
        }
        else if (colecao == "pets")
        {
            var visiveis = await permissoes.PetsVisiveisAsync(contexto) ?? new();
            foreach (var id in atualizados.Select(i => Texto(i, "id")).Concat(removidos))
                if (!visiveis.Contains(id))
                    throw new AcessoNegadoException("Você só pode alterar pets atendidos na sua unidade.");
            // Pet criado por funcionario nasce na unidade dele (ou na informada,
            // se for a mesma).
            foreach (var item in criados)
            {
                var unidade = Texto(item, "unidade");
                if (unidade.Length > 0) PermissaoService.ExigirUnidade(contexto, unidade, "pets");
            }
        }
    }

    private async Task<string> CriarAgendamento(JsonElement item)
    {
        var id = Id(item, "AGD");
        var dono = Texto(item, "dono");
        var telefone = Texto(item, "telefone");

        // Liga o agendamento ao cadastro real sempre que der: e o que faz o
        // agendamento criado no painel aparecer tambem na conta do cliente.
        var pet = await ResolverPet(Texto(item, "petId"), Texto(item, "pet"), dono);
        var clienteId = Texto(item, "clienteId");
        if (clienteId.Length == 0) clienteId = pet?.ClienteId ?? await ResolverClienteId(dono, telefone);

        // Item 4: status novo validado e responsavel da mesma unidade.
        var unidadeNova = UnidadeBanco(Texto(item, "unidade"));
        var statusNovo = StatusAgendamento.Validar(Texto(item, "status"), "");
        var responsavelNovo = await ResponsaveisAgendamento.ValidarAsync(db, Texto(item, "responsavelId"), unidadeNova);

        db.Agendamentos.Add(new Agendamento
        {
            Id = id,
            Pet = Texto(item, "pet"),
            Dono = dono,
            Telefone = telefone,
            DataHora = Texto(item, "dataHora"),
            ServicosJson = Bruto(item, "servicos") ?? "[]",
            Total = Numero(item, "total"),
            Transporte = DescricaoTransporte(item),
            ValorTransporte = Numero(item, "valorTransporte"),
            Status = statusNovo,
            ResponsavelId = responsavelNovo,
            PagamentoStatus = Preenchido(Texto(item, "pagamentoStatus"), "A pagar"),
            FormaPagamento = Texto(item, "formaPagamento"),
            Obs = Texto(item, "obs"),
            Unidade = UnidadeBanco(Texto(item, "unidade")),
            ClienteId = clienteId,
            PetId = pet?.Id ?? Texto(item, "petId")
        });
        return id;
    }

    private async Task<int> AtualizarAgendamento(JsonElement item)
    {
        var alvo = await db.Agendamentos.FindAsync(Texto(item, "id"));
        if (alvo is null) return 0;
        alvo.Pet = Texto(item, "pet");
        alvo.Dono = Texto(item, "dono");
        alvo.Telefone = Texto(item, "telefone");
        alvo.DataHora = Texto(item, "dataHora");
        alvo.ServicosJson = Bruto(item, "servicos") ?? alvo.ServicosJson;
        alvo.Total = Numero(item, "total");
        alvo.Transporte = DescricaoTransporte(item, alvo.Transporte);
        alvo.ValorTransporte = Numero(item, "valorTransporte");
        alvo.Status = StatusAgendamento.Validar(Texto(item, "status"), alvo.Status);
        alvo.PagamentoStatus = Preenchido(Texto(item, "pagamentoStatus"), alvo.PagamentoStatus);
        alvo.FormaPagamento = Texto(item, "formaPagamento");
        alvo.Obs = Texto(item, "obs");
        var unidadeAntes = Normalizador.IdUnidade(alvo.Unidade);
        var unidade = UnidadeBanco(Texto(item, "unidade"));
        if (unidade.Length > 0) alvo.Unidade = unidade;

        // Item 4: responsavel. Tela antiga (sem o campo) mantem o atual. So
        // revalida quando o responsavel ou a unidade mudam — assim mudar so o
        // status de um atendimento antigo nunca trava.
        var responsavel = item.TryGetProperty("responsavelId", out _) ? Texto(item, "responsavelId") : alvo.ResponsavelId;
        if (responsavel != alvo.ResponsavelId || Normalizador.IdUnidade(alvo.Unidade) != unidadeAntes)
            alvo.ResponsavelId = await ResponsaveisAgendamento.ValidarAsync(db, responsavel, alvo.Unidade);
        return 1;
    }

    /// <summary>Recupera a descricao do transporte sem perder a que ja estava gravada.</summary>
    private static string DescricaoTransporte(JsonElement item, string? atual = null)
    {
        var marcado = Booleano(item, "transporte") || Numero(item, "valorTransporte") > 0;
        // Sem transporte marcado a descricao antiga nao pode continuar valendo:
        // desmarcar o transporte na tela precisa apagar tambem a descricao.
        if (!marcado) return "Cliente leva";
        var descricao = Texto(item, "transporteDescricao");
        if (descricao.Length > 0 && !SemTransporte.Contains(Normalizador.Texto(descricao))
            && Normalizador.Texto(descricao) != "true") return descricao;
        return string.IsNullOrWhiteSpace(atual) || Normalizador.Texto(atual) == "cliente leva" ? "Busca e entrega" : atual!;
    }

    private async Task<Pet?> ResolverPet(string petId, string nomePet, string dono)
    {
        if (petId.Length > 0)
        {
            var porId = await db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == petId);
            if (porId is not null) return porId;
        }
        if (nomePet.Length == 0) return null;
        var chavePet = Normalizador.Texto(nomePet);
        var chaveDono = Normalizador.Texto(dono);
        return (await db.Pets.AsNoTracking().ToListAsync())
            .FirstOrDefault(p => Normalizador.Texto(p.PetNome) == chavePet && Normalizador.Texto(p.Dono) == chaveDono);
    }

    private async Task<string> ResolverClienteId(string nome, string telefone)
    {
        if (nome.Length == 0 && telefone.Length == 0) return "";
        var chaveNome = Normalizador.Texto(nome);
        var digitos = new string((telefone ?? "").Where(char.IsDigit).ToArray());
        var clientes = await db.Clientes.AsNoTracking().ToListAsync();
        var achado = clientes.FirstOrDefault(c => Normalizador.Texto(c.Nome) == chaveNome)
                     ?? (digitos.Length >= 8
                         ? clientes.FirstOrDefault(c => new string(c.Telefone.Where(char.IsDigit).ToArray()) == digitos)
                         : null);
        return achado?.Id ?? "";
    }

    // ----------------------------------------------------------------- PET
    /// <param name="unidadePadrao">
    /// Unidade usada quando a tela nao informa nenhuma. Vem preenchida so para
    /// funcionario: o pet que ele cadastra nasce na unidade dele e continua
    /// visivel para ele depois.
    /// </param>
    private async Task<string> CriarPet(JsonElement item, string unidadePadrao = "")
    {
        var id = Id(item, "PET");
        var dono = Texto(item, "dono");
        var telefone = Texto(item, "telefone");
        var endereco = Texto(item, "endereco");
        var clienteId = Texto(item, "clienteId");
        if (clienteId.Length == 0) clienteId = await ResolverClienteId(dono, telefone);

        // Pet cadastrado no painel para um dono que ainda nao existe: cria o
        // cliente junto, para nao nascer um pet orfao.
        if (clienteId.Length == 0 && dono.Length > 0)
        {
            var cliente = new Cliente
            {
                Id = NovoId("CLI"),
                Nome = dono,
                Telefone = telefone,
                Endereco = endereco,
                Origem = "cadastro_painel",
                Status = "ativo"
            };
            db.Clientes.Add(cliente);
            clienteId = cliente.Id;
        }

        db.Pets.Add(new Pet
        {
            Id = id,
            ClienteId = clienteId,
            Dono = dono,
            PetNome = Texto(item, "pet"),
            Tipo = Texto(item, "tipo"),
            Raca = Texto(item, "raca"),
            Telefone = telefone,
            Endereco = endereco,
            PacoteJson = Bruto(item, "pacote") ?? "",
            Unidade = Preenchido(UnidadeBanco(Texto(item, "unidade")), UnidadeBanco(unidadePadrao))
        });
        return id;
    }

    private async Task<int> AtualizarPet(JsonElement item)
    {
        var alvo = await db.Pets.FindAsync(Texto(item, "id"));
        if (alvo is null) return 0;
        alvo.Dono = Texto(item, "dono");
        alvo.PetNome = Texto(item, "pet");
        alvo.Tipo = Texto(item, "tipo");
        alvo.Raca = Texto(item, "raca");
        alvo.Telefone = Texto(item, "telefone");
        alvo.Endereco = Texto(item, "endereco");
        alvo.PacoteJson = Bruto(item, "pacote") ?? alvo.PacoteJson;
        var unidade = UnidadeBanco(Texto(item, "unidade"));
        if (unidade.Length > 0) alvo.Unidade = unidade;
        return 1;
    }

    // ------------------------------------------------------------- CLIENTE
    private async Task<string> CriarCliente(JsonElement item)
    {
        var id = Id(item, "CLI");
        db.Clientes.Add(new Cliente
        {
            Id = id,
            Nome = Texto(item, "nome"),
            Telefone = Texto(item, "telefone"),
            Endereco = Texto(item, "endereco"),
            Observacoes = Texto(item, "observacoes"),
            Origem = Preenchido(Texto(item, "origem"), "cadastro_painel"),
            Status = Preenchido(Texto(item, "status"), "ativo")
        });
        await Task.CompletedTask;
        return id;
    }

    private async Task<int> AtualizarCliente(JsonElement item)
    {
        var alvo = await db.Clientes.FindAsync(Texto(item, "id"));
        if (alvo is null) return 0;
        var nome = Texto(item, "nome");
        if (nome.Length > 0) alvo.Nome = nome;
        alvo.Telefone = Texto(item, "telefone");
        alvo.Endereco = Texto(item, "endereco");
        alvo.Observacoes = Texto(item, "observacoes");
        alvo.Status = Preenchido(Texto(item, "status"), alvo.Status);

        // O nome do dono aparece copiado nos pets e nos agendamentos: mantem
        // as duas pontas coerentes, como ja acontece na area do cliente.
        foreach (var pet in await db.Pets.Where(p => p.ClienteId == alvo.Id).ToListAsync())
        {
            pet.Dono = alvo.Nome;
            pet.Telefone = alvo.Telefone;
        }
        foreach (var ag in await db.Agendamentos.Where(a => a.ClienteId == alvo.Id).ToListAsync())
        {
            ag.Dono = alvo.Nome;
            ag.Telefone = alvo.Telefone;
        }
        return 1;
    }

    // ------------------------------------------------------------- SERVICO
    private string CriarServico(JsonElement item)
    {
        var id = Id(item, "SRV");
        db.Servicos.Add(new Servico
        {
            Id = id,
            Nome = Texto(item, "nome"),
            Preco = Numero(item, "preco"),
            Porte = Texto(item, "porte"),
            AdicionaisJson = Bruto(item, "adicionais") ?? "[]",
            Pacote = Texto(item, "pacote"),
            Adicional = Texto(item, "adicional")
        });
        return id;
    }

    private async Task<int> AtualizarServico(JsonElement item)
    {
        var alvo = await db.Servicos.FindAsync(Texto(item, "id"));
        if (alvo is null) return 0;
        alvo.Nome = Texto(item, "nome");
        alvo.Preco = Numero(item, "preco");
        alvo.Porte = Texto(item, "porte");
        alvo.AdicionaisJson = Bruto(item, "adicionais") ?? alvo.AdicionaisJson;
        alvo.Pacote = Texto(item, "pacote");
        alvo.Adicional = Texto(item, "adicional");
        return 1;
    }

    // ------------------------------------------------------------- PRODUTO
    private string _autorId = "", _autor = "";

    /// <summary>Descricao do produto: opcional, ate 1000 caracteres.</summary>
    private static string DescricaoProduto(JsonElement item)
    {
        var texto = Texto(item, "descricao").Trim();
        if (texto.Length > 1000) throw new Validacao.ValidacaoException("A descrição do produto pode ter no máximo 1000 caracteres.");
        return texto;
    }

    /// <summary>Foto do produto pela mesma regra de toda imagem (ImagemDataUri, item 10).</summary>
    private static string FotoProduto(JsonElement item, string atual)
    {
        var remover = Booleano(item, "removerFoto");
        var enviada = item.TryGetProperty("fotoUrl", out _) ? Texto(item, "fotoUrl") : null;
        // A tela devolve a foto atual no mesmo campo: igual = nada mudou.
        if (!remover && enviada == atual) return atual;
        return ImagemDataUri.Resolver(atual, enviada, remover);
    }

    private string CriarProduto(JsonElement item)
    {
        // Item 13: valida antes de criar (preco > 0, custo e estoques >= 0).
        Validacao.Produto(Texto(item, "nome"), Numero(item, "valorVenda"), Numero(item, "valorCompra"),
            (int)Numero(item, "estoque"), (int)Numero(item, "estoqueMinimo"));
        var id = Id(item, "PRD");
        var produto = new Produto
        {
            Id = id,
            Codigo = Texto(item, "codigo"),
            Nome = Texto(item, "nome"),
            Categoria = Texto(item, "categoria"),
            ValorCompra = Numero(item, "valorCompra"),
            ValorVenda = Numero(item, "valorVenda"),
            Estoque = 0,
            EstoqueMinimo = (int)Numero(item, "estoqueMinimo"),
            ControlaEstoque = Booleano(item, "controlaEstoque"),
            Descricao = DescricaoProduto(item),
            FotoUrl = FotoProduto(item, ""),
            // Item 6: sem o campo (tela antiga) o produto nasce visivel, como sempre foi.
            VisivelLoja = !item.TryGetProperty("visivelLoja", out _) || Booleano(item, "visivelLoja")
        };
        db.Produtos.Add(produto);
        // Item 7: estoque inicial entra pelo livro, como "entrada".
        var inicial = (int)Numero(item, "estoque");
        if (inicial > 0) EstoqueService.Movimentar(db, produto, inicial, EstoqueService.Entrada, "Estoque inicial do cadastro", _autorId, _autor);
        else if (!produto.ControlaEstoque) produto.Estoque = inicial;
        return id;
    }

    private async Task<int> AtualizarProduto(JsonElement item)
    {
        var alvo = await db.Produtos.FindAsync(Texto(item, "id"));
        if (alvo is null) return 0;
        // Item 13: valida o estado FINAL antes de tocar na entidade.
        Validacao.Produto(Texto(item, "nome"), Numero(item, "valorVenda"), Numero(item, "valorCompra"),
            alvo.Estoque,
            item.TryGetProperty("estoqueMinimo", out _) ? (int)Numero(item, "estoqueMinimo") : alvo.EstoqueMinimo);
        var descricao = DescricaoProduto(item);
        var foto = FotoProduto(item, alvo.FotoUrl);
        alvo.Codigo = Texto(item, "codigo");
        alvo.Nome = Texto(item, "nome");
        alvo.Categoria = Texto(item, "categoria");
        alvo.ValorCompra = Numero(item, "valorCompra");
        alvo.ValorVenda = Numero(item, "valorVenda");
        if (item.TryGetProperty("descricao", out _)) alvo.Descricao = descricao;
        alvo.FotoUrl = foto;
        if (item.TryGetProperty("visivelLoja", out _)) alvo.VisivelLoja = Booleano(item, "visivelLoja");
        // Item 7: o SALDO nao muda pela edicao do produto — so pelo livro de
        // estoque (movimentacao/ajuste). Antes, editar o preco com a tela
        // aberta ha tempo devolvia um saldo velho e desfazia a baixa de pedidos.
        if (item.TryGetProperty("estoqueMinimo", out _)) alvo.EstoqueMinimo = (int)Numero(item, "estoqueMinimo");
        if (item.TryGetProperty("controlaEstoque", out _)) alvo.ControlaEstoque = Booleano(item, "controlaEstoque");
        return 1;
    }

    // ---------------------------------------------------------- LANCAMENTO
    /// <summary>
    /// A tela de Entradas e Saidas so valida no navegador (experiencia, nao
    /// decisao — regra 15 do CONTEXTO). Quem decide e aqui: tipo tem que ser
    /// Entrada/Saida, valor tem que ser positivo, data e descricao nao podem
    /// vir vazias. Excecao aqui vira 400 pelo ApiControllerBase, com a mesma
    /// mensagem que a tela mostra no toast.
    /// </summary>
    private static void ValidarLancamento(string data, string tipo, decimal valor, string descricao)
    {
        if (tipo != "Entrada" && tipo != "Saída")
            throw new Exception("Tipo de movimentacao invalido: use \"Entrada\" ou \"Saida\".");
        if (valor <= 0)
            throw new Exception("O valor da movimentacao precisa ser maior que zero.");
        if (string.IsNullOrWhiteSpace(data) || !DateTime.TryParse(data, out _))
            throw new Exception("Informe uma data valida para a movimentacao.");
        if (string.IsNullOrWhiteSpace(descricao))
            throw new Exception("Informe uma descricao para a movimentacao.");
    }

    private string CriarLancamento(JsonElement item)
    {
        var data = Texto(item, "data");
        var descricao = Texto(item, "descricao");
        var tipo = Texto(item, "tipo");
        var valor = Numero(item, "valor");
        ValidarLancamento(data, tipo, valor, descricao);

        var id = Id(item, "FIN");
        db.EntradasESaidas.Add(new EntradaSaida
        {
            Id = id,
            Data = data,
            Descricao = descricao,
            Tipo = tipo,
            Valor = valor,
            Unidade = UnidadeBanco(Texto(item, "unidade")),
            Origem = Preenchido(Texto(item, "origem"), "painel")
        });
        return id;
    }

    private async Task<int> AtualizarLancamento(JsonElement item)
    {
        var alvo = await db.EntradasESaidas.FindAsync(Texto(item, "id"));
        if (alvo is null) return 0;

        var data = Texto(item, "data");
        var descricao = Texto(item, "descricao");
        var tipo = Texto(item, "tipo");
        var valor = Numero(item, "valor");
        ValidarLancamento(data, tipo, valor, descricao);

        alvo.Data = data;
        alvo.Descricao = descricao;
        alvo.Tipo = tipo;
        alvo.Valor = valor;
        alvo.Unidade = UnidadeBanco(Texto(item, "unidade"));
        return 1;
    }

    // =======================================================================
    // AUXILIARES
    // =======================================================================

    private async Task<int> Remover<T>(DbSet<T> conjunto, List<string> ids, Func<T, string> chave) where T : class
    {
        if (ids.Count == 0) return 0;
        var alvos = (await conjunto.ToListAsync()).Where(x => ids.Contains(chave(x))).ToList();
        conjunto.RemoveRange(alvos);
        return alvos.Count;
    }

    private static string NovoId(string prefixo) => prefixo + "-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    /// <summary>Usa o id que a tela mandou; se veio vazio, gera um novo.</summary>
    private static string Id(JsonElement item, string prefixo)
    {
        var informado = Texto(item, "id");
        return informado.Length > 0 ? informado : NovoId(prefixo);
    }

    private static string Preenchido(string valor, string padrao) => valor.Length > 0 ? valor : padrao;

    /// <summary>
    /// Grava a unidade na mesma forma que o banco ja usa ("franco"/"caieiras"),
    /// que e tambem a que a area do cliente escreve. A leitura continua passando
    /// pelo Normalizador, entao registros antigos em qualquer capitalizacao
    /// continuam sendo reconhecidos.
    /// </summary>
    private static string UnidadeBanco(string? valor) => Normalizador.IdUnidade(valor);

    public static List<JsonElement> Itens(JsonElement valor)
        => valor.ValueKind == JsonValueKind.Array
            ? valor.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList()
            : new List<JsonElement>();

    public static List<string> Ids(JsonElement valor)
        => valor.ValueKind == JsonValueKind.Array
            ? valor.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.ToString())
                   .Where(x => x.Length > 0).ToList()
            : new List<string>();

    public static string Texto(JsonElement objeto, string nome)
    {
        if (!objeto.TryGetProperty(nome, out var valor)) return "";
        return valor.ValueKind switch
        {
            JsonValueKind.String => valor.GetString()?.Trim() ?? "",
            JsonValueKind.Number => valor.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    private static decimal Numero(JsonElement objeto, string nome)
    {
        if (!objeto.TryGetProperty(nome, out var valor)) return 0m;
        if (valor.ValueKind == JsonValueKind.Number && valor.TryGetDecimal(out var d)) return d;
        var texto = (valor.ValueKind == JsonValueKind.String ? valor.GetString() : null) ?? "";
        texto = texto.Replace("R$", "").Trim();
        if (texto.Contains(',')) texto = texto.Replace(".", "").Replace(',', '.');
        return decimal.TryParse(texto, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    private static bool Booleano(JsonElement objeto, string nome)
    {
        if (!objeto.TryGetProperty(nome, out var valor)) return false;
        return valor.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => valor.TryGetDecimal(out var n) && n != 0,
            JsonValueKind.String => Normalizador.Texto(valor.GetString()) is "sim" or "true" or "1",
            _ => false
        };
    }

    private static string? Bruto(JsonElement objeto, string nome)
        => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind is JsonValueKind.Array or JsonValueKind.Object
            ? valor.GetRawText()
            : null;
}
