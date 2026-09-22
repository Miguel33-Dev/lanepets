using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PetshopCSharp.Data;
using PetshopCSharp.Models;

namespace PetshopCSharp.Extensions;

public static class SessionExtensions
{
    public static void SetUsuario(this ISession session, Usuario usuario)
    {
        session.SetInt32("UsuarioId", usuario.Id);
        session.SetString("UsuarioNome", usuario.Nome);
        session.SetString("UsuarioEmail", usuario.Email);
        session.SetString("UsuarioTipo", usuario.Tipo);
    }

    public static void ClearUsuario(this ISession session)
    {
        session.Remove("UsuarioId");
        session.Remove("UsuarioNome");
        session.Remove("UsuarioEmail");
        session.Remove("UsuarioTipo");
    }

    public static Usuario? GetUsuario(this ISession session, ApplicationDbContext db)
    {
        var usuarioId = session.GetInt32("UsuarioId");
        if (!usuarioId.HasValue)
        {
            return null;
        }

        return db.Usuarios.FirstOrDefault(u => u.Id == usuarioId.Value);
    }

    public static void SetCart(this ISession session, List<CartItemViewModel> cart)
    {
        session.SetString("Carrinho", JsonSerializer.Serialize(cart));
    }

    public static List<CartItemViewModel> GetCart(this ISession session)
    {
        var cartJson = session.GetString("Carrinho");
        if (string.IsNullOrWhiteSpace(cartJson))
        {
            return new List<CartItemViewModel>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<CartItemViewModel>>(cartJson) ?? new List<CartItemViewModel>();
        }
        catch
        {
            return new List<CartItemViewModel>();
        }
    }

    public static void ClearCart(this ISession session)
    {
        session.Remove("Carrinho");
    }
}
