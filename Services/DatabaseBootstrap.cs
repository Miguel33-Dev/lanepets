using Microsoft.Data.Sqlite;

namespace LanePets.Services;

/// <summary>
/// Onde o banco vive, como ele e aberto e como ele e fechado.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// O sistema apresentou "SQLite Error 11: database disk image is malformed"
/// com o arquivo do banco INTEGRO (integrity_check = ok). O erro nao vinha do
/// arquivo: vinha da forma como ele estava sendo hospedado e aberto.
///
/// Duas causas, as duas tratadas aqui:
///
/// 1) SIDECARS ORFAOS DO WAL
///    Em modo WAL o SQLite mantem dois arquivos ao lado do banco: "-wal" (as
///    paginas ainda nao aplicadas) e "-shm" (o indice dessas paginas em memoria
///    compartilhada). Se o arquivo .db for trocado por outro enquanto um
///    processo esta com esse par aberto, o indice passa a descrever um arquivo
///    que nao existe mais e toda leitura cai em pagina invalida — que e
///    exatamente o "disk image is malformed".
///    Tratamento: a aplicacao faz checkpoint TRUNCATE ao encerrar, entao ela
///    nunca deixa um -wal para tras; e confere a integridade ao subir, entao
///    um banco ruim falha na hora, com mensagem clara, em vez de falhar no meio
///    de um login.
///
/// 2) O BANCO DENTRO DE UMA PASTA SINCRONIZADA (OneDrive)
///    O OneDrive sincroniza .db, -wal e -shm como tres arquivos independentes e
///    pode substituir um deles enquanto o SQLite escreve. Isso corrompe o banco
///    de verdade, e nao ha PRAGMA que conserte.
///    Tratamento: por padrao o banco passa a morar em uma pasta local fora de
///    qualquer sincronizacao (LocalApplicationData/LanePets). O codigo continua
///    onde esta; so o arquivo de dados sai.
///
/// Continua existindo UM UNICO BANCO. Este arquivo apenas decide onde ele fica.
/// </summary>
public static class DatabaseBootstrap
{
    private const string NomeArquivo = "lanepets.db";
    private const string Pasta = "LanePets";

