using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace LanePets.Services;

/// <summary>
/// 28/09: envio de e-mail do LanePets (primeiro uso: codigo de recuperacao de senha).
///
/// - Com <c>LanePets:Email:SmtpHost</c> configurado, envia por SMTP (System.Net.Mail, sem pacote novo):
///   <c>SmtpPorta</c> (587), <c>SmtpUsuario</c>, <c>SmtpSenha</c>, <c>SmtpSsl</c> (true), <c>Remetente</c>.
/// - Sem SMTP (desenvolvimento), grava a mensagem em &lt;pasta do banco&gt;/emails/AAAA-MM-DD_HHmmss_xxxx.txt —
///   e a "caixa de saida" local para testar o fluxo sem servidor de e-mail.
/// As ultimas 50 mensagens ficam em memoria (<see cref="Enviados"/>), para os testes automatizados.
/// Nunca lanca: falha de envio vai para o log do servidor e volta como false.
/// Singleton.
/// </summary>
public class EmailService(IConfiguration config, string pasta, ILogger<EmailService> log)
{
    public sealed record Mensagem(DateTime Quando, string Para, string Assunto, string Corpo, string Destino);

    private readonly ConcurrentQueue<Mensagem> _enviados = new();
    public IReadOnlyList<Mensagem> Enviados => _enviados.ToArray();
    public string Pasta => pasta;

    // O Google mostra a senha de app em 4 blocos ("abcd efgh ijkl mnop"): os espacos nao fazem parte dela.
    private string SenhaSmtp => new((config["LanePets:Email:SmtpSenha"] ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>Uma linha no console da subida dizendo como o e-mail vai sair (nunca mostra a senha).</summary>
    public void AnunciarModo()
    {
        var host = config["LanePets:Email:SmtpHost"];
        if (string.IsNullOrWhiteSpace(host))
            log.LogInformation("LanePets: e-mail SEM SMTP — cada mensagem vira .txt em {Pasta}.", pasta);
        else
            log.LogInformation("LanePets: e-mail por SMTP {Host}:{Porta} como {Usuario} (senha {Senha}).", host,
                config["LanePets:Email:SmtpPorta"] ?? "587", config["LanePets:Email:SmtpUsuario"] ?? "(sem usuario)",
                SenhaSmtp.Length == 0 ? "NAO configurada" : $"com {SenhaSmtp.Length} caracteres");
    }

    private string Remetente => config["LanePets:Email:Remetente"] is { Length: > 0 } r ? r : "LanePets <nao-responda@lanepets.local>";

    public async Task<bool> EnviarAsync(string para, string assunto, string corpo)
    {
        var host = config["LanePets:Email:SmtpHost"];
        try
        {
            string destino;
            if (!string.IsNullOrWhiteSpace(host))
            {
                using var smtp = new SmtpClient(host, int.TryParse(config["LanePets:Email:SmtpPorta"], out var porta) ? porta : 587)
                {
                    EnableSsl = !string.Equals(config["LanePets:Email:SmtpSsl"], "false", StringComparison.OrdinalIgnoreCase),
                    Credentials = string.IsNullOrWhiteSpace(config["LanePets:Email:SmtpUsuario"]) ? null
                        : new NetworkCredential(config["LanePets:Email:SmtpUsuario"]?.Trim(), SenhaSmtp)
                };
                using var mensagem = new MailMessage { From = new MailAddress(ExtrairEndereco(Remetente), "LanePets"), Subject = assunto, Body = corpo, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
                mensagem.To.Add(para);
                await smtp.SendMailAsync(mensagem);
                destino = "smtp:" + host;
                log.LogInformation("LanePets: e-mail \"{Assunto}\" enviado por SMTP ({Host}).", assunto, host);
            }
            else
            {
                Directory.CreateDirectory(pasta);
                var arquivo = Path.Combine(pasta, $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{Guid.NewGuid().ToString("N")[..4]}.txt");
                var texto = new StringBuilder()
                    .Append("De: ").AppendLine(Remetente)
                    .Append("Para: ").AppendLine(para)
                    .Append("Assunto: ").AppendLine(assunto)
                    .Append("Data: ").AppendLine(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"))
                    .AppendLine().Append(corpo).ToString();
                await File.WriteAllTextAsync(arquivo, texto, Encoding.UTF8);
                destino = arquivo;
                log.LogInformation("LanePets: e-mail \"{Assunto}\" gravado em {Arquivo} (sem SMTP configurado).", assunto, arquivo);
            }
            _enviados.Enqueue(new Mensagem(DateTime.UtcNow, para, assunto, corpo, destino));
            while (_enviados.Count > 50 && _enviados.TryDequeue(out _)) { }
            return true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "LanePets: falha ao enviar e-mail \"{Assunto}\".", assunto);
            if (ex is SmtpException { Message: var m } && (m.Contains("5.7.0") || m.Contains("5.7.8") || m.Contains("Authentication", StringComparison.OrdinalIgnoreCase)))
                log.LogWarning("LanePets: o servidor recusou o login SMTP. No Gmail: use a SENHA DE APP (16 letras) da mesma conta de SmtpUsuario.");
            return false;
        }
    }

    private static string ExtrairEndereco(string remetente)
    {
        var i = remetente.IndexOf('<');
        var j = remetente.IndexOf('>');
        return i >= 0 && j > i ? remetente[(i + 1)..j] : remetente;
    }
}
