using System.Text.Json;
using LanePets.Models;

namespace LanePets.Services;

/// <summary>
/// Formato das telas antigas do painel (nomes proprios de campo: pet, dono...). Saiu do
/// AdminStoreController em 27/09 porque o /api/admin/estado e as listas paginadas de Clientes
/// e Pets precisam devolver o MESMO objeto — e o objeto que a tela manda de volta pelo sync
/// ("atualizar regrava o registro inteiro"). Classe static: so traducao, sem banco.
/// </summary>
public static class FormatoPainel
{
    public static object Agendamento(Agendamento a, IReadOnlyDictionary<string, string>? responsaveis = null) => new
    {
        id = a.Id,
        unidade = Normalizador.IdUnidade(a.Unidade),
        pet = a.Pet,
        dono = a.Dono,
        telefone = a.Telefone,
        dataHora = a.DataHora,
        servicos = Lista(a.ServicosJson),
        total = a.Total,
        // A tela trabalha com um "sim/nao" de transporte; o banco guarda a
        // descricao ("Busca e entrega", "Cliente leva"). Os dois vao juntos.
        transporte = TemTransporte(a.Transporte, a.ValorTransporte),
        transporteDescricao = a.Transporte,
        valorTransporte = a.ValorTransporte,
        status = StatusAgendamento.Exibir(a.Status),
        responsavelId = a.ResponsavelId,
        responsavelNome = a.ResponsavelId.Length > 0 && responsaveis is not null && responsaveis.TryGetValue(a.ResponsavelId, out var resp) ? resp : "",
        pagamentoStatus = string.IsNullOrWhiteSpace(a.PagamentoStatus) ? "A pagar" : a.PagamentoStatus,
        formaPagamento = a.FormaPagamento,
        obs = a.Obs,
        clienteId = a.ClienteId,
        petId = a.PetId
    };

    public static object Servico(Servico s) => new
    {
        id = s.Id,
        nome = s.Nome,
        preco = s.Preco,
        porte = s.Porte,
        adicionais = Lista(s.AdicionaisJson),
        pacote = s.Pacote,
        adicional = s.Adicional
    };

    /// <summary>
    /// A tela trabalha com um sim/nao. O banco tem historico gravado de duas
    /// formas: como descricao ("Busca e entrega") e, nos registros importados
    /// do navegador, como o texto "True"/"False". As duas sao aceitas aqui.
    /// </summary>
    public static bool TemTransporte(string? descricao, decimal valor)
    {
        if (valor > 0) return true;
        return !SyncPainelService.SemTransporte.Contains(Normalizador.Texto(descricao));
    }

    /// <summary>JSON gravado como texto -> valor (lista vazia quando vazio ou invalido).</summary>
    public static object Lista(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return Array.Empty<object>();
        try { return JsonSerializer.Deserialize<JsonElement>(bruto); }
        catch { return Array.Empty<object>(); }
    }

    /// <summary>Nomes dos servicos gravados no agendamento (ServicosJson = [{ nome, ... }]).</summary>
    public static List<string> NomesServicos(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return [];
        try
        {
            var v = JsonSerializer.Deserialize<JsonElement>(bruto);
            if (v.ValueKind != JsonValueKind.Array) return [];
            return v.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("nome", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "")
                .Where(n => n.Length > 0).ToList();
        }
        catch { return []; }
    }

    public static object Pet(Pet p) => new
    {
        id = p.Id,
        dono = p.Dono,
        pet = p.PetNome,
        tipo = p.Tipo,
        raca = p.Raca,
        telefone = p.Telefone,
        endereco = p.Endereco,
        unidade = Normalizador.IdUnidade(p.Unidade),
        pacote = Objeto(p.PacoteJson),
        clienteId = p.ClienteId,
        // Campos da ficha que a area do cliente ja gravava e que o painel
        // precisa para mostrar o pet nos detalhes do agendamento. Acrescimo
        // SOMENTE de leitura: AtualizarPet nao escreve nenhum deles, entao
        // salvar um pet pelo painel continua sem poder apaga-los.
        fotoUrl = p.FotoUrl,
        sexo = p.Sexo,
        dataNascimento = p.DataNascimento,
        peso = p.Peso,
        cor = p.Cor,
        porte = p.Porte,
        observacoes = p.Observacoes,
        necessidadesEspeciais = p.NecessidadesEspeciais
    };

    /// <summary>Cliente como a tela Clientes usa (conta do site + contagens ja calculadas).</summary>
    public static object Cliente(Cliente c, UsuarioCliente? conta, int qtdPets, int qtdAgendamentos, int qtdPedidos) => new
    {
        id = c.Id,
        nome = c.Nome,
        telefone = c.Telefone,
        endereco = c.Endereco,
        observacoes = c.Observacoes,
        origem = string.IsNullOrWhiteSpace(c.Origem) ? "cadastro_painel" : c.Origem,
        status = string.IsNullOrWhiteSpace(c.Status) ? "ativo" : c.Status,
        email = conta?.Email ?? "",
        temConta = conta is not null,
        criadoEm = conta?.CriadoEm.ToString("O") ?? "",
        qtdPets,
        qtdAgendamentos,
        qtdPedidos
    };

    /// <summary>Pacote do pet: sempre objeto (vazio = {}). Devolver [] fazia o painel
    /// marcar "ativo" num array, que o JSON.stringify descarta — o pacote nunca gravava.</summary>
    public static object Objeto(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto)) return new { };
        try
        {
            var valor = JsonSerializer.Deserialize<JsonElement>(bruto);
            return valor.ValueKind == JsonValueKind.Object ? valor : new { };
        }
        catch { return new { }; }
    }
}
