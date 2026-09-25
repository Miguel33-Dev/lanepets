using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;

[Route("api/cliente")]
public class ClientPortalController(LanePetsDbContext db, SessionService sessions, RealtimeNotifier realtime, EventosService eventos) : ApiControllerBase
{
    /// <summary>Item 15: evento de log com o IP desta requisicao. Nunca lanca.</summary>
    private Task Evento(string categoria, string acao, Cliente? cliente, string alvoId = "", string detalhes = "", string nivel = "info", string autor = "")
        => eventos.RegistrarAsync(new(categoria, acao, nivel, "cliente", cliente?.Id ?? "", autor.Length > 0 ? autor : cliente?.Nome ?? "", alvoId, detalhes), HttpContext);

    /// <summary>Item 11.4b: recusa da conta (senha errada etc.) vira evento "aviso" antes da excecao subir.</summary>
    private Task Recusar(ContaClienteService.Recusa r)
        => eventos.RegistrarAsync(new(r.Categoria, r.Acao, "aviso", "cliente", r.ClienteId, r.Autor, r.AlvoId, r.Detalhes), HttpContext);

    [HttpPost("cadastro")]
    public async Task<IActionResult> Cadastro([FromBody] CadastroRequest request)
    {
        try
        {
            // Item 11.4b: validacao e gravacao em ContaClienteService.
            var (cliente, pet, email) = await ContaClienteService.CadastrarAsync(db, new(
                request.Nome, request.Email, request.Senha, request.Telefone, request.Endereco, request.Pet, request.Tipo, request.Raca));
            await realtime.NotificarAsync("clientes", "criado", new { cliente.Id, cliente.Nome });
            await Evento("cliente", "Conta de cliente criada", cliente, cliente.Id, $"Pet inicial: {pet.PetNome}.", autor: email);
            return OkApi(new { token = sessions.CreateClient(cliente.Id), cliente = new { cliente.Nome, Email = email } });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            // Item 15: recusa vira evento com o motivo real; a tela continua com
            // a mensagem generica (nao revela se o e-mail existe). Item 11.4b: regra em ContaClienteService.
            var (cliente, user) = await ContaClienteService.AutenticarAsync(db, request.Email, request.Senha, Recusar);
            await Evento("autenticacao", "Login de cliente", cliente, autor: user.Email);
            return OkApi(new { token=sessions.CreateClient(cliente.Id), cliente = new { cliente.Nome, user.Email } });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
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
                agendamentos = await AgendamentosClienteService.ListarAsync(db, cliente.Id),   // item 11.4
                pedidos = await db.Pedidos.AsNoTracking().Where(p => p.ClienteId == cliente.Id).OrderByDescending(p => p.CriadoEm).ToListAsync(),
                // Item 8: pagamentos da conta (somente leitura para o cliente).
                pagamentos = (await db.Pagamentos.AsNoTracking().Where(p => p.ClienteId == cliente.Id).ToListAsync())
                    .OrderByDescending(p => p.DataReferencia)
                    .Select(p => new { p.Id, p.Origem, p.OrigemId, p.Descricao, p.Valor, p.Forma, p.Status, p.ReembolsoPendente, p.DataReferencia, atualizadoEm = DateTime.SpecifyKind(p.AtualizadoEm, DateTimeKind.Utc).ToString("O") })
                    .ToList()
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
            // Item 11.4b: validacao e gravacao (inclusive nome/telefone nos pets) em ContaClienteService.
            var (cliente, email) = await ContaClienteService.AtualizarPerfilAsync(db, (await Cliente()).Id, request.Nome, request.Telefone, request.Endereco);
            await realtime.NotificarAsync("clientes", "atualizado", new { cliente.Id, cliente.Nome });
            await Evento("cliente", "Perfil atualizado", cliente, cliente.Id);
            return OkApi(new { cliente.Nome, cliente.Telefone, cliente.Endereco, Email = email, message = "Perfil atualizado." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    // -----------------------------------------------------------------------
    // ACESSO DA CONTA — e-mail e senha (item 2 do roadmap, 24/09)
    //
    // As duas trocas exigem a SENHA ATUAL, mesmo com a sessao valida: quem
    // pegar um navegador esquecido logado nao consegue tomar a conta trocando
    // o e-mail ou a senha. O e-mail continua sendo a chave do login em
    // UsuarioCliente e segue unico.
    // -----------------------------------------------------------------------

    [HttpPut("conta/email")]
    public async Task<IActionResult> AlterarEmail([FromBody] AlterarEmailRequest request)
    {
        try
        {
            var cliente = await Cliente();
            // Item 11.4b: senha atual, unicidade e gravacao em ContaClienteService.
            var (emailAntigo, novo) = await ContaClienteService.AlterarEmailAsync(db, cliente, request.NovoEmail, request.SenhaAtual, Recusar);
            await realtime.NotificarAsync("clientes", "atualizado", new { cliente.Id });
            await Evento("seguranca", "E-mail de acesso alterado", cliente, cliente.Id, $"De {emailAntigo} para {novo}.", autor: novo);
            return OkApi(new { email = novo, message = "E-mail de acesso atualizado. Use o novo e-mail no próximo login." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    [HttpPut("conta/senha")]
    public async Task<IActionResult> AlterarSenha([FromBody] AlterarSenhaRequest request)
    {
        try
        {
            var cliente = await Cliente();
            // Item 11.4b: senha atual, regra de senha nova e gravacao em ContaClienteService.
            var email = await ContaClienteService.AlterarSenhaAsync(db, cliente, request.SenhaAtual, request.NovaSenha, request.ConfirmarSenha, Recusar);

            // Os outros aparelhos logados saem; este continua.
            sessions.EncerrarSessoesDoCliente(cliente.Id, Token());
            await Evento("seguranca", "Senha alterada", cliente, cliente.Id, "Outras sessões encerradas.", autor: email);
            return OkApi(new { message = "Senha alterada. Outros aparelhos conectados à sua conta foram desconectados." });
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
            await Evento("pet", "Pet cadastrado", cliente, pet.Id, pet.PetNome);
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
            await Evento("pet", "Pet atualizado", cliente, pet.Id, pet.PetNome);
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
            // Item 11.4: regra (quem pode cancelar, pagamento) em AgendamentosClienteService.
            var agendamento = await AgendamentosClienteService.CancelarAsync(db, cliente.Id, id);
            await realtime.NotificarAsync("agendamentos", "cancelado", new { agendamento.Id, agendamento.Dono });
            await Evento("agendamento", "Agendamento cancelado pelo cliente", cliente, agendamento.Id, $"{agendamento.Pet} · {agendamento.DataHora}", "aviso");
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
            // Item 11.4c: filtro pela conta e adocao dos contratos antigos em SegurosClienteService.
            var meus = await SegurosClienteService.MeusAsync(db, cliente);
            return OkApi(meus.Select(x => new
            {
                x.Seguro.Id,
                x.Seguro.PlanoSeguroId,
                x.Seguro.NomePlano,
                x.Seguro.NomePet,
                x.Seguro.PetId,
                x.Seguro.Observacao,
                x.Seguro.Status,
                CriadoEm = x.Seguro.CriadoEm.ToString("O"),
                x.Seguro.Valor,
                x.Seguro.MetodoPagamento,
                x.Seguro.PagamentoStatus,
                x.Seguro.CartaoFinal,
                DataCancelamento = x.Seguro.DataCancelamento.HasValue ? x.Seguro.DataCancelamento.Value.ToString("O") : null,
                // O botao "Cancelar seguro" so existe para contrato que ainda esta valendo.
                podeCancelar = SegurosClienteService.PodeCancelar(x.Seguro),
                // Detalhes do plano vem do catalogo: se o admin corrigir a
                // cobertura, o cliente ve a versao corrigida.
                valorMensal = x.Plano?.ValorMensal ?? 0m,
                coberturas = x.Plano?.Coberturas ?? "",
                beneficios = x.Plano?.Beneficios ?? "",
                condicoes = x.Plano?.Condicoes ?? "",
                planoAtivo = x.Plano?.Ativo ?? false
            }));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

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
            // Item 11.4c: plano do catalogo, pet da conta, forma de pagamento e cartao em SegurosClienteService.
            var solicitacao = await SegurosClienteService.ContratarAsync(db, cliente,
                request.PlanoId, request.PetId, request.MetodoPagamento, request.CartaoFinal, request.Observacao);
            await realtime.NotificarAsync("seguros", "solicitado", new { solicitacao.Id, solicitacao.NomePlano });
            await Evento("seguro", "Seguro contratado", cliente, solicitacao.Id, $"{solicitacao.NomePlano} · {solicitacao.NomePet} · {solicitacao.MetodoPagamento}");
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
            // Item 11.4c: regra do cancelamento (e do pagamento) em SegurosClienteService.
            var seguro = await SegurosClienteService.CancelarAsync(db, cliente.Id, id);
            await realtime.NotificarAsync("seguros", "cancelado", new { seguro.Id, seguro.NomePlano, seguro.NomeCliente });
            await Evento("seguro", "Seguro cancelado pelo cliente", cliente, seguro.Id, seguro.NomePlano, "aviso");
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

    /// <summary>Avaliacoes enviadas pelo proprio cliente, com o status da moderacao.</summary>
    [HttpGet("avaliacoes")]
    public async Task<IActionResult> MinhasAvaliacoes()
    {
        try
        {
            var cliente = await Cliente();
            return OkApi(await AvaliacoesService.DoClienteAsync(db, cliente.Id));   // item 11.4c
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
            // Item 11.4c: validacao e gravacao em AvaliacoesService.
            var depoimento = await AvaliacoesService.CriarDoClienteAsync(db, cliente, request.PetId, request.Avaliacao, request.Comentario);
            await realtime.NotificarAsync("avaliacoes", "criada", new { depoimento.Id, depoimento.NomeCliente });
            await Evento("cliente", "Avaliação enviada", cliente, depoimento.Id, $"{depoimento.Avaliacao} estrela(s)");
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
            // Item 6: so produto visivel na loja, com descricao e foto.
            .Where(p => p.VisivelLoja)
            .Select(p => new { p.Id, p.Nome, p.Categoria, p.ValorVenda, p.Estoque, p.ControlaEstoque, p.Descricao, p.FotoUrl }),
        // Item 5: cada unidade leva capacidade e servicos oferecidos ([] = todos),
        // para o wizard filtrar servicos pela unidade escolhida.
        unidades = (await db.Unidades.AsNoTracking().Where(u => u.Ativa).OrderBy(u => u.Nome).ToListAsync())
            .Select(u => new { u.Id, u.Nome, u.Endereco, u.Telefone, u.HorarioFuncionamento, u.Ativa, capacidade = UnidadesRegras.CapacidadeDe(u), servicos = UnidadesRegras.Servicos(u) })
    });
    [HttpGet("horarios")]
    public async Task<IActionResult> Horarios([FromQuery] string unidade, [FromQuery] string data)
    {
        try
        {
            // Item 11.4: grade e capacidade da unidade em AgendamentosClienteService.
            return OkApi(await AgendamentosClienteService.HorariosLivresAsync(db, unidade, data));
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
    [HttpPost("agendamentos")]
    public async Task<IActionResult> Agendar([FromBody] AgendamentoRequest request)
    {
        try
        {
            var cliente = await Cliente();
            // Item 11.4: validacao (pet da conta, unidade, servico, capacidade) e gravacao em AgendamentosClienteService.
            var (appointment, service) = await AgendamentosClienteService.CriarAsync(db, cliente, new(
                request.PetId, request.ServicoId, request.Unidade, request.Data, request.Horario,
                request.Transporte, request.FormaPagamento, request.Observacao));
            await realtime.NotificarAsync("agendamentos", "criado", new { appointment.Id, appointment.Dono, appointment.DataHora });
            await Evento("agendamento", "Agendamento criado pelo cliente", cliente, appointment.Id, $"{appointment.Pet} · {service.Nome} · {appointment.DataHora} · {appointment.Unidade}");
            return OkApi(appointment);
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
            // Item 11.4: validacao, baixa no livro de estoque e pagamento em PedidosService.
            var (pedido, produto, retirada, alerta) = await PedidosService.CriarDoClienteAsync(db, cliente,
                request.ProdutoId, request.Quantidade, request.FormaPagamento, request.Unidade);
            if (alerta is not null) await AlertarEstoqueAsync(alerta);
            await realtime.NotificarAsync("pedidos", "criado", new { pedido.Id, pedido.ProdutoNome, pedido.Quantidade, estoqueRestante = produto.ControlaEstoque ? produto.Estoque : (int?)null });
            await Evento("pedido", "Pedido criado", cliente, pedido.Id, $"{pedido.Quantidade}× {pedido.ProdutoNome} · {pedido.Total:C} · {pedido.FormaPagamento} · retirada em {retirada.Nome}");
            return OkApi(new { pedido.Id, pedido.ProdutoId, pedido.ProdutoNome, pedido.Quantidade, pedido.Total, pedido.FormaPagamento, pedido.Status, pedido.CriadoEm, pedido.Unidade, unidadeNome = retirada.Nome, estoqueRestante = produto.ControlaEstoque ? produto.Estoque : (int?)null });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }
    private string Token() => Request.Headers["X-LanePets-Client"].FirstOrDefault() ?? "";
    private async Task<Cliente> Cliente() { var session = sessions.RequireClient(Token()); return await db.Clientes.FindAsync(session.AdminToken) ?? throw new UnauthorizedAccessException("Cliente não localizado."); }
    public record CadastroRequest(string? Nome, string? Email, string? Senha, string? Telefone, string? Endereco, string? Pet, string? Tipo, string? Raca);
    public record LoginRequest(string? Email, string? Senha);
    public record AgendamentoRequest(string PetId, string ServicoId, string Unidade, string Data, string Horario, string? Transporte, string? FormaPagamento, string? Observacao);
    /// <summary>
    /// Itens 6/7: o cliente cancela o proprio pedido enquanto ele esta
    /// Pendente. O estoque baixado volta pelo livro de estoque.
    /// </summary>
    [HttpPost("pedidos/{id}/cancelar")]
    public async Task<IActionResult> CancelarPedido(string id)
    {
        try
        {
            var cliente = await Cliente();
            // Item 11.4: regra (so Pendente, devolucao ao estoque, pagamento) em PedidosService.
            var (pedido, devolvido) = await PedidosService.CancelarDoClienteAsync(db, cliente, id);
            await realtime.NotificarAsync("pedidos", "cancelado", new { pedido.Id });
            await Evento("pedido", "Pedido cancelado pelo cliente", cliente, pedido.Id,
                $"{pedido.Quantidade}× {pedido.ProdutoNome}" + (devolvido > 0 ? $" · {devolvido} unidade(s) devolvida(s) ao estoque" : ""), "aviso");
            return OkApi(new { pedido.Id, pedido.Status, devolvido, message = "Pedido cancelado." });
        }
        catch (Exception ex) { return ErrorApi(ex); }
    }

    private Task AlertarEstoqueAsync(EstoqueService.Alerta a) =>
        eventos.RegistrarAsync(new("estoque", a.Zerado ? "Produto sem estoque" : "Estoque baixo", "aviso", "sistema", "", "",
            a.ProdutoId, $"{a.ProdutoNome}: saldo {a.Saldo} (mínimo {a.Minimo})."), HttpContext);

    public record PedidoRequest(string ProdutoId, int Quantidade, string? FormaPagamento, string? Unidade = null);
    public record PerfilRequest(string? Nome, string? Telefone, string? Endereco);
    public record AlterarEmailRequest(string? NovoEmail, string? SenhaAtual);
    public record AlterarSenhaRequest(string? SenhaAtual, string? NovaSenha, string? ConfirmarSenha);
    /// <summary>
    /// A ficha que a tela envia. Note o que NAO esta aqui: id do cliente. O
    /// vinculo do pet com a conta nunca chega pelo corpo da requisicao.
    /// </summary>
    /// <summary>
    /// RemoverFoto e um pedido explicito de remocao da foto atual. FotoUrl
    /// vazio/ausente sozinho NUNCA remove uma foto ja salva -- ver
    /// PetFicha.ResolverFoto.
    /// </summary>
    public record PetRequest(
        string? Nome, string? Tipo, string? Raca, string? Sexo, string? DataNascimento,
        string? Peso, string? Cor, string? Porte, string? FotoUrl,
        string? Observacoes, string? NecessidadesEspeciais, string? InfoAtendimento,
        bool RemoverFoto = false)
    {
        public PetFicha.FichaEntrada ParaFicha() => new(
            Nome, Tipo, Raca, Sexo, DataNascimento, Peso, Cor, Porte, FotoUrl,
            Observacoes, NecessidadesEspeciais, InfoAtendimento, RemoverFoto);
    }
    public record AvaliacaoRequest(string? PetId, int Avaliacao, string? Comentario);
    public record SeguroContratacaoRequest(string PlanoId, string? PetId, string? MetodoPagamento, string? CartaoFinal, string? Observacao);
}
