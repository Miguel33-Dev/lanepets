using LanePets.Data;
using LanePets.Hubs;
using LanePets.Middleware;
using LanePets.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 29/09: HOSPEDAGEM (Railway, Render, Docker)
// - PORT: a plataforma diz em qual porta escutar; sem PORT vale ASPNETCORE_HTTP_PORTS / launchSettings.
// - Atras do proxy da plataforma o HTTPS termina antes do app: X-Forwarded-Proto/For dizem o
//   esquema e o IP reais (HSTS, redirecionamento e o IP do Log de eventos ficam certos).
// - Em producao, sem LanePets:Demo=true, as senhas de exemplo nao sobem (Hospedagem.ConferirSegredos).
// ---------------------------------------------------------------------------
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } portaDaPlataforma && int.TryParse(portaDaPlataforma, out var porta))
    builder.WebHost.UseUrls($"http://0.0.0.0:{porta}");
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();   // o proxy da plataforma nao tem IP fixo
    o.KnownProxies.Clear();
});
Hospedagem.ConferirSegredos(builder.Configuration, builder.Environment);
// Seguranca, Etapa 3 (29/09): sem "Server: Kestrel" na resposta (nao conta a quem ataca o que roda aqui).
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

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

// 28/09: e-mail (codigo de recuperacao de senha). Sem SMTP configurado, grava em
// <pasta do lanepets.db>/emails/ — a caixa de saida de desenvolvimento.
builder.Services.AddSingleton(sp => new EmailService(sp.GetRequiredService<IConfiguration>(),
    Path.Combine(Path.GetDirectoryName(Path.GetFullPath(caminhoDoBanco)) ?? ".", "emails"),
    sp.GetRequiredService<ILogger<EmailService>>()));

builder.Services.AddDbContext<LanePetsDbContext>(options =>
    options.UseSqlite(DatabaseBootstrap.MontarConnectionString(caminhoDoBanco)));

builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<TentativasLogin>();   // 29/09: trava contra forca bruta (Etapa 2 da auditoria)
builder.Services.AddSingleton<IChavesGoogle, ChavesGoogleJwks>();   // Tarefa 1: chaves publicas do Google (JWKS em cache)
builder.Services.AddSingleton<GoogleTokenService>();
builder.Services.AddScoped<SeedService>();

// Autorizacao administrativa: quem e o usuario da requisicao e o que ele pode.
// Scoped porque resolve uma vez por requisicao e reusa dentro dela.
builder.Services.AddScoped<PermissaoService>();
// Item 11.5: gravacao do painel (POST /api/admin/sync/{colecao}). Scoped: guarda o autor da requisicao.
builder.Services.AddScoped<SyncPainelService>();
builder.Services.AddScoped<ClientesPainelService>();   // 27/09: tela Clientes sem o estado inteiro
builder.Services.AddScoped<AgendaPainelService>();     // 27/09: tela Agendamentos sem o estado inteiro

// Log de eventos (item 15 do roadmap): singleton que grava com DbContext proprio.
builder.Services.AddSingleton<EventosService>();

// Cobranca mensal do Seguro Pet (26/09): gera a mensalidade que venceu, de hora em hora.
builder.Services.AddHostedService<CobrancaMensalSeguroWorker>();
// Retencao do Log de eventos (26/09): apaga eventos com mais de 12 meses, uma vez por dia.
builder.Services.AddHostedService<RetencaoLogWorker>();
builder.Services.AddHostedService<LembreteAgendamentoWorker>();   // 28/09: lembrete da vespera por e-mail

// Canal de tempo real do painel administrativo.
builder.Services.AddSignalR();
builder.Services.AddScoped<RealtimeNotifier>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;   // Etapa 3: Secure no HTTPS
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

