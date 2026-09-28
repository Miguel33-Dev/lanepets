using System.Globalization;
using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// 28/09: e-mails para o tutor (reusam o <see cref="EmailService"/> da recuperacao de senha).
///
///   Boas-vindas ........... ao criar a conta pelo site (sempre).
///   Agendamento recebido .. quando o proprio cliente agenda (sempre: e o comprovante do pedido).
///   Confirmado / Cancelado  quando a EQUIPE muda o status pelo painel (respeita "Receber avisos").
///   Lembrete da vespera ... pela rotina de hora em hora, a partir das 9h do dia anterior
///                           (respeita "Receber avisos"; uma vez so: LembreteEnviadoEm).
///
/// So vai e-mail para quem tem conta no site (UsuarioCliente). Nunca lanca: falha de envio
/// fica no log do servidor (EmailService) e a operacao que disparou o aviso continua.
/// Classe static (§6.34): monta o texto e decide para quem vai; o envio e do EmailService.
/// </summary>
public static class AvisosClienteService
{
    public const int HoraDoLembrete = 9;
    private static readonly CultureInfo PtBr = new("pt-BR");

    public sealed record Destino(string Email, string Nome, bool ReceberAvisos);

    public static async Task<Destino?> DestinoAsync(LanePetsDbContext db, string? clienteId)
    {
        if (string.IsNullOrWhiteSpace(clienteId)) return null;
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId);
        var usuario = await db.UsuariosClientes.AsNoTracking().Where(u => u.ClienteId == clienteId).OrderBy(u => u.CriadoEm).FirstOrDefaultAsync();
        return cliente is null || usuario is null ? null : new Destino(usuario.Email, cliente.Nome, cliente.ReceberAvisos);
    }

    private static string PrimeiroNome(string nome) => string.IsNullOrWhiteSpace(nome) ? "tutor(a)" : nome.Trim().Split(' ')[0];

    private static string Quando(string dataHora)
        => DateTime.TryParse(dataHora, out var d) ? d.ToString("dddd, dd/MM/yyyy 'às' HH:mm", PtBr) : dataHora;

    private static async Task<string> OndeAsync(LanePetsDbContext db, string unidade)
    {
        var u = await UnidadesRegras.AcharAsync(db, unidade);
        if (u is null) return Normalizador.NomeUnidade(unidade) is { Length: > 0 } n ? n : "unidade a confirmar";
        // O seed grava "Consulte a equipe LanePets" como endereco provisorio: no e-mail fica so o nome.
        var semEndereco = string.IsNullOrWhiteSpace(u.Endereco) || u.Endereco.StartsWith("Consulte", StringComparison.OrdinalIgnoreCase);
        return semEndereco ? u.Nome : $"{u.Nome} · {u.Endereco}";
    }

    private static string Servicos(Agendamento a) => AgendaPainelService.ServicosResumo(a);

    private static string Rodape(IConfiguration config)
        => config["LanePets:UrlPublica"] is { Length: > 0 } url
            ? $"\n\nAcompanhe pela área do cliente: {url.TrimEnd('/')}/minha-conta.html#agendar\n\nEquipe LanePets"
            : "\n\nAcompanhe pela área do cliente no site da LanePets.\n\nEquipe LanePets";

    private const string Descadastro = "\n\nNão quer receber estes avisos? Desligue em Minha Conta > Avisos por e-mail.";

    // ------------------------------------------------------------------ mensagens

    public static Task<bool> BoasVindasAsync(EmailService email, IConfiguration config, string para, string nome, string pet)
        => email.EnviarAsync(para, "Bem-vindo(a) à LanePets!",
            $"""
            Olá, {PrimeiroNome(nome)}!

            Sua conta na LanePets está pronta. {(string.IsNullOrWhiteSpace(pet) ? "" : $"O cadastro de {pet} já está lá, ")}é só escolher o serviço, a unidade e o horário para agendar.

            Pela área do cliente você acompanha agendamentos, pedidos da loja, pagamentos e o Seguro Pet.
            """ + Rodape(config));

    public static async Task<bool> AgendamentoRecebidoAsync(LanePetsDbContext db, EmailService email, IConfiguration config, Agendamento a)
    {
        var destino = await DestinoAsync(db, a.ClienteId);
        if (destino is null) return false;
        // 29/09: unidade com confirmacao automatica -> o comprovante ja diz que o horario esta confirmado.
        var confirmado = StatusAgendamento.Exibir(a.Status) == StatusAgendamento.Confirmado;
        return await email.EnviarAsync(destino.Email,
            confirmado ? $"LanePets · agendamento de {a.Pet} confirmado" : $"LanePets · recebemos o agendamento de {a.Pet}",
            $"""
            Olá, {PrimeiroNome(destino.Nome)}!

            {(confirmado ? "Seu agendamento está confirmado. Esperamos vocês!" : "Recebemos o seu pedido de agendamento. A equipe confirma o horário em breve.")}

            Pet: {a.Pet}
            Serviço: {Servicos(a)}
            Quando: {Quando(a.DataHora)}
            Onde: {await OndeAsync(db, a.Unidade)}
            """ + Rodape(config));
    }

    /// <summary>Aviso quando a equipe muda o status. So Confirmado e Cancelado geram e-mail.</summary>
    public static async Task<bool> StatusAlteradoAsync(LanePetsDbContext db, EmailService email, IConfiguration config, Agendamento a, string statusNovo)
    {
        if (statusNovo is not (StatusAgendamento.Confirmado or StatusAgendamento.Cancelado)) return false;
        var destino = await DestinoAsync(db, a.ClienteId);
        if (destino is null || !destino.ReceberAvisos) return false;
        var confirmado = statusNovo == StatusAgendamento.Confirmado;
        return await email.EnviarAsync(destino.Email,
            confirmado ? $"LanePets · agendamento de {a.Pet} confirmado" : $"LanePets · agendamento de {a.Pet} cancelado",
            $"""
            Olá, {PrimeiroNome(destino.Nome)}!

            {(confirmado ? "Seu agendamento está confirmado. Esperamos vocês!" : "Seu agendamento foi cancelado pela equipe. Se quiser, escolha outro horário pela área do cliente.")}

            Pet: {a.Pet}
            Serviço: {Servicos(a)}
            Quando: {Quando(a.DataHora)}
            Onde: {await OndeAsync(db, a.Unidade)}
            """ + Rodape(config) + Descadastro);
    }

    /// <summary>
    /// Lembrete da vespera: agendamentos de AMANHA (data local) Solicitados ou Confirmados, ainda sem
    /// lembrete, de quem tem conta e aceita avisos. So roda a partir das <see cref="HoraDoLembrete"/>h.
    /// Marca LembreteEnviadoEm so quando o envio deu certo (falha tenta de novo na proxima hora).
    /// Devolve quantos foram enviados.
    /// </summary>
    public static async Task<int> EnviarLembretesAsync(LanePetsDbContext db, EmailService email, IConfiguration config, DateTime agoraLocal)
    {
        if (agoraLocal.Hour < HoraDoLembrete) return 0;
        var amanha = agoraLocal.Date.AddDays(1).ToString("yyyy-MM-dd");
        var candidatos = (await db.Agendamentos.Where(a => a.DataHora.StartsWith(amanha) && a.LembreteEnviadoEm == null && a.ClienteId != "").ToListAsync())
            .Where(a => StatusAgendamento.Exibir(a.Status) is StatusAgendamento.Solicitado or StatusAgendamento.Confirmado)
            .ToList();
        var enviados = 0;
        foreach (var a in candidatos)
        {
            var destino = await DestinoAsync(db, a.ClienteId);
            if (destino is null || !destino.ReceberAvisos) continue;
            var ok = await email.EnviarAsync(destino.Email, $"LanePets · lembrete: {a.Pet} amanhã",
                $"""
                Olá, {PrimeiroNome(destino.Nome)}!

                Passando para lembrar do atendimento de amanhã.

                Pet: {a.Pet}
                Serviço: {Servicos(a)}
                Quando: {Quando(a.DataHora)}
                Onde: {await OndeAsync(db, a.Unidade)}
                Situação: {StatusAgendamento.Exibir(a.Status)}

                Se não puder ir, cancele pela área do cliente para liberar o horário.
                """ + Rodape(config) + Descadastro);
            if (!ok) continue;
            a.LembreteEnviadoEm = DateTime.UtcNow;
            enviados++;
        }
        if (enviados > 0) await db.SaveChangesAsync();
        return enviados;
    }
}

