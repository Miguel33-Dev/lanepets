using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

public class SeedService(LanePetsDbContext db, IWebHostEnvironment env, ILogger<SeedService> log)
{
    public async Task SeedAsync()
    {
        await EnsurePublicAreaAsync();
        if (await db.Pets.AnyAsync() || await db.Agendamentos.AnyAsync()) return;
        var dir = Path.Combine(env.ContentRootPath, "Data", "seed");
        await ImportPets(Path.Combine(dir, "pets_importacao.csv"));
        await ImportAppointments(Path.Combine(dir, "agendamentos_importacao.csv"));
        await ImportServices(Path.Combine(dir, "servicos_importacao.csv"));
        await ImportProducts(Path.Combine(dir, "produtos_importacao.csv"));
        await ImportFinance(Path.Combine(dir, "entradasESaidas_importacao.csv"));
        await NormalizeClientsAndRelations();
        await db.SaveChangesAsync();
        log.LogInformation("Lane Pets inicializado com dados dos CSVs de staging.");
    }

    private async Task EnsurePublicAreaAsync()
    {
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Depoimentos (Id TEXT NOT NULL PRIMARY KEY, ClienteId TEXT NOT NULL, NomeCliente TEXT NOT NULL, NomePet TEXT NOT NULL, Telefone TEXT NOT NULL, Avaliacao INTEGER NOT NULL, Comentario TEXT NOT NULL, Status TEXT NOT NULL, CriadoEm TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS PlanosSeguro (Id TEXT NOT NULL PRIMARY KEY, Nome TEXT NOT NULL, Descricao TEXT NOT NULL, Coberturas TEXT NOT NULL, Beneficios TEXT NOT NULL, ValorMensal TEXT NOT NULL, Ativo INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS SolicitacoesSeguro (Id TEXT NOT NULL PRIMARY KEY, PlanoSeguroId TEXT NOT NULL, NomePlano TEXT NOT NULL, NomeCliente TEXT NOT NULL, Telefone TEXT NOT NULL, NomePet TEXT NOT NULL, Observacao TEXT NOT NULL, Status TEXT NOT NULL, CriadoEm TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS UsuariosClientes (Id TEXT NOT NULL PRIMARY KEY, ClienteId TEXT NOT NULL, Email TEXT NOT NULL, SenhaHash TEXT NOT NULL, SenhaSalt TEXT NOT NULL, CriadoEm TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS UsuariosAdministradores (Id TEXT NOT NULL PRIMARY KEY, Email TEXT NOT NULL, SenhaHash TEXT NOT NULL, SenhaSalt TEXT NOT NULL, Ativo INTEGER NOT NULL, CriadoEm TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Unidades (Id TEXT NOT NULL PRIMARY KEY, Nome TEXT NOT NULL, Endereco TEXT NOT NULL, Telefone TEXT NOT NULL, HorarioFuncionamento TEXT NOT NULL, Ativa INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Pedidos (Id TEXT NOT NULL PRIMARY KEY, ClienteId TEXT NOT NULL, ProdutoId TEXT NOT NULL, ProdutoNome TEXT NOT NULL, Quantidade INTEGER NOT NULL, Total TEXT NOT NULL, FormaPagamento TEXT NOT NULL, Status TEXT NOT NULL, CriadoEm TEXT NOT NULL)");
        if (!await db.UsuariosAdministradores.AnyAsync())
        {
            var (hash, salt) = SessionService.HashPassword("123456");
            db.UsuariosAdministradores.Add(new UsuarioAdministrador { Id = "ADM-INICIAL", Email = "admin@gmail.com", SenhaHash = hash, SenhaSalt = salt, Ativo = true });
            await db.SaveChangesAsync();
        }
        if (!await db.Unidades.AnyAsync()) { db.Unidades.AddRange(new Unidade { Id="franco", Nome="Franco da Rocha", Endereco="Consulte a equipe LanePets", Telefone="", HorarioFuncionamento="Segunda a sábado, 08h às 18h" }, new Unidade { Id="caieiras", Nome="Caieiras", Endereco="Consulte a equipe LanePets", Telefone="", HorarioFuncionamento="Segunda a sábado, 08h às 18h" }); await db.SaveChangesAsync(); }
        if (await db.PlanosSeguro.AnyAsync()) return;
        db.PlanosSeguro.AddRange(
            new PlanoSeguro { Id = "SEG-CARINHO", Nome = "Carinho", Descricao = "Proteção essencial para a rotina do seu pet.", Coberturas = "Teleorientação veterinária; assistência emergencial", Beneficios = "Atendimento orientado e suporte quando precisar", ValorMensal = 19.90m },
            new PlanoSeguro { Id = "SEG-CUIDADO", Nome = "Cuidado", Descricao = "Mais tranquilidade para acompanhar cada fase.", Coberturas = "Consultas, urgências e exames conforme contratação", Beneficios = "Prioridade de atendimento e rede parceira", ValorMensal = 39.90m },
            new PlanoSeguro { Id = "SEG-PREMIUM", Nome = "Premium", Descricao = "Uma proteção completa para o seu melhor amigo.", Coberturas = "Consultas, urgências, exames e assistência ampliada", Beneficios = "Suporte prioritário e benefícios exclusivos", ValorMensal = 69.90m });
        await db.SaveChangesAsync();
    }

    private async Task ImportPets(string path) { foreach (var r in CsvService.Read(path).Skip(1)) if (r.Length >= 9) db.Pets.Add(new Pet { Id=r[0], Dono=r[1], PetNome=r[2], Tipo=r[3], Raca=r[4], Telefone=r[5], Endereco=r[6], PacoteJson=r[7], Unidade=r[8] }); await db.SaveChangesAsync(); }
    private async Task ImportAppointments(string path) { foreach (var r in CsvService.Read(path).Skip(1)) if (r.Length >= 14) db.Agendamentos.Add(new Agendamento { Id=r[0], Pet=r[1], Dono=r[2], Telefone=r[3], DataHora=r[4], ServicosJson=r[5], Total=CsvService.Decimal(r[6]), Transporte=r[7], ValorTransporte=CsvService.Decimal(r[8]), Status=r[9], PagamentoStatus=r[10], FormaPagamento=r[11], Obs=r[12], Unidade=r[13] }); await db.SaveChangesAsync(); }
    private async Task ImportServices(string path) { foreach (var r in CsvService.Read(path).Skip(1)) if (r.Length >= 7) db.Servicos.Add(new Servico { Id=r[0], Nome=r[1], Preco=CsvService.Decimal(r[2]), Porte=r[3], AdicionaisJson=r[4], Pacote=r[5], Adicional=r[6] }); }
    private async Task ImportProducts(string path) { foreach (var r in CsvService.Read(path).Skip(1)) if (r.Length >= 6) db.Produtos.Add(new Produto { Id=r[0], Codigo=r[1], Nome=r[2], Categoria=r[3], ValorCompra=CsvService.Decimal(r[4]), ValorVenda=CsvService.Decimal(r[5]) }); }
    private async Task ImportFinance(string path) { foreach (var r in CsvService.Read(path).Skip(1)) if (r.Length >= 7) db.EntradasESaidas.Add(new EntradaSaida { Id=r[0], Data=r[1], Descricao=r[2], Tipo=r[3], Valor=CsvService.Decimal(r[4]), Unidade=r[5], Origem=r[6] }); }

    private async Task NormalizeClientsAndRelations()
    {
        var pets = await db.Pets.ToListAsync(); var clients = await db.Clientes.ToListAsync();
        var byName = clients.ToDictionary(c => Normalize(c.Nome), StringComparer.OrdinalIgnoreCase);
        foreach (var p in pets)
        {
            var key = Normalize(p.Dono); if (string.IsNullOrWhiteSpace(key)) continue;
            if (!byName.TryGetValue(key, out var c)) { c = new Cliente { Id = StableId("CLI", key), Nome=p.Dono, Telefone=p.Telefone, Endereco=p.Endereco, Origem="normalizacao_3A1", Status="ativo" }; db.Clientes.Add(c); byName[key]=c; }
            p.ClienteId = c.Id;
        }
        var map = pets.GroupBy(p => Normalize(p.Dono)+"|"+Normalize(p.PetNome)).ToDictionary(g=>g.Key,g=>g.ToList());
        foreach (var a in await db.Agendamentos.ToListAsync())
        {
            var cand = map.GetValueOrDefault(Normalize(a.Dono)+"|"+Normalize(a.Pet));
            if (cand?.Count == 1) { a.PetId=cand[0].Id; a.ClienteId=cand[0].ClienteId; }
        }
    }
    private static string Normalize(string s) => (s ?? "").Normalize(System.Text.NormalizationForm.FormD).Where(c=>System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark).Aggregate("",(a,c)=>a+c).Trim().ToLowerInvariant().Replace("  "," ");
    private static string StableId(string prefix,string key) => prefix+"-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))).Substring(0,12);
}
