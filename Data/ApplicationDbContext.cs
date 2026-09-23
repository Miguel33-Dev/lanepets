using Microsoft.EntityFrameworkCore;
using PetshopCSharp.Models;

namespace PetshopCSharp.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<Agendamento> Agendamentos => Set<Agendamento>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<PedidoItem> PedidoItens => Set<PedidoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Tipo).HasDefaultValue("cliente");
        });

        modelBuilder.Entity<Produto>(entity =>
        {
            entity.Property(e => e.Preco).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Disponivel).HasDefaultValue(true);
        });

        modelBuilder.Entity<Agendamento>(entity =>
        {
            entity.HasOne(e => e.Usuario)
                .WithMany()
                .HasForeignKey(e => e.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Pedido>(entity =>
        {
            entity.Property(e => e.Total).HasColumnType("decimal(18,2)");
            entity.HasOne(e => e.Usuario)
                .WithMany()
                .HasForeignKey(e => e.UsuarioId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PedidoItem>(entity =>
        {
            entity.Property(e => e.PrecoUnitario).HasColumnType("decimal(18,2)");
            entity.HasOne(e => e.Pedido)
                .WithMany(p => p.Itens)
                .HasForeignKey(e => e.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
