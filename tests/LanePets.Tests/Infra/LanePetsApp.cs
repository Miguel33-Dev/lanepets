using LanePets.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace LanePets.Tests.Infra;

/// <summary>
/// A aplicacao LanePets de verdade (mesmo Program.cs, mesmos controllers e Services),
/// rodando em memoria contra um banco TEMPORARIO criado so para a rodada de testes.
///
///     pasta temporaria/lanepets.db   <- banco da rodada (nasce vazio; o SeedService
///                                       semeia catalogo + admin@gmail.com / 123456)
///     pasta temporaria/backups/      <- backup automatico da subida (item 19)
///     pasta temporaria/logs/         <- erros-AAAA-MM-DD.log (item 14)
///
/// POR QUE O ARQUIVO VAZIO E CRIADO ANTES: DatabaseBootstrap.GarantirArquivo copia o
/// lanepets.db antigo que esta na pasta do projeto quando o destino nao existe. Com o
/// arquivo ja criado (0 byte = banco SQLite vazio valido), a copia nao acontece e o
/// teste comeca sempre do zero.
///
/// Uma instancia so para todos os testes (ver <see cref="ColecaoApi"/>): subir a
/// aplicacao custa segundos, e as sessoes ficam em memoria no SessionService.
/// Cada teste cria os proprios dados (e-mails unicos), entao a ordem nao importa.
/// </summary>
public sealed class LanePetsApp : WebApplicationFactory<Program>
{
    public string Pasta { get; }
    public string CaminhoBanco { get; }

    public LanePetsApp()
    {
        Pasta = Path.Combine(Path.GetTempPath(), "LanePets.Tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Pasta);
        CaminhoBanco = Path.Combine(Pasta, "lanepets.db");
        File.WriteAllBytes(CaminhoBanco, []);

        // Program.cs le a connection string ANTES do Build(); a variavel de ambiente
        // garante que ela ja esteja la. O UseSetting abaixo e a mesma coisa pelo
        // caminho oficial do WebApplicationFactory.
        Environment.SetEnvironmentVariable("ConnectionStrings__LanePetsConnection", $"Data Source={CaminhoBanco}");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:LanePetsConnection", $"Data Source={CaminhoBanco}");
    }

    /// <summary>Cliente HTTP ja apontando para a aplicacao em memoria.</summary>
    public Api Api() => new(CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

    /// <summary>Acesso direto ao banco da rodada, para conferir o que foi gravado.</summary>
    public async Task<T> NoBanco<T>(Func<LanePetsDbContext, Task<T>> consulta)
    {
        using var escopo = Services.CreateScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<LanePetsDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        Environment.SetEnvironmentVariable("ConnectionStrings__LanePetsConnection", null);
        try
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(Pasta, recursive: true);
        }
        catch
        {
            // Apagar a pasta temporaria e cortesia; um arquivo preso pelo antivirus nao
            // pode transformar uma rodada verde em vermelha.
        }
    }
}

/// <summary>
/// Todos os testes que falam com a API ficam nesta colecao: uma aplicacao so, e o
/// xUnit roda as classes da colecao uma de cada vez (o SQLite agradece).
/// </summary>
[CollectionDefinition(Nome)]
public sealed class ColecaoApi : ICollectionFixture<LanePetsApp>
{
    public const string Nome = "API LanePets";
}
