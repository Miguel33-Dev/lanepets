using System.Globalization;
using System.Text;
using LanePets.Models;

namespace LanePets.Services;

/// <summary>
/// Validacao e normalizacao da ficha do pet.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// O cadastro e a edicao de pet na area do cliente passaram a aceitar muito
/// mais do que nome/tipo/raca. Se cada endpoint validasse por conta propria,
/// criar e editar aceitariam coisas diferentes — e e exatamente ai que entra
/// dado invalido no banco. Entao existe UM lugar que decide o que e um pet
/// valido, e os dois endpoints chamam ele.
///
/// A validacao do navegador NAO conta. Ela existe para a experiencia (o cliente
/// ve o erro no campo, na hora), mas qualquer requisicao pode chegar sem passar
/// por tela nenhuma. O que vale e o que esta aqui.
///
/// As mensagens sao escritas para o cliente ler: "Informe o nome do pet.", e
/// nao "400 Bad Request".
/// </summary>
public static class PetFicha
{
    /// <summary>Erro de preenchimento com mensagem pronta para a tela.</summary>
    public sealed class ValidacaoException(string mensagem) : Exception(mensagem) { }

    public const int LimiteNome = 40;
    public const int LimiteRaca = 40;
    public const int LimiteCor = 30;
    public const int LimiteTexto = 500;

    /// <summary>
    /// Teto da foto guardada. A tela ja reduz a imagem antes de enviar; este
    /// limite existe porque a tela nao e a unica coisa que pode chamar a API.
    /// 200 KB de data URI e folgado para uma foto de 320px de lado.
    /// </summary>
    public const int LimiteFotoBytes = 200 * 1024;

    private static readonly string[] EspeciesAceitas = ["Cachorro", "Gato", "Outro"];
    private static readonly string[] SexosAceitos = ["Macho", "Fêmea"];
    private static readonly string[] PortesAceitos = ["Pequeno", "Médio", "Grande"];

    /// <summary>
    /// Aplica a ficha recebida sobre o pet, validando tudo antes de encostar na
    /// entidade. Se algo estiver errado, a excecao sobe ANTES de qualquer
    /// atribuicao — um pet nunca fica meio preenchido por causa de um campo
    /// invalido no fim do formulario.
    ///
    /// Id, ClienteId, Dono e Telefone NAO sao tocados aqui de proposito: quem
    /// manda neles e o vinculo do pet com a conta, nunca o corpo da requisicao.
    /// </summary>
    public static void Aplicar(Pet pet, FichaEntrada entrada)
    {
        var nome = Limpar(entrada.Nome);
        if (nome.Length < 2) throw new ValidacaoException("Informe o nome do pet (mínimo 2 letras).");
        if (nome.Length > LimiteNome) throw new ValidacaoException($"O nome do pet pode ter até {LimiteNome} caracteres.");

        var especie = Limpar(entrada.Tipo);
        if (especie.Length == 0) throw new ValidacaoException("Escolha a espécie do pet.");
        especie = EspeciesAceitas.FirstOrDefault(e => e.Equals(especie, StringComparison.OrdinalIgnoreCase))
                  ?? throw new ValidacaoException("Espécie inválida. Escolha Cachorro, Gato ou Outro.");

        var raca = Limpar(entrada.Raca);
        if (raca.Length > LimiteRaca) throw new ValidacaoException($"A raça pode ter até {LimiteRaca} caracteres.");

        var sexo = Limpar(entrada.Sexo);
        if (sexo.Length > 0)
        {
            sexo = SexosAceitos.FirstOrDefault(s => s.Equals(sexo, StringComparison.OrdinalIgnoreCase)
                                                    || Sem(s).Equals(Sem(sexo), StringComparison.OrdinalIgnoreCase))
                   ?? throw new ValidacaoException("Sexo inválido. Escolha Macho ou Fêmea.");
        }

        var porte = Limpar(entrada.Porte);
        if (porte.Length > 0)
        {
            porte = PortesAceitos.FirstOrDefault(p => p.Equals(porte, StringComparison.OrdinalIgnoreCase)
                                                     || Sem(p).Equals(Sem(porte), StringComparison.OrdinalIgnoreCase))
                    ?? throw new ValidacaoException("Porte inválido. Escolha Pequeno, Médio ou Grande.");
        }

        var nascimento = ValidarNascimento(entrada.DataNascimento);
        var peso = ValidarPeso(entrada.Peso);

        var cor = Limpar(entrada.Cor);
        if (cor.Length > LimiteCor) throw new ValidacaoException($"A cor pode ter até {LimiteCor} caracteres.");

        var foto = ValidarFoto(entrada.FotoUrl);

        var observacoes = Limpar(entrada.Observacoes);
        if (observacoes.Length > LimiteTexto) throw new ValidacaoException($"As observações podem ter até {LimiteTexto} caracteres.");
        var necessidades = Limpar(entrada.NecessidadesEspeciais);
        if (necessidades.Length > LimiteTexto) throw new ValidacaoException($"As necessidades especiais podem ter até {LimiteTexto} caracteres.");
        var atendimento = Limpar(entrada.InfoAtendimento);
        if (atendimento.Length > LimiteTexto) throw new ValidacaoException($"As informações para o atendimento podem ter até {LimiteTexto} caracteres.");

        // A partir daqui nada mais pode falhar: so entao a entidade muda.
        pet.PetNome = nome;
        pet.Tipo = especie;
        pet.Raca = raca;
        pet.Sexo = sexo;
        pet.DataNascimento = nascimento;
        pet.Peso = peso;
        pet.Cor = cor;
        pet.Porte = porte;
        pet.FotoUrl = foto;
        pet.Observacoes = observacoes;
        pet.NecessidadesEspeciais = necessidades;
        pet.InfoAtendimento = atendimento;
    }