    /// <summary>
    /// Caminho do unico banco do sistema.
    ///
    /// Se ConnectionStrings:LanePetsConnection estiver preenchida, ela manda —
    /// e assim que o Docker aponta para /app/data. Vazia (o padrao em
    /// desenvolvimento), o banco vai para a pasta local do usuario, fora do
    /// OneDrive:
    ///
    ///     Windows  %LOCALAPPDATA%\LanePets\lanepets.db
    ///     Linux    ~/.local/share/LanePets/lanepets.db
    /// </summary>
    public static string ResolverCaminho(IConfiguration configuracao)
    {
        var configurada = configuracao.GetConnectionString("LanePetsConnection");
        if (!string.IsNullOrWhiteSpace(configurada))
        {
            var origem = new SqliteConnectionStringBuilder(configurada).DataSource;
            return Path.GetFullPath(origem);
        }

        var raiz = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(raiz)) raiz = Path.GetTempPath();
        return Path.Combine(raiz, Pasta, NomeArquivo);
    }

    public static string MontarConnectionString(string caminho)
        => new SqliteConnectionStringBuilder
        {
            DataSource = caminho,
            // O SQLite nao e servidor: quando duas requisicoes escrevem ao mesmo
            // tempo, a segunda precisa esperar em vez de estourar "database is
            // locked" na cara do usuario.
            DefaultTimeout = 15
        }.ToString();

    /// <summary>
    /// Garante que o arquivo exista no destino.
    ///
    /// Na primeira execucao depois da mudanca de pasta, o banco que estava junto
    /// do codigo e copiado para o destino. A copia e feita pelo mecanismo de
    /// backup do proprio SQLite, nao por File.Copy: assim o conteudo pendente no
    /// -wal entra junto e nenhum sidecar da origem e arrastado para o destino —
    /// foi justamente arrastar sidecar que causou o erro relatado.
    /// </summary>
    public static void GarantirArquivo(string destino, string contentRoot, ILogger log)
    {
        var pasta = Path.GetDirectoryName(destino);
        if (!string.IsNullOrWhiteSpace(pasta)) Directory.CreateDirectory(pasta);

        if (File.Exists(destino)) return;

        var herdado = Path.Combine(contentRoot, NomeArquivo);
        if (!File.Exists(herdado))
        {
            log.LogInformation("LanePets: banco novo sera criado em {Destino}.", destino);
            return;
        }

        try
        {
            using var origem = new SqliteConnection(MontarConnectionString(herdado));
            using var copia = new SqliteConnection(MontarConnectionString(destino));
            origem.Open();
            copia.Open();
            origem.BackupDatabase(copia);
            log.LogInformation("LanePets: banco migrado de {Origem} para {Destino} (fora da pasta sincronizada).", herdado, destino);
        }
        catch (Exception ex)
        {
            // Falhar aqui e melhor do que subir apontando para um banco vazio e
            // deixar o usuario achar que perdeu os dados.
            throw new InvalidOperationException(
                $"Nao foi possivel migrar o banco de \"{herdado}\" para \"{destino}\". " +
                "Verifique se a aplicacao anterior foi encerrada e tente novamente.", ex);
        }
    }

    /// <summary>
    /// Confere a saude do banco ANTES de servir a primeira requisicao e deixa o
    /// arquivo no modo em que ele deve operar.
    ///
    /// WAL: mantido de proposito. A aplicacao tem muitas leituras concorrentes
    /// (painel, area do cliente e dashboard lendo ao mesmo tempo) e escritas
    /// esparsas; em WAL um leitor nao bloqueia o escritor nem o contrario. O que
    /// faltava nao era o modo, era fechar o WAL direito — o que
    /// <see cref="Encerrar"/> passa a fazer.
    ///
    /// Se o banco estiver realmente corrompido, esta funcao LANCA. Nada de
    /// engolir o erro: um banco corrompido tem de impedir a subida, e nao virar
    /// um 500 aleatorio meia hora depois.
    /// </summary>
    public static void PrepararEConferir(string caminho, ILogger log)
    {
        using var conexao = new SqliteConnection(MontarConnectionString(caminho));
        conexao.Open();

        Executar(conexao, "PRAGMA journal_mode=WAL");
        Executar(conexao, "PRAGMA synchronous=NORMAL");
        Executar(conexao, "PRAGMA busy_timeout=15000");
        Executar(conexao, "PRAGMA foreign_keys=ON");

        var resultado = Escalar(conexao, "PRAGMA quick_check");
        if (!string.Equals(resultado, "ok", StringComparison.OrdinalIgnoreCase))
        {
            var completo = Escalar(conexao, "PRAGMA integrity_check");
            throw new InvalidOperationException(
                $"O banco \"{caminho}\" esta corrompido e a aplicacao nao pode subir.\n" +
                $"  PRAGMA quick_check     -> {resultado}\n" +
                $"  PRAGMA integrity_check -> {completo}\n" +
                "Restaure uma copia integra ou apague o arquivo para que ele seja recriado.");
        }

        var modo = Escalar(conexao, "PRAGMA journal_mode");
        log.LogInformation("LanePets: banco {Caminho} verificado (quick_check=ok, journal_mode={Modo}).", caminho, modo);
    }

    /// <summary>
    /// Fecha o banco sem deixar rastro: aplica tudo o que esta no -wal e o
    /// trunca. Depois disso o .db e autossuficiente e nao sobra nenhum sidecar
    /// apontando para o estado de uma execucao que ja acabou.
    /// </summary>
    public static void Encerrar(string caminho, ILogger log)
    {
        try
        {
            // As conexoes do pool ainda seguram o arquivo; sem limpar o pool o
            // checkpoint nao consegue truncar o -wal.
            SqliteConnection.ClearAllPools();
            using var conexao = new SqliteConnection(MontarConnectionString(caminho));
            conexao.Open();
            Executar(conexao, "PRAGMA wal_checkpoint(TRUNCATE)");
            log.LogInformation("LanePets: WAL consolidado no encerramento.");
        }
        catch (Exception ex)
        {
            // Encerramento nao pode derrubar o processo.
            log.LogWarning(ex, "LanePets: nao foi possivel consolidar o WAL ao encerrar.");
        }
    }

    private static void Executar(SqliteConnection conexao, string sql)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }

    private static string Escalar(SqliteConnection conexao, string sql)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        return comando.ExecuteScalar()?.ToString() ?? "";
    }
}
