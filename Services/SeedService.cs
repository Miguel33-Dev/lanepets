using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Preparacao do banco na subida da aplicacao.
///
/// REGRA DESTE ARQUIVO: aqui so entra CATALOGO — o que precisa existir para o
/// sistema funcionar antes de qualquer pessoa usa-lo:
///
///     produtos, servicos, unidades, planos de seguro e o administrador inicial
///
/// Nada de cliente, pet, agendamento, pedido, pagamento, contrato de seguro ou
/// avaliacao e criado aqui. Esses registros nascem exclusivamente da acao real
/// de um cliente ou do administrador, passando pela API.
///
/// Antes, este servico importava pets_importacao.csv, agendamentos_importacao.csv
/// e entradasESaidas_importacao.csv sempre que o banco nascia vazio — era isso
/// que repovoava o sistema com o historico antigo. Essa importacao foi desligada
/// e os CSVs correspondentes ficaram em Data/seed/historico/, fora do caminho do
/// seed, apenas como arquivo morto.
/// </summary>
public class SeedService(LanePetsDbContext db, IWebHostEnvironment env, ILogger<SeedService> log)
{
    public async Task SeedAsync()
    {
        await GarantirEstruturaAsync();
        await GarantirCatalogoAsync();
    }

    // -----------------------------------------------------------------------
    // ESTRUTURA
    // -----------------------------------------------------------------------
    private async Task GarantirEstruturaAsync()
    {
        // Cria o schema completo quando o lanepets.db ainda nao existe.
        // Em bancos ja povoados isto e uma operacao sem efeito.
        await db.Database.EnsureCreatedAsync();

        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Depoimentos (Id TEXT NOT NULL PRIMARY KEY, ClienteId TEXT NOT NULL, NomeCliente TEXT NOT NULL, NomePet TEXT NOT NULL, Telefone TEXT NOT NULL, Avaliacao INTEGER NOT NULL, Comentario TEXT NOT NULL, Status TEXT NOT NULL, CriadoEm TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS PlanosSeguro (Id TEXT NOT NULL PRIMARY KEY, Nome TEXT NOT NULL, Descricao TEXT NOT NULL, Coberturas TEXT NOT NULL, Beneficios TEXT NOT NULL, ValorMensal TEXT NOT NULL, Ativo INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS SolicitacoesSeguro (Id TEXT NOT NULL PRIMARY KEY, PlanoSeguroId TEXT NOT NULL, NomePlano TEXT NOT NULL, NomeCliente TEXT NOT NULL, Telefone TEXT NOT NULL, NomePet TEXT NOT NULL, Observacao TEXT NOT NULL, Status TEXT NOT NULL, CriadoEm TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS UsuariosClientes (Id TEXT NOT NULL PRIMARY KEY, ClienteId TEXT NOT NULL, Email TEXT NOT NULL, SenhaHash TEXT NOT NULL, SenhaSalt TEXT NOT NULL, CriadoEm TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS UsuariosAdministradores (Id TEXT NOT NULL PRIMARY KEY, Email TEXT NOT NULL, SenhaHash TEXT NOT NULL, SenhaSalt TEXT NOT NULL, Ativo INTEGER NOT NULL, CriadoEm TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Unidades (Id TEXT NOT NULL PRIMARY KEY, Nome TEXT NOT NULL, Endereco TEXT NOT NULL, Telefone TEXT NOT NULL, HorarioFuncionamento TEXT NOT NULL, Ativa INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Pedidos (Id TEXT NOT NULL PRIMARY KEY, ClienteId TEXT NOT NULL, ProdutoId TEXT NOT NULL, ProdutoNome TEXT NOT NULL, Quantidade INTEGER NOT NULL, Total TEXT NOT NULL, FormaPagamento TEXT NOT NULL, Status TEXT NOT NULL, CriadoEm TEXT NOT NULL)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Configuracoes (Chave TEXT NOT NULL PRIMARY KEY, Valor TEXT NOT NULL, Descricao TEXT NOT NULL)");

        // Colunas acrescentadas depois que o banco ja existia. EnsureCreated nao
        // altera tabelas ja criadas, entao a coluna e adicionada aqui, uma unica
        // vez, sem tocar em nenhum dado existente.
        await GarantirColunaAsync("Produtos", "Estoque", "INTEGER NOT NULL DEFAULT 0");
        await GarantirColunaAsync("Produtos", "EstoqueMinimo", "INTEGER NOT NULL DEFAULT 0");
        await GarantirColunaAsync("Produtos", "ControlaEstoque", "INTEGER NOT NULL DEFAULT 0");

        // Seguro Pet: o contrato passou a referenciar o cliente e o pet por id.
        // Contratos antigos ficam com o campo vazio e continuam validos; a
        // consulta do cliente trata os dois casos.
        await GarantirColunaAsync("SolicitacoesSeguro", "ClienteId", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("SolicitacoesSeguro", "PetId", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("PlanosSeguro", "Condicoes", "TEXT NOT NULL DEFAULT ''");

        // Contratacao do Seguro Pet: valor fechado, forma de pagamento escolhida
        // e a data do cancelamento. Contratos antigos continuam validos — eles
        // ficam com valor 0, metodo vazio e DataCancelamento nula, e a tela
        // trata esses casos. Nenhuma linha existente e alterada.
        await GarantirColunaAsync("SolicitacoesSeguro", "Valor", "TEXT NOT NULL DEFAULT '0'");
        await GarantirColunaAsync("SolicitacoesSeguro", "MetodoPagamento", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("SolicitacoesSeguro", "PagamentoStatus", "TEXT NOT NULL DEFAULT 'Pendente'");
        await GarantirColunaAsync("SolicitacoesSeguro", "CartaoFinal", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("SolicitacoesSeguro", "DataCancelamento", "TEXT NULL");

        // -------------------------------------------------------------------
        // PERMISSOES ADMINISTRATIVAS
        //
        // UsuariosAdministradores NAO foi recriada: ela ja existia e continua
        // com os mesmos dados. As colunas novas entram por ALTER TABLE, uma
        // unica vez, com valor padrao seguro — Perfil "Admin" e AcessoTotal 0.
        // Ou seja: qualquer administrador que ja exista no banco nasce SEM
        // acesso total, e nao o contrario. O admin@gmail.com e promovido a
        // AdminGeral logo abaixo, no catalogo.
        // -------------------------------------------------------------------
        await GarantirColunaAsync("UsuariosAdministradores", "Nome", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("UsuariosAdministradores", "Telefone", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("UsuariosAdministradores", "Perfil", "TEXT NOT NULL DEFAULT 'Admin'");
        await GarantirColunaAsync("UsuariosAdministradores", "AcessoTotal", "INTEGER NOT NULL DEFAULT 0");
        await GarantirColunaAsync("UsuariosAdministradores", "UltimoAcesso", "TEXT NULL");

        // -------------------------------------------------------------------
        // FICHA DO PET (area do cliente)
        //
        // A tabela Pets NAO foi recriada e nenhuma linha existente e tocada.
        // Estas colunas apenas acrescentam a ficha que o proprio tutor passou a
        // preencher em "Meus pets". Todas nascem vazias/zeradas, entao um pet
        // cadastrado antes desta mudanca continua valido e aparece normalmente
        // no painel, no agendamento e no Seguro Pet.
        //
        // A idade nao e gravada: o que fica no banco e a DATA DE NASCIMENTO, e a
        // idade e calculada na exibicao. Idade digitada envelhece errado.
        // -------------------------------------------------------------------
        await GarantirColunaAsync("Pets", "Sexo", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("Pets", "DataNascimento", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("Pets", "Peso", "TEXT NOT NULL DEFAULT '0'");
        await GarantirColunaAsync("Pets", "Cor", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("Pets", "Porte", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("Pets", "FotoUrl", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("Pets", "Observacoes", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("Pets", "NecessidadesEspeciais", "TEXT NOT NULL DEFAULT ''");
        await GarantirColunaAsync("Pets", "InfoAtendimento", "TEXT NOT NULL DEFAULT ''");

        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS UsuariosAdminPermissoes (Id TEXT NOT NULL PRIMARY KEY, UsuarioAdminId TEXT NOT NULL, Modulo TEXT NOT NULL, PodeVisualizar INTEGER NOT NULL DEFAULT 0, PodeCriar INTEGER NOT NULL DEFAULT 0, PodeEditar INTEGER NOT NULL DEFAULT 0, PodeExcluir INTEGER NOT NULL DEFAULT 0)");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_UsuariosAdminPermissoes_Usuario_Modulo ON UsuariosAdminPermissoes (UsuarioAdminId, Modulo)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS AuditoriasAdmin (Id TEXT NOT NULL PRIMARY KEY, DataHora TEXT NOT NULL, AutorId TEXT NOT NULL, AutorEmail TEXT NOT NULL, Acao TEXT NOT NULL, AlvoId TEXT NOT NULL, AlvoEmail TEXT NOT NULL, Detalhes TEXT NOT NULL)");
    }

    /// <summary>Acrescenta uma coluna se ela ainda nao existir (SQLite nao tem IF NOT EXISTS para colunas).</summary>
    private async Task GarantirColunaAsync(string tabela, string coluna, string definicao)
    {
        var existe = false;
        var conexao = db.Database.GetDbConnection();
        var precisaAbrir = conexao.State != System.Data.ConnectionState.Open;
        if (precisaAbrir) await conexao.OpenAsync();
        try
        {
            await using var comando = conexao.CreateCommand();
            comando.CommandText = $"PRAGMA table_info({tabela})";
            await using var leitor = await comando.ExecuteReaderAsync();
            while (await leitor.ReadAsync())
            {
                if (string.Equals(leitor.GetString(1), coluna, StringComparison.OrdinalIgnoreCase)) { existe = true; break; }
            }
        }
        finally { if (precisaAbrir) await conexao.CloseAsync(); }

        if (existe) return;
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {tabela} ADD COLUMN {coluna} {definicao}");
        log.LogInformation("LanePets: coluna {Tabela}.{Coluna} criada.", tabela, coluna);
    }

    // -----------------------------------------------------------------------
    // CATALOGO
    // Cada bloco so age quando a tabela esta vazia: reiniciar o sistema nunca
    // duplica um produto, um servico nem uma unidade.
    // -----------------------------------------------------------------------
    private async Task GarantirCatalogoAsync()
    {
        // ---------------------------------------------------------------
        // ADMINISTRADOR GERAL
        //
        // O admin@gmail.com que ja existe NAO e recriado e a senha dele NAO e
        // tocada. O unico ajuste e garantir que ele esteja marcado como
        // AdminGeral com acesso total — o perfil mora no banco, entao a
        // seguranca do sistema nao depende de comparar e-mail em lugar nenhum.
        // ---------------------------------------------------------------
        var administradorGeral = await db.UsuariosAdministradores.FirstOrDefaultAsync(u => u.Email == "admin@gmail.com");
        if (administradorGeral is null)
        {
            var (hash, salt) = SessionService.HashPassword("123456");
            db.UsuariosAdministradores.Add(new UsuarioAdministrador { Id = "ADM-INICIAL", Email = "admin@gmail.com", Nome = "Administrador Geral", SenhaHash = hash, SenhaSalt = salt, Ativo = true, Perfil = PerfilAdmin.Geral, AcessoTotal = true });
            await db.SaveChangesAsync();
            log.LogInformation("LanePets: administrador inicial admin@gmail.com criado como Administrador Geral.");
        }
        else if (administradorGeral.Perfil != PerfilAdmin.Geral || !administradorGeral.AcessoTotal || !administradorGeral.Ativo)
        {
            administradorGeral.Perfil = PerfilAdmin.Geral;
            administradorGeral.AcessoTotal = true;
            administradorGeral.Ativo = true;
            if (string.IsNullOrWhiteSpace(administradorGeral.Nome)) administradorGeral.Nome = "Administrador Geral";
            await db.SaveChangesAsync();
            log.LogInformation("LanePets: admin@gmail.com confirmado como Administrador Geral (senha inalterada).");
        }

        // Um sistema sem NENHUM Administrador Geral ficaria sem quem conceda
        // permissoes. Isso nao pode acontecer em banco antigo nenhum.
        if (!await db.UsuariosAdministradores.AnyAsync(u => u.Perfil == PerfilAdmin.Geral))
        {
            var primeiro = await db.UsuariosAdministradores.OrderBy(u => u.CriadoEm).FirstAsync();
            primeiro.Perfil = PerfilAdmin.Geral;
            primeiro.AcessoTotal = true;
            primeiro.Ativo = true;
            await db.SaveChangesAsync();
            log.LogWarning("LanePets: nenhum Administrador Geral encontrado; {Email} foi promovido.", primeiro.Email);
        }

        if (!await db.Unidades.AnyAsync())
        {
            db.Unidades.AddRange(
                new Unidade { Id = "franco", Nome = "Franco da Rocha", Endereco = "Consulte a equipe LanePets", Telefone = "", HorarioFuncionamento = "Segunda a sábado, 08h às 18h" },
                new Unidade { Id = "caieiras", Nome = "Caieiras", Endereco = "Consulte a equipe LanePets", Telefone = "", HorarioFuncionamento = "Segunda a sábado, 08h às 18h" });
            await db.SaveChangesAsync();
            log.LogInformation("LanePets: unidades Franco da Rocha e Caieiras criadas.");
        }

        if (!await db.PlanosSeguro.AnyAsync())
        {
            // Planos sao catalogo, como produto e servico. O CONTRATO do cliente
            // e outra coisa e continua nascendo so quando alguem contrata.
            db.PlanosSeguro.AddRange(
                new PlanoSeguro { Id = "SEG-CARINHO", Nome = "Carinho", Descricao = "Proteção essencial para a rotina do seu pet.", Coberturas = "Teleorientação veterinária; assistência emergencial", Beneficios = "Atendimento orientado e suporte quando precisar", ValorMensal = 19.90m },
                new PlanoSeguro { Id = "SEG-CUIDADO", Nome = "Cuidado", Descricao = "Mais tranquilidade para acompanhar cada fase.", Coberturas = "Consultas, urgências e exames conforme contratação", Beneficios = "Prioridade de atendimento e rede parceira", ValorMensal = 39.90m },
                new PlanoSeguro { Id = "SEG-PREMIUM", Nome = "Premium", Descricao = "Uma proteção completa para o seu melhor amigo.", Coberturas = "Consultas, urgências, exames e assistência ampliada", Beneficios = "Suporte prioritário e benefícios exclusivos", ValorMensal = 69.90m });
            await db.SaveChangesAsync();
            log.LogInformation("LanePets: planos de seguro do catálogo criados.");
        }

        var dir = Path.Combine(env.ContentRootPath, "Data", "seed");

        if (!await db.Servicos.AnyAsync())
        {
            ImportarServicos(Path.Combine(dir, "servicos_importacao.csv"));
            await db.SaveChangesAsync();
            log.LogInformation("LanePets: catálogo de serviços carregado ({Total} serviços).", await db.Servicos.CountAsync());
        }

        if (!await db.Produtos.AnyAsync())
        {
            ImportarProdutos(Path.Combine(dir, "produtos_importacao.csv"));
            await db.SaveChangesAsync();
            log.LogInformation("LanePets: catálogo de produtos carregado ({Total} produtos).", await db.Produtos.CountAsync());
        }
    }

    private void ImportarServicos(string caminho)
    {
        if (!File.Exists(caminho)) { log.LogWarning("LanePets: {Arquivo} não encontrado; catálogo de serviços fica vazio.", caminho); return; }
        foreach (var r in CsvService.Read(caminho).Skip(1))
            if (r.Length >= 7)
                db.Servicos.Add(new Servico { Id = r[0], Nome = r[1], Preco = CsvService.Decimal(r[2]), Porte = r[3], AdicionaisJson = r[4], Pacote = r[5], Adicional = r[6] });
    }

    private void ImportarProdutos(string caminho)
    {
        if (!File.Exists(caminho)) { log.LogWarning("LanePets: {Arquivo} não encontrado; catálogo de produtos fica vazio.", caminho); return; }
        foreach (var r in CsvService.Read(caminho).Skip(1))
            if (r.Length >= 6)
                db.Produtos.Add(new Produto { Id = r[0], Codigo = r[1], Nome = r[2], Categoria = r[3], ValorCompra = CsvService.Decimal(r[4]), ValorVenda = CsvService.Decimal(r[5]) });
    }
}
