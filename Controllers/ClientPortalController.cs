using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;

[Route("api/cliente")]
public class ClientPortalController(LanePetsDbContext db, SessionService sessions, RealtimeNotifier realtime) : ApiControllerBase
{
    [HttpPost("cadastro")]
    public async Task<IActionResult> Cadastro([FromBody] CadastroRequest request)
    {
        try
        {
            var email = (request.Email ?? "").Trim().ToLowerInvariant();
            if (request.Nome?.Trim().Length < 3 || !email.Contains('@') || request.Senha?.Length < 8 || request.Pet?.Trim().Length < 2) throw new Exception("Informe nome, e-mail válido, senha com ao menos 8 caracteres e nome do pet.");
            if (await db.UsuariosClientes.AnyAsync(u => u.Email == email)) throw new Exception("Já existe uma conta com este e-mail.");
            var cliente = new Cliente { Id = "CLI-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Nome = request.Nome.Trim(), Telefone = (request.Telefone ?? "").Trim(), Endereco = (request.Endereco ?? "").Trim(), Origem = "portal_cliente", Status = "ativo" };
            var pet = new Pet { Id = "PET-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), ClienteId = cliente.Id, Dono = cliente.Nome, PetNome = request.Pet.Trim(), Tipo = (request.Tipo ?? "Não informado").Trim(), Raca = (request.Raca ?? "Não informada").Trim(), Telefone = cliente.Telefone, Endereco = cliente.Endereco };
            var (hash, salt) = SessionService.HashPassword(request.Senha); db.Clientes.Add(cliente); db.Pets.Add(pet); db.UsuariosClientes.Add(new UsuarioCliente { Id="USR-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), ClienteId=cliente.Id, Email=email, SenhaHash=hash, SenhaSalt=salt }); await db.SaveChangesAsync();
            await realtime.NotificarAsync("clientes", "criado", new { cliente.Id, cliente.Nome });
            return OkApi(new { token = sessions.CreateClient(cliente.Id), cliente = new { cliente.Nome, Email = email } });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    { try { var user = await db.UsuariosClientes.FirstOrDefaultAsync(u => u.Email == (request.Email ?? "").Trim().ToLowerInvariant()) ?? throw new Exception("E-mail ou senha inválidos."); if (!SessionService.VerifyPassword(request.Senha ?? "", user.SenhaHash, user.SenhaSalt)) throw new Exception("E-mail ou senha inválidos."); var cliente = await db.Clientes.FindAsync(user.ClienteId) ?? throw new Exception("Cadastro de cliente não localizado."); return OkApi(new { token=sessions.CreateClient(cliente.Id), cliente = new { cliente.Nome, user.Email } }); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpPost("logout")]
    public IActionResult Logout() { sessions.LogoutClient(Token()); return OkApi(new { encerrado = true }); }
    [HttpGet("conta")]
    public async Task<IActionResult> Conta()
    {
        try
        {
            var cliente = await Cliente();
            // Campos adicionais (email / criadoEm) vem do usuario vinculado ao cliente.
            // Acrescimo aditivo: nada do que ja era devolvido mudou de nome ou de tipo.
            var usuario = await db.UsuariosClientes.AsNoTracking().FirstOrDefaultAsync(u => u.ClienteId == cliente.Id);
            return OkApi(new
            {
                cliente.Id,
                cliente.Nome,
                cliente.Telefone,
                cliente.Endereco,
                Email = usuario?.Email ?? "",
                CriadoEm = usuario?.CriadoEm,
                Status = string.IsNullOrWhiteSpace(cliente.Status) ? "ativo" : cliente.Status,
                // A mesma projecao usada pelos endpoints de pet, para a tela
                // receber sempre os mesmos nomes de campo — venha o pet da
                // carga inicial da conta ou de um cadastro recem-confirmado.
                pets = (await db.Pets.AsNoTracking().Where(p => p.ClienteId == cliente.Id).ToListAsync())
                       .Select(Ficha).ToList(),
                agendamentos = await db.Agendamentos.AsNoTracking().Where(a => a.ClienteId == cliente.Id).OrderByDescending(a => a.DataHora).ToListAsync(),
                pedidos = await db.Pedidos.AsNoTracking().Where(p => p.ClienteId == cliente.Id).OrderByDescending(p => p.CriadoEm).ToListAsync()
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // ---------------------------------------------------------------------
    // ROTAS NOVAS DA AREA DO CLIENTE
    // Nenhuma rota existente foi alterada e o esquema do banco continua o mesmo.
    // ---------------------------------------------------------------------

    /// <summary>Atualiza os dados de perfil do proprio cliente autenticado.</summary>
    [HttpPut("conta")]
    public async Task<IActionResult> AtualizarConta([FromBody] PerfilRequest request)
    {
        try
        {
            var cliente = await db.Clientes.FindAsync((await Cliente()).Id) ?? throw new Exception("Cadastro nao localizado.");
            var nome = (request.Nome ?? "").Trim();
            if (nome.Length < 3) throw new Exception("Informe seu nome completo (minimo 3 letras).");
            var telefone = (request.Telefone ?? "").Trim();
            if (telefone.Where(char.IsDigit).Count() < 10) throw new Exception("Informe um telefone com DDD.");

            cliente.Nome = nome;
            cliente.Telefone = telefone;
            cliente.Endereco = (request.Endereco ?? "").Trim();

            // O nome do dono tambem aparece nos pets e nos agendamentos ja criados.
            foreach (var pet in await db.Pets.Where(p => p.ClienteId == cliente.Id).ToListAsync())
            {
                pet.Dono = cliente.Nome;
                pet.Telefone = cliente.Telefone;
            }
            await db.SaveChangesAsync();

            var usuario = await db.UsuariosClientes.AsNoTracking().FirstOrDefaultAsync(u => u.ClienteId == cliente.Id);
            await realtime.NotificarAsync("clientes", "atualizado", new { cliente.Id, cliente.Nome });
            return OkApi(new { cliente.Nome, cliente.Telefone, cliente.Endereco, Email = usuario?.Email ?? "", message = "Perfil atualizado." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // -----------------------------------------------------------------------
    // MEUS PETS
    //
    // Tres endpoints, uma regra so: o cliente da requisicao e identificado pelo
    // token da sessao (header X-LanePets-Client), NUNCA por um id que venha no
    // corpo ou na URL. Toda consulta de pet carrega a condicao
    //
    //     p.Id == id && p.ClienteId == cliente.Id
    //
    // na MESMA consulta. Nao existe "busca o pet e depois confere o dono": a
    // consulta que nao casa com a conta simplesmente nao devolve linha, e o pet
    // de outro cliente responde "nao localizado na sua conta" — o mesmo que um
    // id inexistente responderia. Trocar o id na requisicao nao alcanca nada.
    //
    // O corpo da requisicao nao tem ClienteId. Nao e que ele seja ignorado:
    // ele nao existe no contrato. Quem preenche o vinculo e o servidor.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Ficha completa de UM pet da propria conta, para a tela de detalhes e para
    /// abrir a edicao com os dados que estao mesmo no banco.
    /// </summary>
    [HttpGet("pets/{id}")]
    public async Task<IActionResult> ObterPet(string id)
    {
        try
        {
            var cliente = await Cliente();
            var pet = await db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.ClienteId == cliente.Id)
                      ?? throw new Exception("Pet não localizado na sua conta.");
            return OkApi(Ficha(pet));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>
    /// Cadastra mais um pet na conta do proprio cliente.
    ///
    /// A tela percorre as etapas guardando tudo em memoria; este endpoint so e
    /// chamado UMA vez, na confirmacao final. Nao existe gravacao parcial por
    /// etapa: ou o pet nasce completo, ou nao nasce.
    /// </summary>
    [HttpPost("pets")]
    public async Task<IActionResult> CriarPet([FromBody] PetRequest request)
    {
        try
        {
            var cliente = await Cliente();
            if (await db.Pets.CountAsync(p => p.ClienteId == cliente.Id) >= 20) throw new Exception("Limite de pets por conta atingido. Fale com a equipe LanePets.");

            var pet = new Pet
            {
                Id = "PET-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                // O vinculo e os dados de contato vem da conta autenticada, e nao
                // do formulario. E isto que impede um pet de nascer preso a outro
                // cliente.
                ClienteId = cliente.Id,
                Dono = cliente.Nome,
                Telefone = cliente.Telefone,
                Endereco = cliente.Endereco
            };
            // Valida tudo antes de gravar. Se algo estiver errado, a excecao sobe
            // aqui e nada e adicionado ao contexto.
            PetFicha.Aplicar(pet, request.ParaFicha());

            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            await realtime.NotificarAsync("pets", "criado", new { pet.Id, pet.PetNome, pet.Dono });
            return OkApi(Ficha(pet));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>
    /// Atualiza um pet da propria conta. O cliente so alcanca os pets dele.
    ///
    /// Assim como no cadastro, a tela so chama este endpoint depois que o
    /// cliente confirmou as alteracoes na tela de conferencia.
    /// </summary>
    [HttpPut("pets/{id}")]
    public async Task<IActionResult> AtualizarPet(string id, [FromBody] PetRequest request)
    {
        try
        {
            var cliente = await Cliente();
            var pet = await db.Pets.FirstOrDefaultAsync(p => p.Id == id && p.ClienteId == cliente.Id)
                      ?? throw new Exception("Pet não localizado na sua conta.");

            PetFicha.Aplicar(pet, request.ParaFicha());

            // Os agendamentos guardam o nome do pet; renomear o pet renomeia
            // tambem o que o painel ja mostra deles. E o mesmo pet: o sistema
            // nao cria uma segunda copia com o nome novo.
            foreach (var ag in await db.Agendamentos.Where(a => a.PetId == pet.Id).ToListAsync()) ag.Pet = pet.PetNome;
            // O Seguro Pet tambem carimbou o nome do pet no contrato. O contrato
            // continua sendo o mesmo registro; so o nome exibido acompanha.
            foreach (var s in await db.SolicitacoesSeguro.Where(x => x.PetId == pet.Id).ToListAsync()) s.NomePet = pet.PetNome;
            await db.SaveChangesAsync();

            await realtime.NotificarAsync("pets", "atualizado", new { pet.Id, pet.PetNome });
            return OkApi(Ficha(pet));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>
    /// Como um pet viaja para a tela do cliente. E uma projecao explicita: a
    /// entidade nao e serializada inteira, entao campo interno (PacoteJson) nao
    /// vaza para o navegador por descuido.
    /// </summary>
    private static object Ficha(Pet p) => new
    {
        id = p.Id,
        petNome = p.PetNome,
        tipo = p.Tipo,
        raca = p.Raca,
        sexo = p.Sexo,
        dataNascimento = p.DataNascimento,
        peso = p.Peso,
        cor = p.Cor,
        porte = p.Porte,
        fotoUrl = p.FotoUrl,
        observacoes = p.Observacoes,
        necessidadesEspeciais = p.NecessidadesEspeciais,
        infoAtendimento = p.InfoAtendimento,
        unidade = p.Unidade
    };

    /// <summary>
    /// Cancela um agendamento da propria conta. O registro NAO e apagado: vira
    /// status "Cancelado", que e o que o painel e o dashboard ja sabem tratar
    /// (agendamento cancelado nao entra na receita nem na contagem).
    /// </summary>
    [HttpPost("agendamentos/{id}/cancelar")]
    public async Task<IActionResult> CancelarAgendamento(string id)
    {
        try
        {
            var cliente = await Cliente();
            var agendamento = await db.Agendamentos.FirstOrDefaultAsync(a => a.Id == id && a.ClienteId == cliente.Id)
                              ?? throw new Exception("Agendamento nao localizado na sua conta.");
            if (string.Equals(agendamento.Status, "Cancelado", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Este agendamento ja esta cancelado.");
            if (string.Equals(agendamento.Status, "Entregue", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Atendimentos ja entregues nao podem ser cancelados. Fale com a equipe LanePets.");

            agendamento.Status = "Cancelado";
            await db.SaveChangesAsync();
            await realtime.NotificarAsync("agendamentos", "cancelado", new { agendamento.Id, agendamento.Dono });
            return OkApi(new { agendamento.Id, agendamento.Status, message = "Agendamento cancelado." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>
    /// Seguros contratados pela propria conta ("Meu Seguro").
    ///
    /// O filtro e por ClienteId — a identidade vem da sessao, nunca de um id
    /// mandado pelo frontend. A versao anterior casava por nome e telefone, o
    /// que faria dois clientes homonimos verem o contrato um do outro.
    ///
    /// Contratos criados pelo formulario publico antes deste campo existir
    /// ficaram sem ClienteId; eles sao recuperados pelo par nome+telefone e
    /// adotados pela conta na primeira consulta, para nao sumirem da tela.
    /// </summary>
    [HttpGet("seguros")]
    public async Task<IActionResult> MeusSeguros()
    {
        try
        {
            var cliente = await Cliente();
            var telefone = Digitos(cliente.Telefone);

            var orfaos = await db.SolicitacoesSeguro
                .Where(s => s.ClienteId == "" && s.NomeCliente == cliente.Nome)
                .ToListAsync();
            var adotados = orfaos.Where(s => telefone.Length >= 8 && Digitos(s.Telefone) == telefone).ToList();
            if (adotados.Count > 0)
            {
                foreach (var s in adotados) s.ClienteId = cliente.Id;
                await db.SaveChangesAsync();
            }

            var planos = await db.PlanosSeguro.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p);
            var meus = await db.SolicitacoesSeguro.AsNoTracking()
                .Where(s => s.ClienteId == cliente.Id)
                .OrderByDescending(s => s.CriadoEm)
                .ToListAsync();

            return OkApi(meus.Select(s => new
            {
                s.Id,
                s.PlanoSeguroId,
                s.NomePlano,
                s.NomePet,
                s.PetId,
                s.Observacao,
                s.Status,
                CriadoEm = s.CriadoEm.ToString("O"),
                s.Valor,
                s.MetodoPagamento,
                s.PagamentoStatus,
                s.CartaoFinal,
                DataCancelamento = s.DataCancelamento.HasValue ? s.DataCancelamento.Value.ToString("O") : null,
                // O botao "Cancelar seguro" so existe para contrato que ainda
                // esta valendo. Contrato cancelado nao pode ser cancelado de novo.
                podeCancelar = !string.Equals(s.Status, "Cancelada", StringComparison.OrdinalIgnoreCase),
                // Detalhes do plano vem do catalogo: se o admin corrigir a
                // cobertura, o cliente ve a versao corrigida.
                valorMensal = planos.TryGetValue(s.PlanoSeguroId, out var p) ? p.ValorMensal : 0m,
                coberturas = planos.TryGetValue(s.PlanoSeguroId, out var p2) ? p2.Coberturas : "",
                beneficios = planos.TryGetValue(s.PlanoSeguroId, out var p3) ? p3.Beneficios : "",
                condicoes = planos.TryGetValue(s.PlanoSeguroId, out var p4) ? p4.Condicoes : "",
                planoAtivo = planos.TryGetValue(s.PlanoSeguroId, out var p5) && p5.Ativo
            }));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    private static string Digitos(string? valor) => new((valor ?? "").Where(char.IsDigit).ToArray());

    /// <summary>
    /// Fecha a contratacao de um plano para um pet da propria conta.
    ///
    /// A contratacao so acontece neste POST, ou seja, depois que o cliente
    /// passou pelas etapas da tela (plano, pet, dados, pagamento e resumo).
    /// Tudo o que o contrato precisa e resolvido aqui no servidor:
    ///   - o cliente vem da sessao, nunca de um id mandado pelo navegador;
    ///   - o pet precisa ser comprovadamente desta conta;
    ///   - o valor vem do catalogo, nao do corpo da requisicao.
    ///
    /// PAGAMENTO: o projeto nao tem gateway. Entao nada e "aprovado" aqui.
    /// Grava-se o metodo escolhido e o pagamento nasce Pendente, para a equipe
    /// confirmar no atendimento. Do cartao chegam no maximo os quatro ultimos
    /// digitos; numero completo e CVV nao sao recebidos nem gravados.
    /// </summary>
    [HttpPost("seguros")]
    public async Task<IActionResult> ContratarSeguro([FromBody] SeguroContratacaoRequest request)
    {
        try
        {
            var cliente = await Cliente();
            var plano = await db.PlanosSeguro.FirstOrDefaultAsync(p => p.Id == request.PlanoId && p.Ativo)
                        ?? throw new Exception("Plano indisponivel.");

            if (string.IsNullOrWhiteSpace(request.PetId)) throw new Exception("Selecione um pet para continuar.");
            // O pet tem de ser comprovadamente desta conta. Nao existe atalho
            // "pega o primeiro pet": seguro no pet errado e um erro silencioso
            // que o cliente so descobre quando precisa usar.
            var pet = await db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PetId && p.ClienteId == cliente.Id)
                      ?? throw new Exception("Pet nao localizado na sua conta.");

            var metodo = MetodoDePagamento(request.MetodoPagamento);
            var cartaoFinal = "";
            if (metodo.StartsWith("Cartao", StringComparison.OrdinalIgnoreCase) || metodo.StartsWith("Cart\u00e3o", StringComparison.OrdinalIgnoreCase))
            {
                cartaoFinal = new string(Digitos(request.CartaoFinal).TakeLast(4).ToArray());
                if (cartaoFinal.Length != 4) throw new Exception("Confira os dados do cartao para concluir a contratacao.");
            }

            var jaTem = await db.SolicitacoesSeguro.AnyAsync(s =>
                s.ClienteId == cliente.Id && s.PetId == pet.Id && s.PlanoSeguroId == plano.Id
                && (s.Status == "Pendente" || s.Status == "Em contato"));
            if (jaTem) throw new Exception($"Ja existe um pedido em andamento do plano {plano.Nome} para {pet.PetNome}.");

            var solicitacao = new SolicitacaoSeguro
            {
                Id = "SOL-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                PlanoSeguroId = plano.Id,
                NomePlano = plano.Nome,
                ClienteId = cliente.Id,
                PetId = pet.Id,
                NomeCliente = cliente.Nome,
                Telefone = cliente.Telefone,
                NomePet = pet.PetNome,
                Observacao = (request.Observacao ?? "").Trim(),
                Status = "Pendente",
                // Valor congelado no momento da contratacao: se o admin mudar o
                // preco do plano amanha, este contrato continua valendo o que
                // foi combinado hoje.
                Valor = plano.ValorMensal,
                MetodoPagamento = metodo,
                PagamentoStatus = "Pendente",
                CartaoFinal = cartaoFinal
            };
            db.SolicitacoesSeguro.Add(solicitacao);
            await db.SaveChangesAsync();
            await realtime.NotificarAsync("seguros", "solicitado", new { solicitacao.Id, solicitacao.NomePlano });
            return OkApi(new
            {
                solicitacao.Id,
                solicitacao.NomePlano,
                solicitacao.NomePet,
                solicitacao.Status,
                solicitacao.Valor,
                solicitacao.MetodoPagamento,
                solicitacao.PagamentoStatus,
                solicitacao.CartaoFinal,
                message = "Contratacao registrada. O pagamento fica como pendente ate a equipe LanePets confirmar."
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>
    /// Cancela um seguro da PROPRIA conta.
    ///
    /// O registro nao e apagado: o Status vira "Cancelada" e a data do
    /// cancelamento e gravada, entao o historico da contratacao continua
    /// inteiro no banco e o painel administrativo ve a mudanca sem precisar
    /// criar nada a mao.
    ///
    /// A dona do contrato e checada contra o cliente da sessao. Um id de
    /// contrato de outra pessoa simplesmente nao e encontrado por esta
    /// consulta, entao nao ha o que cancelar.
    /// </summary>
    [HttpPost("seguros/{id}/cancelar")]
    public async Task<IActionResult> CancelarSeguro(string id)
    {
        try
        {
            var cliente = await Cliente();
            var seguro = await db.SolicitacoesSeguro.FirstOrDefaultAsync(s => s.Id == id && s.ClienteId == cliente.Id)
                         ?? throw new Exception("Seguro nao localizado na sua conta.");
            if (string.Equals(seguro.Status, "Cancelada", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Este seguro ja esta cancelado.");

            seguro.Status = "Cancelada";
            seguro.DataCancelamento = DateTime.UtcNow;
            // Pagamento que nunca foi confirmado nao pode ficar "aguardando"
            // para sempre depois do cancelamento. Pagamento ja confirmado nao e
            // tocado: o estorno e assunto do atendimento.
            if (!string.Equals(seguro.PagamentoStatus, "Pago", StringComparison.OrdinalIgnoreCase))
                seguro.PagamentoStatus = "Cancelado";
            await db.SaveChangesAsync();
            await realtime.NotificarAsync("seguros", "cancelado", new { seguro.Id, seguro.NomePlano, seguro.NomeCliente });
            return OkApi(new
            {
                seguro.Id,
                seguro.Status,
                DataCancelamento = seguro.DataCancelamento?.ToString("O"),
                message = "Seguro cancelado."
            });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>Aceita apenas as formas de pagamento que o sistema conhece.</summary>
    private static string MetodoDePagamento(string? valor)
    {
        var escolhido = (valor ?? "").Trim();
        var aceitos = new[] { "PIX", "Cart\u00e3o de cr\u00e9dito", "Cart\u00e3o de d\u00e9bito" };
        var achado = aceitos.FirstOrDefault(a => string.Equals(a, escolhido, StringComparison.OrdinalIgnoreCase));
        return achado ?? throw new Exception("Escolha uma forma de pagamento para continuar.");
    }

    /// <summary>Avaliacoes enviadas pelo proprio cliente, com o status da moderacao.</summary>
    [HttpGet("avaliacoes")]
    public async Task<IActionResult> MinhasAvaliacoes()
    {
        try
        {
            var cliente = await Cliente();
            var lista = await db.Depoimentos.AsNoTracking()
                .Where(d => d.ClienteId == cliente.Id)
                .OrderByDescending(d => d.CriadoEm)
                .ToListAsync();
            return OkApi(lista);
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    /// <summary>Envia uma avaliacao ja vinculada a conta (segue para a mesma moderacao do site).</summary>
    [HttpPost("avaliacoes")]
    public async Task<IActionResult> CriarAvaliacao([FromBody] AvaliacaoRequest request)
    {
        try
        {
            var cliente = await Cliente();
            var comentario = (request.Comentario ?? "").Trim();
            if (comentario.Length < 10) throw new Exception("Escreva um comentario com pelo menos 10 caracteres.");
            if (request.Avaliacao is < 1 or > 5) throw new Exception("Escolha uma nota de 1 a 5 estrelas.");

            var pet = await db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PetId && p.ClienteId == cliente.Id)
                      ?? await db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.ClienteId == cliente.Id);

            var depoimento = new Depoimento
            {
                Id = "DEP-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                ClienteId = cliente.Id,
                NomeCliente = cliente.Nome,
                NomePet = pet?.PetNome ?? "",
                Telefone = cliente.Telefone,
                Avaliacao = request.Avaliacao,
                Comentario = comentario.Length > 500 ? comentario[..500] : comentario,
                Status = "Pendente"
            };
            db.Depoimentos.Add(depoimento);
            await db.SaveChangesAsync();
            await realtime.NotificarAsync("avaliacoes", "criada", new { depoimento.Id, depoimento.NomeCliente });
            return OkApi(new { depoimento.Id, depoimento.Status, message = "Avaliacao enviada! Ela aparece no site assim que a equipe aprovar." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
    [HttpGet("catalogo")]
    public async Task<IActionResult> Catalogo() => OkApi(new
    {
        servicos = await db.Servicos.AsNoTracking().OrderBy(s => s.Nome).ToListAsync(),
        // O cliente recebe so o que a tela dele precisa. Antes ia a entidade
        // Produto inteira, com ValorCompra e EstoqueMinimo — o preco de custo e
        // o ponto de reposicao do petshop, que sao numeros internos e nao tem
        // por que trafegar para a area do cliente.
        produtos = (await db.Produtos.AsNoTracking().OrderBy(p => p.Nome).ToListAsync())
            .Select(p => new { p.Id, p.Nome, p.Categoria, p.ValorVenda, p.Estoque, p.ControlaEstoque }),
        unidades = await db.Unidades.AsNoTracking().Where(u => u.Ativa).ToListAsync()
    });
    [HttpGet("horarios")]
    public async Task<IActionResult> Horarios([FromQuery] string unidade, [FromQuery] string data)
    { try { if (!DateOnly.TryParse(data, out var day) || string.IsNullOrWhiteSpace(unidade)) throw new Exception("Informe unidade e data."); var occupied = await db.Agendamentos.AsNoTracking().Where(a => a.Unidade.ToLower() == unidade.ToLower() && a.DataHora.StartsWith(data) && a.Status != "Cancelado").Select(a => a.DataHora).ToListAsync(); var times = new[] { "09:00", "10:00", "11:00", "13:00", "14:00", "15:00", "16:00", "17:00" }.Where(time => !occupied.Any(value => value.Contains(time))).ToArray(); return OkApi(times); } catch (Exception ex) { return ErrorApi(ex); } }
    [HttpPost("agendamentos")]
    public async Task<IActionResult> Agendar([FromBody] AgendamentoRequest request)
    {
        try
        {
            var cliente = await Cliente(); var pet = await db.Pets.FirstOrDefaultAsync(p => p.Id == request.PetId && p.ClienteId == cliente.Id) ?? throw new Exception("Pet não localizado."); var service = await db.Servicos.FindAsync(request.ServicoId) ?? throw new Exception("Serviço não localizado.");
            if (!DateOnly.TryParse(request.Data, out _) || !TimeOnly.TryParse(request.Horario, out _) || string.IsNullOrWhiteSpace(request.Unidade)) throw new Exception("Escolha unidade, data e horário válidos.");
            var dataHora = request.Data + "T" + request.Horario + ":00"; var conflict = await db.Agendamentos.AnyAsync(a => a.Unidade.ToLower() == request.Unidade.ToLower() && a.DataHora == dataHora && a.Status != "Cancelado"); if (conflict) throw new Exception("Este horário acabou de ser ocupado. Escolha outro horário.");
            var total = service.Preco + (request.Transporte?.Contains("Busca", StringComparison.OrdinalIgnoreCase) == true ? 15m : 0m) + (request.Transporte?.Contains("Entrega", StringComparison.OrdinalIgnoreCase) == true ? 15m : 0m);
            var appointment = new Agendamento { Id="AGD-"+Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), ClienteId=cliente.Id, PetId=pet.Id, Pet=pet.PetNome, Dono=cliente.Nome, Telefone=cliente.Telefone, DataHora=dataHora, ServicosJson=$"[{{\"id\":\"{service.Id}\",\"nome\":\"{service.Nome.Replace("\"", "") }\"}}]", Total=total, Transporte=request.Transporte ?? "Cliente leva", ValorTransporte=total-service.Preco, Status="Pendente", PagamentoStatus="Pendente", FormaPagamento=request.FormaPagamento ?? "A combinar", Unidade=request.Unidade, Obs=(request.Observacao ?? "").Trim() }; db.Agendamentos.Add(appointment); await db.SaveChangesAsync(); await realtime.NotificarAsync("agendamentos", "criado", new { appointment.Id, appointment.Dono, appointment.DataHora }); return OkApi(appointment);
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
    /// <summary>
    /// Pedido da loja. Alem de gravar o pedido, da baixa no estoque do produto
    /// na mesma transacao: os dois vivem no mesmo SaveChangesAsync, entao ou as
    /// duas coisas acontecem ou nenhuma acontece.
    /// </summary>
    [HttpPost("pedidos")]
    public async Task<IActionResult> CriarPedido([FromBody] PedidoRequest request)
    {
        try
        {
            var cliente = await Cliente();
            var produto = await db.Produtos.FindAsync(request.ProdutoId) ?? throw new Exception("Produto não localizado.");
            if (request.Quantidade is < 1 or > 99) throw new Exception("Informe uma quantidade entre 1 e 99.");
            // O controle de estoque e opcional por produto: so vale para os que a
            // equipe marcou como controlados no painel. Produto sem controle
            // continua vendendo normalmente, sem baixa.
            if (produto.ControlaEstoque && produto.Estoque < request.Quantidade)
                throw new Exception(produto.Estoque <= 0
                    ? $"{produto.Nome} está sem estoque no momento."
                    : $"Temos apenas {produto.Estoque} unidade(s) de {produto.Nome} em estoque.");

            var pedido = new Pedido
            {
                Id = "PED-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                ClienteId = cliente.Id,
                ProdutoId = produto.Id,
                ProdutoNome = produto.Nome,
                Quantidade = request.Quantidade,
                Total = produto.ValorVenda * request.Quantidade,
                FormaPagamento = string.IsNullOrWhiteSpace(request.FormaPagamento) ? "A combinar" : request.FormaPagamento,
                Status = "Pendente"
            };
            if (produto.ControlaEstoque) produto.Estoque -= request.Quantidade;
            db.Pedidos.Add(pedido);
            await db.SaveChangesAsync();
            await realtime.NotificarAsync("pedidos", "criado", new { pedido.Id, pedido.ProdutoNome, pedido.Quantidade, estoqueRestante = produto.ControlaEstoque ? produto.Estoque : (int?)null });
            return OkApi(new { pedido.Id, pedido.ProdutoId, pedido.ProdutoNome, pedido.Quantidade, pedido.Total, pedido.FormaPagamento, pedido.Status, pedido.CriadoEm, estoqueRestante = produto.ControlaEstoque ? produto.Estoque : (int?)null });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
    private string Token() => Request.Headers["X-LanePets-Client"].FirstOrDefault() ?? "";
    private async Task<Cliente> Cliente() { var session = sessions.RequireClient(Token()); return await db.Clientes.FindAsync(session.AdminToken) ?? throw new UnauthorizedAccessException("Cliente não localizado."); }
    public record CadastroRequest(string? Nome, string? Email, string? Senha, string? Telefone, string? Endereco, string? Pet, string? Tipo, string? Raca);
    public record LoginRequest(string? Email, string? Senha);
    public record AgendamentoRequest(string PetId, string ServicoId, string Unidade, string Data, string Horario, string? Transporte, string? FormaPagamento, string? Observacao);
    public record PedidoRequest(string ProdutoId, int Quantidade, string? FormaPagamento);
    public record PerfilRequest(string? Nome, string? Telefone, string? Endereco);
    /// <summary>
    /// A ficha que a tela envia. Note o que NAO esta aqui: id do cliente. O
    /// vinculo do pet com a conta nunca chega pelo corpo da requisicao.
    /// </summary>
    public record PetRequest(
        string? Nome, string? Tipo, string? Raca, string? Sexo, string? DataNascimento,
        string? Peso, string? Cor, string? Porte, string? FotoUrl,
        string? Observacoes, string? NecessidadesEspeciais, string? InfoAtendimento)
    {
        public PetFicha.FichaEntrada ParaFicha() => new(
            Nome, Tipo, Raca, Sexo, DataNascimento, Peso, Cor, Porte, FotoUrl,
            Observacoes, NecessidadesEspeciais, InfoAtendimento);
    }
    public record AvaliacaoRequest(string? PetId, int Avaliacao, string? Comentario);
    public record SeguroContratacaoRequest(string PlanoId, string? PetId, string? MetodoPagamento, string? CartaoFinal, string? Observacao);
}
