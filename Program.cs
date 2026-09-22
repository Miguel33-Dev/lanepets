using LanePets.Data;
using LanePets.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<LanePetsDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<SessionService>();
builder.Services.AddScoped<SeedService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Length > 0) p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LanePetsDbContext>();
    db.Database.EnsureCreated();
    await scope.ServiceProvider.GetRequiredService<SeedService>().SeedAsync();
    app.Logger.LogInformation("Banco de dados SQLite conectado e inicializado com sucesso.");
}

app.UseCors();
var publicHome = new DefaultFilesOptions();
publicHome.DefaultFileNames.Clear();
publicHome.DefaultFileNames.Add("cliente.html");
app.UseDefaultFiles(publicHome);
var adminFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/admin", "/index.html", "/clientes.html", "/clientes_pacote.html", "/agendamentos.html", "/dashboard_financeiro.html", "/Produtos.html", "/entradas_e_saidas.html", "/relatorio.html", "/gestao-publica.html" };
app.Use(async (context, next) => { if (adminFiles.Contains(context.Request.Path)) { if (!context.Request.Cookies.TryGetValue("lanePetsAdmin", out var token)) { context.Response.Redirect("/admin-login.html"); return; } try { context.RequestServices.GetRequiredService<SessionService>().RequireAdmin(token); } catch { context.Response.Cookies.Delete("lanePetsAdmin"); context.Response.Redirect("/admin-login.html"); return; } } await next(); });
app.UseStaticFiles(new StaticFileOptions
{
    // As páginas HTML são sempre revalidadas: assim uma alteração no wwwroot
    // aparece na próxima navegação, sem depender de Ctrl+F5. CSS/JS seguem
    // o cache padrão e já são versionados pela query string (?v=).
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    }
});
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapFallbackToFile("cliente.html");
app.Logger.LogInformation("LanePets iniciado. Abra o sistema pela URL exibida pelo ASP.NET Core; não abra os arquivos HTML diretamente.");
app.Run();
