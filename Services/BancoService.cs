using LanePets.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Item 19 do roadmap (25/09): cuidados com o banco, SEM migrations e SEM
/// recriar tabela (decisoes do Fabricio — §6.1 continua valendo).
///
///   1. Backup automatico na subida, ANTES de qualquer ajuste de esquema,
///      em &lt;pasta do banco&gt;/backups (guarda os 10 ultimos).
///   2. Indices nas colunas de vinculo e de busca, por CREATE INDEX IF NOT
///      EXISTS — so cria se a tabela e as colunas existirem. Indice nao muda
///      dado nenhum: so acelera consulta.
///   Foreign key de verdade nao foi criada (exigiria recriar tabelas); os
///   vinculos quebrados sao apontados pelo IntegridadeService.
/// </summary>
public static class BancoService
{
    public const int BackupsGuardados = 10;

    public record InfoBackup(string Arquivo, long Bytes, DateTime CriadoEm);
    public record InfoIndice(string Nome, string Tabela, string Colunas, string Situacao);

    /// <summary>Resultado da ultima subida (mostrado na tela de integridade).</summary>
    public static IReadOnlyList<InfoIndice> UltimosIndices { get; private set; } = [];
    public static string UltimoBackup { get; private set; } = "";
    public static string UltimoErroBackup { get; private set; } = "";

    public static string PastaBackups(string caminhoBanco) =>
        Path.Combine(Path.GetDirectoryName(caminhoBanco) ?? ".", "backups");

    /// <summary>
    /// Copia consistente do banco com VACUUM INTO (funciona com WAL aberto e
    /// gera um arquivo unico, sem -wal/-shm). Falha de backup NAO impede a
    /// aplicacao de subir: fica registrada e aparece na tela.
    /// </summary>
    public static void FazerBackup(string caminhoBanco, ILogger log)
    {
        try
        {
            if (!File.Exists(caminhoBanco)) return;
            var pasta = PastaBackups(caminhoBanco);
            Directory.CreateDirectory(pasta);
            var destino = Path.Combine(pasta, $"lanepets-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            using (var conexao = new SqliteConnection(DatabaseBootstrap.MontarConnectionString(caminhoBanco)))
            {
                conexao.Open();
                using var cmd = conexao.CreateCommand();
                cmd.CommandText = "VACUUM INTO $destino";
                cmd.Parameters.AddWithValue("$destino", destino);
                cmd.ExecuteNonQuery();
            }
            UltimoBackup = Path.GetFileName(destino);
            UltimoErroBackup = "";

            // Guarda so os mais recentes (o nome ja ordena por data).
            foreach (var velho in Directory.GetFiles(pasta, "lanepets-*.db").OrderByDescending(f => f).Skip(BackupsGuardados))
                File.Delete(velho);
            log.LogInformation("LanePets: backup do banco em {Destino}.", destino);
        }
        catch (Exception ex)
        {
            UltimoErroBackup = ex.Message;
            log.LogWarning(ex, "LanePets: nao foi possivel fazer o backup automatico do banco.");
        }
    }

    public static List<InfoBackup> ListarBackups(string caminhoBanco)
    {
        var pasta = PastaBackups(caminhoBanco);
        if (!Directory.Exists(pasta)) return [];
        return Directory.GetFiles(pasta, "lanepets-*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Name)
            .Select(f => new InfoBackup(f.Name, f.Length, f.LastWriteTimeUtc))
            .ToList();
    }

    /// <summary>Indices desejados: tabela, colunas. Nome = IX_Tabela_Colunas.</summary>
    private static readonly (string Tabela, string[] Colunas)[] Indices =
    [
        ("Pets", ["ClienteId"]),
        ("Agendamentos", ["ClienteId"]),
        ("Agendamentos", ["PetId"]),
        ("Agendamentos", ["DataHora"]),
        ("Agendamentos", ["Unidade", "DataHora"]),
        ("Agendamentos", ["ResponsavelId"]),
        ("Pedidos", ["ClienteId"]),
        ("Pedidos", ["ProdutoId"]),
        ("Pedidos", ["Unidade"]),
        ("UsuariosClientes", ["Email"]),
        ("UsuariosClientes", ["ClienteId"]),
        ("UsuariosAdministradores", ["Email"]),
        ("UsuariosAdminPermissoes", ["UsuarioAdminId"]),
        ("SolicitacoesSeguro", ["ClienteId"]),
        ("Depoimentos", ["ClienteId"]),
        ("Depoimentos", ["Status"]),
        ("EntradasESaidas", ["Data"]),
        ("Pacotes", ["PetId"]),
        ("EventosLog", ["DataHora"]),
        ("EventosLog", ["Categoria"]),
        ("MovimentacoesEstoque", ["ProdutoId"]),
        ("MovimentacoesEstoque", ["PedidoId"]),
        ("Pagamentos", ["ClienteId"]),
        ("Pagamentos", ["Status"]),
        ("Produtos", ["Codigo"])
    ];

    /// <summary>Cria os indices que faltam. Nunca derruba a subida.</summary>
    public static async Task CriarIndicesAsync(LanePetsDbContext db, ILogger log)
    {
        var resultado = new List<InfoIndice>();
        var conexao = db.Database.GetDbConnection();
        var abriu = conexao.State != System.Data.ConnectionState.Open;
        if (abriu) await conexao.OpenAsync();
        try
        {
            foreach (var (tabela, colunas) in Indices)
            {
                var nome = $"IX_{tabela}_{string.Join("_", colunas)}";
                var lista = string.Join(", ", colunas);
                try
                {
                    var existentes = await ColunasAsync(conexao, tabela);
                    if (existentes.Count == 0) { resultado.Add(new(nome, tabela, lista, "tabela não existe")); continue; }
                    var falta = colunas.FirstOrDefault(c => !existentes.Contains(c));
                    if (falta is not null) { resultado.Add(new(nome, tabela, lista, $"coluna {falta} não existe")); continue; }
                    var jaTinha = await ExisteIndiceAsync(conexao, nome);
                    using var cmd = conexao.CreateCommand();
                    // Nomes vem da lista fixa acima (nunca de entrada do usuario).
                    cmd.CommandText = $"CREATE INDEX IF NOT EXISTS \"{nome}\" ON \"{tabela}\" ({string.Join(", ", colunas.Select(c => $"\"{c}\""))})";
                    await cmd.ExecuteNonQueryAsync();
                    resultado.Add(new(nome, tabela, lista, jaTinha ? "ok" : "criado agora"));
                }
                catch (Exception ex)
                {
                    resultado.Add(new(nome, tabela, lista, "erro: " + ex.Message));
                    log.LogWarning(ex, "LanePets: indice {Indice} nao foi criado.", nome);
                }
            }
        }
        finally { if (abriu) await conexao.CloseAsync(); }
        UltimosIndices = resultado;
        var criados = resultado.Count(r => r.Situacao == "criado agora");
        if (criados > 0) log.LogInformation("LanePets: {Total} indice(s) criados no banco.", criados);
    }

    private static async Task<HashSet<string>> ColunasAsync(System.Data.Common.DbConnection conexao, string tabela)
    {
        var colunas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tabela}\")";
        using var leitor = await cmd.ExecuteReaderAsync();
        while (await leitor.ReadAsync()) colunas.Add(leitor.GetString(1));
        return colunas;
    }

    private static async Task<bool> ExisteIndiceAsync(System.Data.Common.DbConnection conexao, string nome)
    {
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $n";
        var p = cmd.CreateParameter(); p.ParameterName = "$n"; p.Value = nome; cmd.Parameters.Add(p);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }
}
