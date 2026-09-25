namespace LanePets.Models;

// ---------------------------------------------------------------------------
// PET
//
// A entidade NAO foi recriada: Id, Dono, PetNome, Tipo, Raca, Telefone,
// Endereco, PacoteJson, Unidade e ClienteId sao exatamente os mesmos campos de
// antes, com os mesmos nomes e a mesma tabela. O painel administrativo, os
// agendamentos, os pacotes e o Seguro Pet continuam lendo o que sempre leram.
//
// As colunas abaixo foram acrescentadas para a area do cliente poder manter uma
// ficha completa do pet. Todas sao OPCIONAIS e nascem vazias, entao nenhum pet
// ja cadastrado deixa de ser valido:
//
//   Sexo                  -> "Macho", "Femea" ou "" (nao informado)
//   DataNascimento        -> data ISO "yyyy-MM-dd" ou "". A idade e calculada a
//                            partir dela, nunca digitada, entao nunca envelhece
//                            errado no banco.
//   Peso                  -> em quilos. 0 significa "nao informado".
//   Cor                   -> pelagem, texto livre curto
//   Porte                 -> "Pequeno", "Medio", "Grande" ou ""
//   FotoUrl               -> data URI (imagem reduzida no navegador). Vazio
//                            significa que a tela usa o simbolo da especie.
//   Observacoes           -> texto livre do tutor
//   NecessidadesEspeciais -> o que a equipe precisa saber antes do banho
//   InfoAtendimento       -> preferencias de manejo no atendimento
//
// Nao ha campo medico aqui: diagnostico, medicacao e vacina sao registro
// veterinario e nao cabem num cadastro preenchido pelo proprio tutor.
// ---------------------------------------------------------------------------
public class Pet
{
    public string Id { get; set; } = "";
    public string Dono { get; set; } = "";
    public string PetNome { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Raca { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string PacoteJson { get; set; } = "";
    public string Unidade { get; set; } = "";
    public string ClienteId { get; set; } = "";

    public string Sexo { get; set; } = "";
    public string DataNascimento { get; set; } = "";
    public decimal Peso { get; set; }
    public string Cor { get; set; } = "";
    public string Porte { get; set; } = "";
    public string FotoUrl { get; set; } = "";
    public string Observacoes { get; set; } = "";
    public string NecessidadesEspeciais { get; set; } = "";
    public string InfoAtendimento { get; set; } = "";
}
public class Cliente { public string Id { get; set; } = ""; public string Nome { get; set; } = ""; public string Telefone { get; set; } = ""; public string Endereco { get; set; } = ""; public string Observacoes { get; set; } = ""; public string Origem { get; set; } = ""; public string Status { get; set; } = ""; }
public class Agendamento { public string Id { get; set; } = ""; public string Pet { get; set; } = ""; public string Dono { get; set; } = ""; public string Telefone { get; set; } = ""; public string DataHora { get; set; } = ""; public string ServicosJson { get; set; } = "[]"; public decimal Total { get; set; } public string Transporte { get; set; } = ""; public decimal ValorTransporte { get; set; } public string Status { get; set; } = ""; public string PagamentoStatus { get; set; } = ""; public string FormaPagamento { get; set; } = ""; public string Obs { get; set; } = ""; public string Unidade { get; set; } = ""; public string ClienteId { get; set; } = ""; public string PetId { get; set; } = "";
    /* Item 4 (24/09): funcionario responsavel (Id de UsuarioAdministrador com perfil Funcionario da
       mesma unidade). Nunca vai para o JSON da entidade: o cliente recebe so o primeiro nome. */
    [System.Text.Json.Serialization.JsonIgnore] public string ResponsavelId { get; set; } = "";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public string ResponsavelNome { get; set; } = ""; }
public class Servico { public string Id { get; set; } = ""; public string Nome { get; set; } = ""; public decimal Preco { get; set; } public string Porte { get; set; } = ""; public string AdicionaisJson { get; set; } = "[]"; public string Pacote { get; set; } = ""; public string Adicional { get; set; } = ""; }
public class Produto { public string Id { get; set; } = ""; public string Codigo { get; set; } = ""; public string Nome { get; set; } = ""; public string Categoria { get; set; } = ""; public decimal ValorCompra { get; set; } public decimal ValorVenda { get; set; } public int Estoque { get; set; } public int EstoqueMinimo { get; set; } public bool ControlaEstoque { get; set; }
    /* Item 6 (24/09): descricao, foto (data URI via ImagemDataUri) e "Visivel na loja". */
    public string Descricao { get; set; } = ""; public string FotoUrl { get; set; } = ""; public bool VisivelLoja { get; set; } = true; }
public class EntradaSaida { public string Id { get; set; } = ""; public string Data { get; set; } = ""; public string Descricao { get; set; } = ""; public string Tipo { get; set; } = ""; public decimal Valor { get; set; } public string Unidade { get; set; } = ""; public string Origem { get; set; } = ""; }
public class Pacote { public string Id { get; set; } = ""; public string PetId { get; set; } = ""; public string Cliente { get; set; } = ""; public string Tipo { get; set; } = ""; public int Quantidade { get; set; } public int Utilizados { get; set; } public int Restantes { get; set; } public string DataInicio { get; set; } = ""; public string DataFim { get; set; } = ""; public string Status { get; set; } = ""; public string Unidade { get; set; } = ""; }
public class Configuracao { public string Chave { get; set; } = ""; public string Valor { get; set; } = ""; public string Descricao { get; set; } = ""; }
public class Auditoria { public string Id { get; set; } = ""; public DateTime DataHora { get; set; } public string Acao { get; set; } = ""; public string Entidade { get; set; } = ""; public string RegistroId { get; set; } = ""; public string Detalhes { get; set; } = ""; }
public class AuditoriaMigracao { public string Id { get; set; } = ""; public DateTime DataHora { get; set; } public string Tipo { get; set; } = ""; public string Entidade { get; set; } = ""; public string RegistroId { get; set; } = ""; public string Status { get; set; } = ""; public string Descricao { get; set; } = ""; public string ValorOriginal { get; set; } = ""; public string ValorSugerido { get; set; } = ""; }
public class ReconciliacaoMigracao { public string Id { get; set; } = ""; public DateTime DataHora { get; set; } public string AgendamentoId { get; set; } = ""; public string DonoHistorico { get; set; } = ""; public string PetHistorico { get; set; } = ""; public string TelefoneHistorico { get; set; } = ""; public string Classificacao { get; set; } = ""; public int Pontuacao { get; set; } public string CandidatoPetId { get; set; } = ""; public string CandidatoDono { get; set; } = ""; public string CandidatoPet { get; set; } = ""; public string CandidatoTelefone { get; set; } = ""; public string CandidatoClienteId { get; set; } = ""; public string Motivo { get; set; } = ""; public string SegundoCandidato { get; set; } = ""; public string AcaoRecomendada { get; set; } = ""; public string Observacao { get; set; } = ""; public string Status { get; set; } = ""; }
public class Depoimento { public string Id { get; set; } = ""; public string ClienteId { get; set; } = ""; public string NomeCliente { get; set; } = ""; public string NomePet { get; set; } = ""; public string Telefone { get; set; } = ""; public int Avaliacao { get; set; } public string Comentario { get; set; } = ""; public string Status { get; set; } = "Pendente"; public DateTime CriadoEm { get; set; } = DateTime.UtcNow; }
public class PlanoSeguro { public string Id { get; set; } = ""; public string Nome { get; set; } = ""; public string Descricao { get; set; } = ""; public string Coberturas { get; set; } = ""; public string Beneficios { get; set; } = ""; public string Condicoes { get; set; } = ""; public decimal ValorMensal { get; set; } public bool Ativo { get; set; } = true; }
// Contrato de seguro. ClienteId e PetId sao o que liga o contrato ao cadastro
// real; os campos de nome continuam gravados porque o historico precisa
// preservar como o cliente se chamava no momento da contratacao.
// Valor/MetodoPagamento/PagamentoStatus guardam a contratacao como ela foi
// fechada; CartaoFinal e SO os quatro ultimos digitos (nunca o numero
// completo e nunca o CVV, que nao chega a sair do navegador).
// DataCancelamento fica nula enquanto o contrato nao for cancelado: o
// cancelamento muda o Status e carimba a data, sem apagar o registro.
public class SolicitacaoSeguro { public string Id { get; set; } = ""; public string PlanoSeguroId { get; set; } = ""; public string NomePlano { get; set; } = ""; public string ClienteId { get; set; } = ""; public string PetId { get; set; } = ""; public string NomeCliente { get; set; } = ""; public string Telefone { get; set; } = ""; public string NomePet { get; set; } = ""; public string Observacao { get; set; } = ""; public string Status { get; set; } = "Pendente"; public DateTime CriadoEm { get; set; } = DateTime.UtcNow; public decimal Valor { get; set; } public string MetodoPagamento { get; set; } = ""; public string PagamentoStatus { get; set; } = "Pendente"; public string CartaoFinal { get; set; } = ""; public DateTime? DataCancelamento { get; set; } }
public class UsuarioCliente { public string Id { get; set; } = ""; public string ClienteId { get; set; } = ""; public string Email { get; set; } = ""; public string SenhaHash { get; set; } = ""; public string SenhaSalt { get; set; } = ""; public DateTime CriadoEm { get; set; } = DateTime.UtcNow; }
public class UsuarioAdministrador { public string Id { get; set; } = ""; public string Email { get; set; } = ""; public string SenhaHash { get; set; } = ""; public string SenhaSalt { get; set; } = ""; public bool Ativo { get; set; } = true; public DateTime CriadoEm { get; set; } = DateTime.UtcNow; public string Nome { get; set; } = ""; public string Telefone { get; set; } = ""; public string Perfil { get; set; } = "Admin"; public bool AcessoTotal { get; set; } public DateTime? UltimoAcesso { get; set; } public string Unidade { get; set; } = ""; }
// Unidade (item 5 do roadmap, 24/09): ganhou Capacidade e ServicosJson.
//   Capacidade   -> quantos atendimentos cabem no MESMO horario (padrao 1, o
//                   comportamento de antes). A agenda do cliente e do painel
//                   respeitam esse numero.
//   ServicosJson -> ids dos servicos oferecidos na unidade. "[]" = todos
//                   (padrao, entao unidade antiga continua oferecendo tudo).
// Unidade nao e excluida: sai de operacao com Ativa = false (historico intacto).
// Funcionarios NAO ficam aqui: sao os UsuariosAdministradores com Unidade = Id.
public class Unidade { public string Id { get; set; } = ""; public string Nome { get; set; } = ""; public string Endereco { get; set; } = ""; public string Telefone { get; set; } = ""; public string HorarioFuncionamento { get; set; } = ""; public bool Ativa { get; set; } = true; public int Capacidade { get; set; } = 1; public string ServicosJson { get; set; } = "[]"; }
public class Pedido { public string Id { get; set; } = ""; public string ClienteId { get; set; } = ""; public string ProdutoId { get; set; } = ""; public string ProdutoNome { get; set; } = ""; public int Quantidade { get; set; } public decimal Total { get; set; } public string FormaPagamento { get; set; } = ""; public string Status { get; set; } = "Pendente"; public DateTime CriadoEm { get; set; } = DateTime.UtcNow; public string Unidade { get; set; } = ""; }

// ---------------------------------------------------------------------------
// USUARIOS ADMINISTRATIVOS E PERMISSOES
//
// UsuarioAdministrador ja existia (Id, Email, SenhaHash, SenhaSalt, Ativo,
// CriadoEm) e continua sendo A MESMA entidade e A MESMA tabela: o login
// administrativo nao mudou. As colunas abaixo foram acrescentadas a ela:
//
//   Nome          -> exibicao na listagem
//   Telefone      -> contato, opcional
//   Perfil        -> "AdminGeral", "Admin" ou "Funcionario". E a identificacao real do
//                    Administrador Geral, gravada no banco. O sistema NUNCA
//                    decide isso comparando o e-mail.
//   AcessoTotal   -> atalho que libera tudo sem listar permissao por permissao
//   UltimoAcesso  -> carimbado no login bem-sucedido
//   Unidade       -> (24/09) id da unidade ("franco", "caieiras"). So tem
//                    efeito para Perfil = "Funcionario", que fica preso a ela.
//                    Vazio para administradores (veem todas as unidades).
//
// Nenhuma segunda entidade de usuario foi criada.
// ---------------------------------------------------------------------------

/// <summary>
/// Permissao de um administrador sobre um modulo, com as quatro acoes
/// separadas. Uma linha por (UsuarioAdminId, Modulo). A ausencia da linha
/// significa nenhum acesso ao modulo — o padrao de todo usuario novo.
/// </summary>
public class UsuarioAdminPermissao
{
    public string Id { get; set; } = "";
    public string UsuarioAdminId { get; set; } = "";
    public string Modulo { get; set; } = "";
    public bool PodeVisualizar { get; set; }
    public bool PodeCriar { get; set; }
    public bool PodeEditar { get; set; }
    public bool PodeExcluir { get; set; }
}

/// <summary>
/// Registro das acoes administrativas sensiveis (criacao de administrador,
/// alteracao de permissoes, ativacao/desativacao, acesso total, exclusao).
/// Tabela propria para nao misturar com a Auditoria de dados que ja existe.
/// </summary>
public class AuditoriaAdmin
{
    public string Id { get; set; } = "";
    public DateTime DataHora { get; set; } = DateTime.UtcNow;
    public string AutorId { get; set; } = "";
    public string AutorEmail { get; set; } = "";
    public string Acao { get; set; } = "";
    public string AlvoId { get; set; } = "";
    public string AlvoEmail { get; set; } = "";
    public string Detalhes { get; set; } = "";
}

// ---------------------------------------------------------------------------
// LOG DE EVENTOS (item 15 do roadmap, 24/09)
//
// Registro do que aconteceu no sistema para auditoria: login realizado e
// recusado, conta criada, pedido, agendamento, alteracoes do painel, acesso
// negado e erro interno. Tabela propria (EventosLog), separada da
// AuditoriaAdmin (que continua existindo e e espelhada aqui).
//
//   Nivel      -> "info", "aviso" ou "erro"
//   Categoria  -> "autenticacao", "cliente", "pet", "agendamento", "pedido",
//                 "produto", "servico", "financeiro", "seguro", "administracao",
//                 "seguranca", "sistema"
//   Origem     -> "cliente", "admin", "publico" ou "sistema"
//   Autor*     -> quem fez (id + e-mail/nome); vazio quando nao ha sessao
//   AlvoId     -> registro afetado (pedido, agendamento, produto...)
//   Referencia -> a mesma "Ref." mostrada ao usuario num ERR-5001
//
// NUNCA entra senha, token ou numero de cartao em nenhum campo.
// ---------------------------------------------------------------------------
public class EventoLog
{
    public string Id { get; set; } = "";
    public DateTime DataHora { get; set; } = DateTime.UtcNow;
    public string Nivel { get; set; } = "info";
    public string Categoria { get; set; } = "";
    public string Acao { get; set; } = "";
    public string Origem { get; set; } = "sistema";
    public string AutorId { get; set; } = "";
    public string Autor { get; set; } = "";
    public string AlvoId { get; set; } = "";
    public string Detalhes { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Referencia { get; set; } = "";
}


// ---------------------------------------------------------------------------
// MOVIMENTACAO DE ESTOQUE (item 7 do roadmap, 24/09)
//
// Livro de estoque: toda mudanca de saldo de um produto com controle de
// estoque gera UMA linha aqui, na mesma transacao que muda o saldo.
//   Tipo       -> entrada | saida | ajuste | venda | cancelamento
//   Quantidade -> variacao com sinal (+ entra, - sai)
// Somente insercao. Nunca apagada nem editada.
// ---------------------------------------------------------------------------
public class MovimentacaoEstoque
{
    public string Id { get; set; } = "";
    public DateTime DataHora { get; set; } = DateTime.UtcNow;
    public string ProdutoId { get; set; } = "";
    public string ProdutoNome { get; set; } = "";
    public string Tipo { get; set; } = "";
    public int Quantidade { get; set; }
    public int SaldoAnterior { get; set; }
    public int SaldoNovo { get; set; }
    public string Motivo { get; set; } = "";
    public string PedidoId { get; set; } = "";
    public string Origem { get; set; } = "admin";
    public string AutorId { get; set; } = "";
    public string Autor { get; set; } = "";
}


// ---------------------------------------------------------------------------
// PAGAMENTO (item 8 do roadmap, 25/09)
//
// Um pagamento por origem: agendamento, pedido da loja ou contratacao de
// seguro (Origem + OrigemId, unicos). Status: Pendente, Aprovado, Recusado,
// Cancelado, Reembolsado. Os campos antigos (Agendamento.PagamentoStatus,
// SolicitacaoSeguro.PagamentoStatus) continuam existindo e sao ESPELHADOS a
// partir daqui, para as telas e relatorios antigos nao mudarem de numero.
// ---------------------------------------------------------------------------
public class Pagamento
{
    public string Id { get; set; } = "";
    public string Origem { get; set; } = "";          // agendamento | pedido | seguro
    public string OrigemId { get; set; } = "";
    public string ClienteId { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Descricao { get; set; } = "";
    public decimal Valor { get; set; }
    public string Forma { get; set; } = "";
    public string Status { get; set; } = "Pendente";
    public bool ReembolsoPendente { get; set; }
    public string Unidade { get; set; } = "";
    public string DataReferencia { get; set; } = "";  // data do atendimento / do pedido / da contratacao
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
    public string AtualizadoPor { get; set; } = "";
    public string Observacao { get; set; } = "";
}
