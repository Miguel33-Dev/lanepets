using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Data;

public class LanePetsDbContext(DbContextOptions<LanePetsDbContext> options) : DbContext(options)
{
    public DbSet<Pet> Pets => Set<Pet>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Agendamento> Agendamentos => Set<Agendamento>();
    public DbSet<Servico> Servicos => Set<Servico>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<EntradaSaida> EntradasESaidas => Set<EntradaSaida>();
    public DbSet<Pacote> Pacotes => Set<Pacote>();
    public DbSet<Configuracao> Configuracoes => Set<Configuracao>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Configuracao>()
            .HasKey(c => c.Chave);
    }
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();
    public DbSet<AuditoriaMigracao> AuditoriasMigracao => Set<AuditoriaMigracao>();
    public DbSet<ReconciliacaoMigracao> Reconciliacoes => Set<ReconciliacaoMigracao>();
    public DbSet<Depoimento> Depoimentos => Set<Depoimento>();
    public DbSet<PlanoSeguro> PlanosSeguro => Set<PlanoSeguro>();
    public DbSet<SolicitacaoSeguro> SolicitacoesSeguro => Set<SolicitacaoSeguro>();
    public DbSet<UsuarioCliente> UsuariosClientes => Set<UsuarioCliente>();
    public DbSet<UsuarioAdministrador> UsuariosAdministradores => Set<UsuarioAdministrador>();
    public DbSet<Unidade> Unidades => Set<Unidade>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
}
