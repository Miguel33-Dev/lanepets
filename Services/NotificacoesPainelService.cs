using LanePets.Data;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// 29/09: o sino do painel. Nao guarda "lido/nao lido": mostra o que ESTA esperando alguem agir —
/// quando a equipe resolve (confirma, aprova, repoe o estoque), o aviso some sozinho.
/// Cada item so aparece para quem pode ver o modulo; o funcionario ve so a unidade dele (VeUnidade).
/// Nada e inventado: sem pendencia, a lista vem vazia. Classe static (§6.34).
/// </summary>
public static class NotificacoesPainelService
{
    public sealed record Item(string Chave, string Modulo, int Quantidade, string Texto, string Link);

    public static async Task<List<Item>> ListarAsync(LanePetsDbContext db, ContextoAdmin ctx, DateTime agoraLocal)
    {
        var itens = new List<Item>();
        void Somar(string chave, string modulo, int n, string um, string varios, string link)
        {
            if (n > 0) itens.Add(new Item(chave, modulo, n, n == 1 ? um : string.Format(varios, n), link));
        }
        bool Ve(string modulo) => ctx.Pode(modulo, AcaoPermissao.Visualizar);

        if (Ve(ModulosAdmin.Agendamentos))
        {
            var hoje = agoraLocal.ToString("yyyy-MM-dd");
            var n = (await db.Agendamentos.AsNoTracking()
                    .Where(a => string.Compare(a.DataHora, hoje) >= 0)
                    .Select(a => new { a.Status, a.Unidade }).ToListAsync())
                .Count(a => StatusAgendamento.Exibir(a.Status) == StatusAgendamento.Solicitado && ctx.VeUnidade(a.Unidade));
            Somar("agendamentos", ModulosAdmin.Agendamentos, n, "1 agendamento aguardando confirmação",
                "{0} agendamentos aguardando confirmação", "agendamentos.html");
        }

        if (Ve(ModulosAdmin.Pedidos))
        {
            var n = (await db.Pedidos.AsNoTracking().Select(p => new { p.Status, p.Unidade }).ToListAsync())
                .Count(p => Normalizador.Texto(p.Status) == "pendente" && ctx.VeUnidade(p.Unidade));
            Somar("pedidos", ModulosAdmin.Pedidos, n, "1 pedido da loja pendente", "{0} pedidos da loja pendentes", "pedidos.html");
        }

        if (Ve(ModulosAdmin.Pagamentos))
        {
            var n = (await db.Pagamentos.AsNoTracking().Where(p => p.ReembolsoPendente).Select(p => p.Unidade).ToListAsync())
                .Count(u => ctx.VeUnidade(u));
            Somar("reembolsos", ModulosAdmin.Pagamentos, n, "1 reembolso em análise", "{0} reembolsos em análise", "pagamentos.html");
        }

        if (Ve(ModulosAdmin.Avaliacoes))
        {
            var n = await db.Depoimentos.AsNoTracking().CountAsync(d => d.Status == "Pendente");
            Somar("avaliacoes", ModulosAdmin.Avaliacoes, n, "1 avaliação aguardando moderação", "{0} avaliações aguardando moderação", "gestao-publica.html");
        }

        if (Ve(ModulosAdmin.Seguros))
        {
            var n = await db.SolicitacoesSeguro.AsNoTracking().CountAsync(s => s.Status == "Pendente");
            Somar("seguros", ModulosAdmin.Seguros, n, "1 solicitação de seguro nova", "{0} solicitações de seguro novas", "gestao-publica.html");
        }

        if (Ve(ModulosAdmin.Produtos))
        {
            var n = await db.Produtos.AsNoTracking().CountAsync(p => p.ControlaEstoque && p.Estoque <= p.EstoqueMinimo);
            Somar("estoque", ModulosAdmin.Produtos, n, "1 produto com estoque baixo", "{0} produtos com estoque baixo", "Produtos.html");
        }

        return itens;
    }
}
