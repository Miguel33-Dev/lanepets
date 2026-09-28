using System.IO.Compression;
using LanePets.Tests.Infra;

namespace LanePets.Tests.Painel;

/// <summary>
/// 29/09: GET /api/admin/relatorio/excel — planilha .xlsx de verdade (ZIP com as abas), mesmo calculo e mesmas
/// permissoes do relatorio da tela: sem pagamentos:visualizar os valores saem "Restrito" e a aba de formas some.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class RelatorioExcelTests(LanePetsApp app)
{
    private async Task<(HttpResponseMessage Resposta, byte[] Corpo)> Baixar(string token, string de, string ate)
    {
        var http = app.CreateClient();
        var r = await http.GetAsync($"/api/admin/relatorio/excel?token={Uri.EscapeDataString(token)}&de={de}&ate={ate}&unidade=todas");
        return (r, await r.Content.ReadAsByteArrayAsync());
    }

    private static Dictionary<string, string> Partes(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx));
        return zip.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
    }

    [Fact]
    public async Task Planilha_tem_as_abas_e_o_atendimento_do_periodo()
    {
        var cliente = app.Api();
        var (_, petId) = await cliente.ClienteComPet("Tutor Planilha", "Planilhinha");
        var (servicoId, _) = await cliente.Servico();
        var data = Cenarios.NovaData();
        Assert.Equal(200, (await cliente.Agendar(petId, servicoId, data, "15:00")).Codigo);

        var (r, corpo) = await Baixar(await app.Api().LoginAdmin(), data, data);
        Assert.Equal(200, (int)r.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal((byte)'P', corpo[0]); Assert.Equal((byte)'K', corpo[1]);            // e um ZIP (xlsx)

        var partes = Partes(corpo);
        var livro = partes["xl/workbook.xml"];
        foreach (var aba in new[] { "Resumo", "Por unidade", "Formas de pagamento", "Atendimentos", "Pedidos" })
            Assert.Contains($"name=\"{aba}\"", livro);
        Assert.Contains(partes.Where(p => p.Key.StartsWith("xl/worksheets/")).Select(p => p.Value), x => x.Contains("Planilhinha"));
    }

    [Fact]
    public async Task Sem_permissao_de_pagamentos_os_valores_saem_restritos()
    {
        var painel = app.Api();
        var geral = await painel.LoginAdmin();
        var soRelatorio = await painel.AdminCom(geral, Permissao.So("relatorios"));
        var hoje = DateTime.Today.ToString("yyyy-MM-dd");

        var (r, corpo) = await Baixar(soRelatorio, hoje, hoje);
        Assert.Equal(200, (int)r.StatusCode);
        var partes = Partes(corpo);
        Assert.DoesNotContain("Formas de pagamento", partes["xl/workbook.xml"]);
        Assert.Contains("Restrito", partes["xl/worksheets/sheet1.xml"]);

        Assert.Equal(401, (int)(await Baixar("invalido", hoje, hoje)).Resposta.StatusCode);
    }
}