// ---------------------------------------------------------------------------
// LIMITE DE REQUISICOES (Seguranca, Etapa 2 — 29/09). Sem pacote novo: o limitador ja vem no ASP.NET Core.
//   * toda a /api: LanePets:Seguranca:RequisicoesPorMinuto por IP (padrao 300);
//   * rotas sensiveis ([EnableRateLimiting("sensivel")]: logins, cadastro, codigos de senha, pedidos do site):
//     LanePets:Seguranca:RequisicoesSensiveisPorMinuto por IP (padrao 10).
// Passou do limite = 429 ERR-4290 no envelope de sempre. LanePets:Seguranca:LimitarRequisicoes=false desliga
// (os testes desligam: centenas de chamadas saem do mesmo "IP").
// ---------------------------------------------------------------------------
static RateLimitPartition<string> LimitePorIp(HttpContext http, string grupo, string chave, int padrao)
{
    var cfg = http.RequestServices.GetRequiredService<IConfiguration>();
    if (!cfg.GetValue("LanePets:Seguranca:LimitarRequisicoes", true))
        return RateLimitPartition.GetNoLimiter("livre");
    var ip = http.Connection.RemoteIpAddress?.ToString() ?? "local";
    var limite = Math.Max(1, cfg.GetValue(chave, padrao));
    return RateLimitPartition.GetFixedWindowLimiter($"{grupo}:{ip}", _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = limite, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
    });
}
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        http.Request.Path.StartsWithSegments("/api")
            ? LimitePorIp(http, "api", "LanePets:Seguranca:RequisicoesPorMinuto", 300)
            : RateLimitPartition.GetNoLimiter("fora-da-api"));
    o.AddPolicy("sensivel", http => LimitePorIp(http, "sensivel", "LanePets:Seguranca:RequisicoesSensiveisPorMinuto", 10));
    o.OnRejected = async (contexto, ct) =>
    {
        var http = contexto.HttpContext;
        http.Response.Headers["Retry-After"] = "60";
        http.Response.ContentType = "application/json; charset=utf-8";
        await http.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = false, error = "Muitas requisições em pouco tempo. Aguarde 1 minuto e tente de novo.",
            codigo = CodigoErro.MuitasTentativas, proibido = false, timestamp = DateTime.UtcNow.ToString("O")
        }), ct);
        // Sem evento no Log de proposito: numa enxurrada de requisicoes, cada recusa viraria uma linha no banco.
    };
});

var app = builder.Build();
GoogleLogin.AvisarSeMalConfigurado(app.Configuration, app.Logger);   // Tarefa 1: chave com formato errado = desligado + aviso
app.Services.GetRequiredService<EmailService>().AnunciarModo();   // 29/09: mostra no console se o e-mail sai por SMTP ou .txt

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
        // 26/09: unidade gravada pelo nome ("Franco da Rocha") passa a ser o id ("franco"). Idempotente.
        var unidadesUnificadas = await UnidadesRegras.UnificarGravadasPeloNomeAsync(scope.ServiceProvider.GetRequiredService<LanePetsDbContext>());
        if (unidadesUnificadas > 0) logger.LogInformation("LanePets: {Total} registro(s) com a unidade gravada pelo nome passaram a usar o id.", unidadesUnificadas);
        // Item 19: indices nas colunas de vinculo (CREATE INDEX IF NOT EXISTS).
        await BancoService.CriarIndicesAsync(scope.ServiceProvider.GetRequiredService<LanePetsDbContext>(), logger);
        // Item 8: cria os pagamentos dos agendamentos, pedidos e seguros que ainda nao tem.
        var pagamentosCriados = await PagamentosService.ReconciliarAsync(scope.ServiceProvider.GetRequiredService<LanePetsDbContext>());
        if (pagamentosCriados > 0) logger.LogInformation("LanePets: {Total} pagamento(s) criados/atualizados a partir dos registros existentes.", pagamentosCriados);
        // Retencao do Log (26/09): o backup da subida ja foi feito antes daqui.
        var eventosApagados = await RetencaoEventos.ExecutarAsync(scope.ServiceProvider.GetRequiredService<LanePetsDbContext>(),
            app.Services.GetRequiredService<EventosService>(), DateTime.UtcNow);
        if (eventosApagados > 0) logger.LogInformation("LanePets: retenção do Log removeu {Total} evento(s) com mais de {Meses} meses.", eventosApagados, RetencaoEventos.Meses);
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
app.UseForwardedHeaders();   // 29/09: antes de tudo (ver HOSPEDAGEM no topo)
app.UseMiddleware<SegurancaHttpMiddleware>();   // Seguranca, Etapa 3: cabecalhos (CSP etc.) + limite de tamanho

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
app.UseRateLimiter();   // Seguranca (29/09): depois do routing, para valer o [EnableRateLimiting] de cada rota
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