/// <summary>28/09: roda o lembrete da vespera de hora em hora (e uma vez 1 minuto depois da subida).</summary>
public sealed class LembreteAgendamentoWorker(IServiceScopeFactory escopos, EmailService email, IConfiguration config, EventosService eventos,
    ILogger<LembreteAgendamentoWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), parar);
            using var relogio = new PeriodicTimer(TimeSpan.FromHours(1));
            do
            {
                try
                {
                    using var escopo = escopos.CreateScope();
                    var db = escopo.ServiceProvider.GetRequiredService<LanePetsDbContext>();
                    var enviados = await AvisosClienteService.EnviarLembretesAsync(db, email, config, DateTime.Now);
                    if (enviados > 0)
                        await eventos.RegistrarAsync(new("agendamento", "Lembretes de agendamento enviados", "info", "sistema", "", "LanePets", "",
                            $"{enviados} lembrete(s) por e-mail para os atendimentos de amanhã."));
                }
                catch (Exception ex) when (!parar.IsCancellationRequested)
                {
                    log.LogWarning(ex, "LanePets: a rotina de lembretes falhou; tenta de novo na proxima hora.");
                }
            } while (await relogio.WaitForNextTickAsync(parar));
        }
        catch (OperationCanceledException) { /* aplicacao encerrando */ }
    }
}
