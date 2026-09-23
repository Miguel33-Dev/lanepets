using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using PetshopCSharp.Models;

namespace PetshopCSharp.Data;

public static class SeedData
{
    public static void Initialize(ApplicationDbContext context)
    {
        context.Database.EnsureCreated();
        EnsurePedidoSchema(context);

        if (!context.Usuarios.Any(u => u.Email == "admin@petshop.com"))
        {
            context.Usuarios.Add(new Usuario
            {
                Nome = "Administrador",
                Email = "admin@petshop.com",
                Senha = BCrypt.Net.BCrypt.HashPassword("123456"),
                Tipo = "admin"
            });

            context.SaveChanges();
        }
    }

    private static void EnsurePedidoSchema(ApplicationDbContext context)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Pedidos"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Pedidos"" PRIMARY KEY AUTOINCREMENT,
                ""UsuarioId"" INTEGER NULL,
                ""NomeCliente"" TEXT NOT NULL DEFAULT '',
                ""Telefone"" TEXT NOT NULL DEFAULT '',
                ""Endereco"" TEXT NOT NULL DEFAULT '',
                ""Observacao"" TEXT NULL,
                ""FormaPagamento"" TEXT NOT NULL DEFAULT 'PIX',
                ""DataPedido"" TEXT NOT NULL,
                ""Status"" TEXT NOT NULL DEFAULT 'Confirmado',
                ""Total"" TEXT NOT NULL,
                CONSTRAINT ""FK_Pedidos_Usuarios_UsuarioId"" FOREIGN KEY (""UsuarioId"") REFERENCES ""Usuarios"" (""Id"") ON DELETE SET NULL
            );");

        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""PedidoItens"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PedidoItens"" PRIMARY KEY AUTOINCREMENT,
                ""PedidoId"" INTEGER NOT NULL,
                ""ProdutoId"" INTEGER NOT NULL,
                ""ProdutoNome"" TEXT NOT NULL DEFAULT '',
                ""Quantidade"" INTEGER NOT NULL,
                ""PrecoUnitario"" TEXT NOT NULL,
                CONSTRAINT ""FK_PedidoItens_Pedidos_PedidoId"" FOREIGN KEY (""PedidoId"") REFERENCES ""Pedidos"" (""Id"") ON DELETE CASCADE
            );");

        EnsureColumn(context, "Pedidos", "NomeCliente", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(context, "Pedidos", "Telefone", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(context, "Pedidos", "Endereco", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(context, "Pedidos", "Observacao", "TEXT NULL");
        EnsureColumn(context, "Pedidos", "FormaPagamento", "TEXT NOT NULL DEFAULT 'PIX'");
    }

    private static void EnsureColumn(ApplicationDbContext context, string tableName, string columnName, string columnDefinition)
    {
        var connection = context.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{tableName}');";

            using var reader = command.ExecuteReader();
            var columns = new List<string>();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }

            if (columns.Contains(columnName, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            var alterSql = $"ALTER TABLE {QuoteIdentifier(tableName)} ADD COLUMN {QuoteIdentifier(columnName)} {columnDefinition};";
            context.Database.ExecuteSqlRaw(alterSql);
        }
        finally
        {
            if (!wasOpen)
            {
                connection.Close();
            }
        }
    }

    private static string QuoteIdentifier(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
