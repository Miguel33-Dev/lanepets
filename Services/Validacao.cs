using System.Text.RegularExpressions;

namespace LanePets.Services;

/// <summary>
/// REGRAS DE VALIDACAO COMPARTILHADAS (item 13 do roadmap, 24/09).
///
/// Um lugar so para o que se repete entre telas: e-mail, telefone, senha e
/// os numeros do produto. Cada metodo devolve o valor ja limpo/normalizado ou
/// lanca ValidacaoException com a mensagem pronta para o usuario ler (vira
/// HTTP 400 pelo ApiControllerBase). Valida-se ANTES de encostar na entidade.
///
/// Senha: minimo 8 caracteres com pelo menos uma letra e um numero, igual para
/// cliente e administrador. A regra vale para senha NOVA (cadastro, troca,
/// redefinicao); o login continua aceitando as senhas antigas ja gravadas.
/// </summary>
public static partial class Validacao
{
    public const int SenhaMinimo = 8;
    public const int SenhaMaximo = 128;
    public const int EmailMaximo = 160;

    public sealed class ValidacaoException(string mensagem) : Exception(mensagem) { }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")]
    private static partial Regex EmailRegex();

    /// <summary>E-mail obrigatorio, em minusculas e sem espacos nas pontas.</summary>
    public static string Email(string? valor)
    {
        var email = (valor ?? "").Trim().ToLowerInvariant();
        if (email.Length == 0) throw new ValidacaoException("Informe o e-mail.");
        if (email.Length > EmailMaximo || !EmailRegex().IsMatch(email))
            throw new ValidacaoException("Informe um e-mail válido, como nome@exemplo.com.");
        return email;
    }

    /// <summary>
    /// Telefone brasileiro: 10 digitos (fixo com DDD) ou 11 (celular com DDD).
    /// A formatacao digitada e preservada; so os digitos sao conferidos.
    /// Com obrigatorio=false, vazio passa (campo opcional).
    /// </summary>
    public static string Telefone(string? valor, bool obrigatorio = true)
    {
        var texto = (valor ?? "").Trim();
        if (texto.Length == 0)
        {
            if (obrigatorio) throw new ValidacaoException("Informe um telefone com DDD.");
            return "";
        }
        if (texto.Any(c => !char.IsDigit(c) && " ()-+.".IndexOf(c) < 0))
            throw new ValidacaoException("O telefone deve ter só números, como (11) 99999-9999.");
        var digitos = new string(texto.Where(char.IsDigit).ToArray());
        if (digitos.StartsWith("55") && digitos.Length is 12 or 13) digitos = digitos[2..];
        if (digitos.Length is not (10 or 11))
            throw new ValidacaoException("Informe um telefone com DDD, com 10 ou 11 dígitos.");
        return texto;
    }

    /// <summary>Regra de senha nova. Nunca devolve nem registra a senha.</summary>
    public static void Senha(string? senha, string? confirmacao = null)
    {
        var s = senha ?? "";
        if (s.Length < SenhaMinimo)
            throw new ValidacaoException($"A senha precisa ter ao menos {SenhaMinimo} caracteres.");
        if (s.Length > SenhaMaximo)
            throw new ValidacaoException($"A senha pode ter no máximo {SenhaMaximo} caracteres.");
        if (!s.Any(char.IsLetter) || !s.Any(char.IsDigit))
            throw new ValidacaoException("A senha precisa ter pelo menos uma letra e um número.");
        if (s.Trim() != s)
            throw new ValidacaoException("A senha não pode começar nem terminar com espaço.");
        if (confirmacao is not null && confirmacao != s)
            throw new ValidacaoException("A confirmação de senha não confere.");
    }

    /// <summary>
    /// Numeros do produto: nome obrigatorio, preco de venda maior que zero,
    /// custo e estoques nunca negativos.
    /// </summary>
    public static void Produto(string nome, decimal valorVenda, decimal valorCompra, int? estoque, int? estoqueMinimo)
    {
        if (string.IsNullOrWhiteSpace(nome)) throw new ValidacaoException("Informe o nome do produto.");
        if (nome.Trim().Length > 120) throw new ValidacaoException("O nome do produto pode ter até 120 caracteres.");
        if (valorVenda <= 0) throw new ValidacaoException($"O preço de venda de \"{nome.Trim()}\" precisa ser maior que zero.");
        if (valorCompra < 0) throw new ValidacaoException($"O valor de compra de \"{nome.Trim()}\" não pode ser negativo.");
        if (estoque is < 0) throw new ValidacaoException($"O estoque de \"{nome.Trim()}\" não pode ser negativo.");
        if (estoqueMinimo is < 0) throw new ValidacaoException($"O estoque mínimo de \"{nome.Trim()}\" não pode ser negativo.");
    }
}
