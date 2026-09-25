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
public class Agendamento { public string Id { get; set; } = ""; public string Pet { get; set; } = ""; public string Dono { get; set; } = ""; public string Telefone { get; set; } = ""; public string DataHora { get; set; } = ""; public string ServicosJson { get; set; } = "[]"; public decimal Total { get; set; } public string Transporte { get; set; } = ""; public decimal ValorTransporte { get; set; } public string Status { get; set; } = ""; public string PagamentoStatus { get; set; } = ""; public string FormaPagamento { get; set; } = ""; public string Obs { get; set; } = ""; public string Unidade { get; set; } = ""; public string ClienteId { get; set; } = ""; public string PetId { get; set; } = ""; }
public class Servico { public string Id { get; set; } = ""; public string Nome { get; set; } = ""; public decimal Preco { get; set; } public string Porte { get; set; } = ""; public string AdicionaisJson { get; set; } = "[]"; public string Pacote { get; set; } = ""; public string Adicional { get; set; } = ""; }
public class Produto { public string Id { get; set; } = ""; public string Codigo { get; set; } = ""; public string Nome { get; set; } = ""; public string Categoria { get; set; } = ""; public decimal ValorCompra { get; set; } public decimal ValorVenda { get; set; } public int Estoque { get; set; } public int EstoqueMinimo { get; set; } public bool ControlaEstoque { get; set; } }
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
public class UsuarioAdministrador { public string Id { get; set; } = ""; public string Email { get; set; } = ""; public string SenhaHash { get; set; } = ""; public string SenhaSalt { get; set; } = ""; public bool Ativo { get; set; } = true; public DateTime CriadoEm { get; set; } = DateTime.UtcNow; public string Nome { get; set; } = ""; public string Telefone { get; set; } = ""; public string Perfil { get; set; } = "Admin"; public bool AcessoTotal { get; set; } public DateTime? UltimoAcesso { get; set; } }
public class Unidade { public string Id { get; set; } = ""; public string Nome { get; set; } = ""; public string Endereco { get; set; } = ""; public string Telefone { get; set; } = ""; public string HorarioFuncionamento { get; set; } = ""; public bool Ativa { get; set; } = true; }
public class Pedido { public string Id { get; set; } = ""; public string ClienteId { get; set; } = ""; public string ProdutoId { get; set; } = ""; public string ProdutoNome { get; set; } = ""; public int Quantidade { get; set; } public decimal Total { get; set; } public string FormaPagamento { get; set; } = ""; public string Status { get; set; } = "Pendente"; public DateTime CriadoEm { get; set; } = DateTime.UtcNow; }

// ---------------------------------------------------------------------------
// USUARIOS ADMINISTRATIVOS E PERMISSOES
//
// UsuarioAdministrador ja existia (Id, Email, SenhaHash, SenhaSalt, Ativo,
// CriadoEm) e continua sendo A MESMA entidade e A MESMA tabela: o login
// administrativo nao mudou. As colunas abaixo foram acrescentadas a ela:
//
//   Nome          -> exibicao na listagem
//   Telefone      -> contato, opcional
//   Perfil        -> "AdminGeral" ou "Admin". E a identificacao real do
//                    Administrador Geral, gravada no banco. O sistema NUNCA
//                    decide isso comparando o e-mail.
//   AcessoTotal   -> atalho que libera tudo sem listar permissao por permissao
//   UltimoAcesso  -> carimbado no login bem-sucedido
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
