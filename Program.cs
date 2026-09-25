using LanePets.Data;
using LanePets.Hubs;
using LanePets.Middleware;
using LanePets.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// BANCO DE DADOS — UM SO
//
// O projeto tinha dois bancos: petshop.db (area MVC legada, com Views Razor e
// entidades de Id inteiro) e lanepets.db (area publica, area do cliente e
// painel administrativo). Duas fontes de verdade para as mesmas coisas.
//
// A camada MVC/petshop.db foi removida. Agora existe UMA API sobre UM banco:
//
//                        lanepets.db
//                             |
//                         API LanePets
//                             |
//          +------------------+------------------+
//          |                  |                  |
//     AREA DO CLIENTE      PAINEL ADMIN       DASHBOARD
//
// O banco nasce vazio de dados transacionais. Clientes, pets, agendamentos,
// pedidos, pagamentos, seguros e avaliacoes so passam a existir quando alguem
// realmente faz a acao no sistema. Apenas o catalogo (produtos, servicos,
// unidades, planos de seguro) e o administrador inicial sao semeados.
//
// ONDE O ARQUIVO FICA: DatabaseBootstrap decide. Por padrao, fora da pasta do
// codigo (que esta no OneDrive e sincronizar um SQLite corrompe o arquivo).
// ConnectionStrings:LanePetsConnection, quando preenchida, tem prioridade.
// ---------------------------------------------------------------------------
var caminhoDoBanco = DatabaseBootstrap.ResolverCaminho(builder.Configuration);

// Item 14 do roadmap (24/09): corpo de requisicao em formato errado (ex.: texto
// onde se espera numero) responde no envelope da API com ERR-4001, e nao com o
// ProblemDetails padrao do ASP.NET que nenhuma tela sabe ler.
builder.Services.AddControllers().ConfigureApiBehaviorOptions(opcoes =>
    opcoes.InvalidModelStateResponseFactory = _ => new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new
    {
        ok = false,
        error = "Os dados enviados estão em um formato inválido. Confira os campos e tente de novo.",
        codigo = CodigoErro.DadosInvalidos,
        proibido = false,
        timestamp = DateTime.UtcNow.ToString("O")
    }));

// Arquivo de erros inesperados, ao lado do banco (fora do OneDrive):
// <pasta do lanepets.db>/logs/erros-AAAA-MM-DD.log
builder.Services.AddSingleton(sp => new RegistroErros(
    Path.Combine(Path.GetDirectoryName(Path.GetFullPath(caminhoDoBanco)) ?? ".", "logs"),
    sp.GetRequiredService<ILogger<RegistroErros>>()));

builder.Services.AddDbContext<LanePetsDbContext>(options =>
    options.UseSqlite(DatabaseBootstrap.MontarConnectionString(caminhoDoBanco)));

builder.Services.AddSingleton<SessionService>();
builder.Services.AddScoped<SeedService>();

// Autorizacao administrativa: quem e o usuario da requisicao e o que ele pode.
// Scoped porque resolve uma vez por requisicao e reusa dentro dela.
builder.Services.AddScoped<PermissaoService>();
// Item 11.5: gravacao do painel (POST /api/admin/sync/{colecao}). Scoped: guarda o autor da requisicao.
builder.Services.AddScoped<SyncPainelService>();

// Log de eventos (item 15 do roadmap): singleton que grava com DbContext proprio.
builder.Services.AddSingleton<EventosService>();

// Canal de tempo real do painel administrativo.
builder.Services.AddSignalR();
builder.Services.AddScoped<RealtimeNotifier>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// CORS (item 1 do roadmap, 24/09). Antes qualquer site podia chamar a API
// com credenciais (SetIsOriginAllowed(_ => true) + AllowCredentials). O
// frontend e servido pela propria aplicacao — mesma origem, nao precisa de
// CORS —, entao so as origens listadas em LanePets:CorsOrigins sao aceitas.
// Sem a chave, valem apenas os enderecos locais do launchSettings.
var origensCors = builder.Configuration.GetSection("LanePets:CorsOrigins").Get<string[]>()
                  ?? new[] { "http://localhost:5180", "https://localhost:7180" };
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(origensCors)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// ---------------------------------------------------------------------------
// Inicializacao do banco
//
// Ordem proposital:
//   1. garantir o arquivo no destino (migrando o antigo, se houver);
//   2. conferir integridade e fixar os PRAGMAs;
//   3. criar o schema e semear apenas o catalogo;
//   4. so entao servir a primeira requisicao.
//
// Se o passo 2 falhar, a aplicacao NAO sobe. Um banco corrompido tem de ser
// tratado na subida, com mensagem clara, e nao virar erro solto no meio de um
// login do cliente.
// ---------------------------------------------------------------------------
var logger = app.Services.GetRequiredService<ILogger<Program>>();

