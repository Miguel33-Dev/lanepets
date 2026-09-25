namespace LanePets.Services;

/// <summary>
/// Item 4 do roadmap (24/09): status do agendamento.
///
///   Solicitado -> Confirmado -> Em andamento -> Concluído      (+ Cancelado)
///
/// Os nomes antigos (Pendente, Em processo, Pronto, Entregue) sao convertidos
/// uma vez no banco pelo SeedService e, por seguranca, continuam sendo
/// ACEITOS na entrada (tela antiga em cache, importacao de CSV) — sempre
/// traduzidos para o nome novo antes de gravar. Nada grava nome antigo.
/// </summary>
public static class StatusAgendamento
{
    public const string Solicitado = "Solicitado";
    public const string Confirmado = "Confirmado";
    public const string EmAndamento = "Em andamento";
    public const string Concluido = "Concluído";
    public const string Cancelado = "Cancelado";

    public static readonly string[] Todos = [Solicitado, Confirmado, EmAndamento, Concluido, Cancelado];

    /// <summary>Nome oficial do status, ou "" quando o texto nao e um status conhecido.</summary>
    public static string Normalizar(string? valor) => Normalizador.Texto(valor) switch
    {
        "solicitado" or "pendente" or "aguardando" or "agendado" => Solicitado,
        "confirmado" => Confirmado,
        "em andamento" or "em_andamento" or "andamento" or "iniciado" or "em processo" or "em atendimento" => EmAndamento,
        "concluido" or "pronto" or "entregue" or "finalizado" or "finalizado/entregue" => Concluido,
        "cancelado" => Cancelado,
        _ => ""
    };

    /// <summary>Status para exibir: vazio (registro muito antigo) vira Solicitado.</summary>
    public static string Exibir(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? Solicitado : (Normalizar(valor) is { Length: > 0 } s ? s : valor!.Trim());

    /// <summary>
    /// Valida o status que chegou do painel. Vazio = mantem o atual (ou Solicitado
    /// no cadastro). Texto desconhecido e recusado com mensagem para o usuario.
    /// </summary>
    public static string Validar(string? valor, string atual)
    {
        if (string.IsNullOrWhiteSpace(valor)) return string.IsNullOrWhiteSpace(atual) ? Solicitado : Exibir(atual);
        var s = Normalizar(valor);
        if (s.Length == 0)
            throw new Exception($"Status \"{valor!.Trim()}\" inválido. Use: {string.Join(", ", Todos)}.");
        return s;
    }

    public static bool EhCancelado(string? valor) => Normalizar(valor) == Cancelado;
    public static bool EhConcluido(string? valor) => Normalizar(valor) == Concluido;

    /// <summary>O cliente so cancela enquanto o atendimento nao comecou.</summary>
    public static bool ClientePodeCancelar(string? valor)
        => Exibir(valor) is Solicitado or Confirmado;
}
