namespace LanePets.Services;

/// <summary>Muitas tentativas erradas seguidas: a API responde 429 (ERR-4290) com esta mensagem.</summary>
public sealed class MuitasTentativasException(string mensagem, int segundos) : Exception(mensagem)
{
    public int Segundos { get; } = segundos;
}

/// <summary>
/// Seguranca — Etapa 2 da auditoria (29/09): trava contra forca bruta.
///
/// Conta cada senha errada por CONTA (ex.: "admin:email", "cliente:email", "financeiro:usuarioId") e por IP.
/// Passou do limite dentro da janela, aquela conta (ou IP) fica bloqueada ate a janela acabar — inclusive
/// para a senha CERTA, senao o ataque so precisaria continuar ate acertar. Acertar a senha zera a conta.
///
/// A chave e o e-mail digitado, exista a conta ou nao: a resposta de bloqueio e a mesma para os dois
/// casos (nao revela quem e cliente). Fica em memoria, como as sessoes: reiniciar o app zera os contadores.
///
/// Configuracao (LanePets:Seguranca): TentativasPorConta (5), TentativasPorIp (30), BloqueioMinutos (15).
/// </summary>
public sealed class TentativasLogin(IConfiguration config)
{
    private readonly object trava = new();
    private readonly Dictionary<string, List<DateTime>> falhas = new();

    private int LimiteConta => Math.Max(1, config.GetValue("LanePets:Seguranca:TentativasPorConta", 5));
    private int LimiteIp => Math.Max(1, config.GetValue("LanePets:Seguranca:TentativasPorIp", 30));
    private TimeSpan Janela => TimeSpan.FromMinutes(Math.Max(1, config.GetValue("LanePets:Seguranca:BloqueioMinutos", 15)));

    private static string ChaveIp(string? ip) => "ip:" + (string.IsNullOrWhiteSpace(ip) ? "local" : ip);

    /// <summary>Lanca MuitasTentativasException se a conta ou o IP estiverem bloqueados.</summary>
    public void Conferir(string conta, string? ip)
    {
        var agora = DateTime.UtcNow;
        lock (trava)
        {
            var espera = Math.Max(Espera(conta.ToLowerInvariant(), LimiteConta, agora), Espera(ChaveIp(ip), LimiteIp, agora));
            if (espera > 0)
            {
                var minutos = (int)Math.Ceiling(espera / 60.0);
                throw new MuitasTentativasException(
                    $"Muitas tentativas erradas. Por segurança, aguarde {minutos} minuto{(minutos == 1 ? "" : "s")} e tente de novo.",
                    espera);
            }
        }
    }

    /// <summary>Registra uma senha errada. Devolve true quando ESTA falha acabou de bloquear a conta (para virar evento).</summary>
    public bool Falhou(string conta, string? ip)
    {
        var agora = DateTime.UtcNow;
        lock (trava)
        {
            var chave = conta.ToLowerInvariant();
            Registrar(ChaveIp(ip), agora);
            return Registrar(chave, agora) == LimiteConta;
        }
    }

    /// <summary>Senha certa: a conta volta a zero (o contador do IP continua, para quem testa muitas contas).</summary>
    public void Acertou(string conta)
    {
        lock (trava) falhas.Remove(conta.ToLowerInvariant());
    }

    private int Registrar(string chave, DateTime agora)
    {
        if (!falhas.TryGetValue(chave, out var lista)) falhas[chave] = lista = new List<DateTime>();
        lista.RemoveAll(t => agora - t >= Janela);
        lista.Add(agora);
        if (falhas.Count > 50_000) Limpar(agora);   // nunca cresce sem limite
        return lista.Count;
    }

    /// <summary>Segundos que ainda faltam para liberar (0 = liberado).</summary>
    private int Espera(string chave, int limite, DateTime agora)
    {
        if (!falhas.TryGetValue(chave, out var lista)) return 0;
        lista.RemoveAll(t => agora - t >= Janela);
        if (lista.Count < limite) return 0;
        var libera = lista[^limite] + Janela;
        return Math.Max(1, (int)Math.Ceiling((libera - agora).TotalSeconds));
    }

    private void Limpar(DateTime agora)
    {
        foreach (var chave in falhas.Where(p => p.Value.All(t => agora - t >= Janela)).Select(p => p.Key).ToList())
            falhas.Remove(chave);
    }
}