DatabaseBootstrap.GarantirArquivo(caminhoDoBanco, app.Environment.ContentRootPath, logger);
DatabaseBootstrap.PrepararEConferir(caminhoDoBanco, logger);
// Item 19: copia do banco ANTES de qualquer ajuste de esquema do SeedService.
BancoService.FazerBackup(caminhoDoBanco, logger);

using (var scope = app.Services.CreateScope())
{
    try
    {
        await scope.ServiceProvider.GetRequiredService<SeedService>().SeedAsync();
        // Item 5: lista de unidades dinamica (Normalizador) montada a partir da tabela.
        await UnidadesRegras.RecarregarAsync(scope.ServiceProvider.GetRequiredService<LanePetsDbContext>());
        // Item 19: indices nas colunas de vinculo (CREATE INDEX IF NOT EXISTS).
        await BancoService.CriarIndicesAsync(scope.ServiceProvider.GetRequiredService<LanePetsDbContext>(), logger);
        // Item 8: cria os pagamentos dos agendamentos, pedidos e seguros que ainda nao tem.
        var pagamentosCriados = await PagamentosService.ReconciliarAsync(scope.ServiceProvider.GetRequiredService<LanePetsDbContext>());
        if (pagamentosCriados > 0) logger.LogInformation("LanePets: {Total} pagamento(s) criados/atualizados a partir dos registros existentes.", pagamentosCriados);
        logger.LogInformation("LanePets: banco pronto em {Caminho}.", caminhoDoBanco);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "LanePets: falha ao inicializar o banco de dados.");
        throw;
    }
}

// Ao encerrar, o WAL e consolidado e truncado: a aplicacao nao deixa -wal nem
// -shm para tras, que foi a origem do "database disk image is malformed".
app.Lifetime.ApplicationStopped.Register(() => DatabaseBootstrap.Encerrar(caminhoDoBanco, logger));

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Item 14 do roadmap (24/09): toda excecao que escapar de um endpoint de /api
// vira o envelope { ok:false, error, codigo, referencia } — em QUALQUER
// ambiente. Falha inesperada nunca mostra texto tecnico: mostra ERR-5001 e uma
// referencia que tambem vai para logs/erros-AAAA-MM-DD.log.
app.UseMiddleware<ErroGlobalMiddleware>();

// 404/405 em /api tambem respondem no envelope (antes vinham sem corpo, e a
// tela so conseguia dizer "erro inesperado").
app.UseStatusCodePages(async contextoStatus =>
{
    var http = contextoStatus.HttpContext;
    if (!http.Request.Path.StartsWithSegments("/api")) return;
    var (codigo, mensagem) = http.Response.StatusCode switch
    {
        404 => (CodigoErro.NaoEncontrado, "Recurso não encontrado na API."),
        405 => (CodigoErro.Metodo, "Esta rota não aceita esse tipo de requisição. Se o sistema acabou de ser atualizado, pare o LanePets e rode \"dotnet run\" de novo."),
        _ => ("", "")
    };
    if (codigo.Length == 0) return;
    http.Response.ContentType = "application/json; charset=utf-8";
    await http.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(
        new { ok = false, error = mensagem, codigo, proibido = false, timestamp = DateTime.UtcNow.ToString("O") }));
});

app.UseCors();
app.UseSession();

// Bloqueia as paginas administrativas antes de servir arquivos estaticos.
app.UseMiddleware<AdminAreaGuardMiddleware>();

// "/" abre a HOME PUBLICA (cliente.html). O painel continua em /index.html.
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "cliente.html" }
});
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

// Somente rotas de API por atributo ([Route("api/...")]). A rota MVC
// {controller}/{action} saiu junto com a camada legada.
app.MapControllers();

// Avisos de "algo mudou" para o painel. O hub nao transporta dados de negocio.
app.MapHub<LanePetsHub>("/hubs/lanepets");

app.Run();

// Item 12 (testes automatizados): o projeto tests/LanePets.Tests sobe esta mesma
// aplicacao em memoria com WebApplicationFactory<Program>. Com top-level statements
// a classe Program e gerada pelo compilador; esta declaracao so a deixa publica.
public partial class Program { }