    /// <summary>
    /// Data de nascimento em ISO "yyyy-MM-dd". Vazia e valida: nem todo tutor
    /// sabe a data (pet adotado, resgatado). O que nao pode passar e data no
    /// futuro nem data absurda — 40 anos cobre qualquer caso real com folga.
    /// </summary>
    private static string ValidarNascimento(string? valor)
    {
        var texto = Limpar(valor);
        if (texto.Length == 0) return "";
        if (!DateTime.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
            throw new ValidacaoException("Data de nascimento inválida. Use o seletor de data.");
        var hoje = DateTime.UtcNow.Date;
        if (data.Date > hoje) throw new ValidacaoException("A data de nascimento não pode estar no futuro.");
        if (data.Date < hoje.AddYears(-40)) throw new ValidacaoException("Confira a data de nascimento: ela está muito antiga.");
        return data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Peso em quilos. 0 (ou vazio) significa "nao informado" e e aceito. O teto
    /// de 150 kg descarta erro de digitacao sem barrar pet nenhum.
    /// </summary>
    private static decimal ValidarPeso(string? valor)
    {
        var texto = Limpar(valor).Replace(',', '.');
        if (texto.Length == 0) return 0m;
        if (!decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var peso))
            throw new ValidacaoException("Peso inválido. Informe apenas números, por exemplo 12,5.");
        if (peso < 0) throw new ValidacaoException("O peso não pode ser negativo.");
        if (peso > 150) throw new ValidacaoException("Confira o peso: o valor informado está acima do esperado.");
        return Math.Round(peso, 2);
    }

    /// <summary>
    /// A foto chega como data URI, ja reduzida pelo navegador. Aqui a API
    /// confere que e mesmo imagem, que o formato e um dos tres que o navegador
    /// gera, e que o tamanho cabe. Um arquivo que nao e imagem nunca vira foto
    /// de pet so porque o nome terminava em .jpg.
    /// </summary>
    private static string ValidarFoto(string? valor)
    {
        var texto = (valor ?? "").Trim();
        if (texto.Length == 0) return "";
        if (!texto.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            throw new ValidacaoException("Formato de imagem não reconhecido. Envie um arquivo JPG, PNG ou WebP.");

        var virgula = texto.IndexOf(',');
        if (virgula < 0) throw new ValidacaoException("Não foi possível ler a imagem. Tente enviar outra foto.");

        var cabecalho = texto[..virgula].ToLowerInvariant();
        if (!cabecalho.Contains(";base64"))
            throw new ValidacaoException("Não foi possível ler a imagem. Tente enviar outra foto.");

        var tipo = cabecalho["data:".Length..].Split(';')[0];
        if (tipo is not ("image/jpeg" or "image/png" or "image/webp"))
            throw new ValidacaoException("Formato de imagem não aceito. Envie um arquivo JPG, PNG ou WebP.");

        if (texto.Length > LimiteFotoBytes)
            throw new ValidacaoException("A foto ficou grande demais. Escolha uma imagem menor.");

        var conteudo = texto[(virgula + 1)..];
        Span<byte> buffer = new byte[((conteudo.Length * 3) / 4) + 4];
        if (!Convert.TryFromBase64String(conteudo, buffer, out var escritos) || escritos == 0)
            throw new ValidacaoException("Não foi possível ler a imagem. Tente enviar outra foto.");

        return texto;
    }

    private static string Limpar(string? valor) => (valor ?? "").Trim();

    /// <summary>Compara ignorando acento, para "Femea" casar com "Fêmea".</summary>
    private static string Sem(string valor) =>
        string.Concat(valor.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));

    /// <summary>
    /// O que a tela manda. Nao ha ClienteId aqui de proposito: o vinculo do pet
    /// com a conta nao e um campo que o cliente preenche.
    /// </summary>
    public sealed record FichaEntrada(
        string? Nome, string? Tipo, string? Raca, string? Sexo, string? DataNascimento,
        string? Peso, string? Cor, string? Porte, string? FotoUrl,
        string? Observacoes, string? NecessidadesEspeciais, string? InfoAtendimento);
}
