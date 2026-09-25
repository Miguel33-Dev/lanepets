using System.Text.Json;
using LanePets.Data;
using LanePets.Models;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Services;

/// <summary>
/// Item 11.3 (26/09): importacao do que ainda so existia no localStorage de algum navegador
/// (tela migrar-dados.html -> POST /api/admin/importar). Estava dentro do AdminSyncController;
/// veio para ca sem mudar o que e importado nem como os repetidos sao reconhecidos.
///
/// Regras (as mesmas de antes):
///   - so acrescenta: registro que ja existe no banco (pela identidade natural) e ignorado,
///     nada do banco e sobrescrito ou apagado;
///   - simular = true conta novos/repetidos/ignorados e nao grava nada;
///   - gravando de verdade: um SaveChanges so e, depois, os agendamentos importados ganham
///     pagamento (item 8).
/// Permissao, evento e aviso em tempo real ficam no controller.
/// </summary>
public static class ImportacaoService
{
    /// <summary>Importa as cinco colecoes na ordem de sempre e devolve o relatorio por colecao.</summary>
    public static async Task<Dictionary<string, ResultadoImportacao>> ExecutarAsync(LanePetsDbContext db, JsonElement corpo, bool simular)
    {
        var relatorio = new Dictionary<string, ResultadoImportacao>
        {
            ["servicos"] = await ImportarServicos(db, Lista(corpo, "servicos"), simular),
            ["produtos"] = await ImportarProdutos(db, Lista(corpo, "produtos"), simular),
            ["pets"] = await ImportarPets(db, Lista(corpo, "pets"), simular),
            ["agendamentos"] = await ImportarAgendamentos(db, Lista(corpo, "agendamentos"), simular),
            ["entradasESaidas"] = await ImportarLancamentos(db, Lista(corpo, "entradasESaidas"), simular)
        };

        if (!simular)
        {
            await db.SaveChangesAsync();
            await PagamentosService.ReconciliarAsync(db);   // item 8: agendamentos importados ganham pagamento
        }
        return relatorio;
    }

    /// <summary>Texto curto para o Log de eventos: "servicos 2 novos, 1 ja existiam; ...".</summary>
    public static string Resumir(Dictionary<string, ResultadoImportacao> relatorio)
        => string.Join("; ", relatorio.Where(kv => kv.Value.Novos + kv.Value.JaExistiam + kv.Value.Ignorados > 0)
                                      .Select(kv => $"{kv.Key}: {kv.Value.Novos} novo(s), {kv.Value.JaExistiam} já existia(m), {kv.Value.Ignorados} ignorado(s)"))
           is { Length: > 0 } texto ? texto : "Nada para importar.";

    public sealed record ResultadoImportacao(int Novos, int JaExistiam, int Ignorados);

    private static async Task<ResultadoImportacao> ImportarServicos(LanePetsDbContext db, List<JsonElement> linhas, bool simular)
    {
        var existentes = (await db.Servicos.AsNoTracking().ToListAsync())
            .Select(x => Chave(x.Nome, x.Porte)).ToHashSet();
        int novos = 0, repetidos = 0, ignorados = 0;
        foreach (var linha in linhas)
        {
            var nome = Texto(linha, "nome");
            if (nome.Length == 0) { ignorados++; continue; }
            var porte = Texto(linha, "porte");
            if (!existentes.Add(Chave(nome, porte))) { repetidos++; continue; }
            novos++;
            if (simular) continue;
            db.Servicos.Add(new Servico
            {
                Id = NovoId("SRV"),
                Nome = nome,
                Preco = Numero(linha, "preco"),
                Porte = porte,
                AdicionaisJson = Bruto(linha, "adicionais") ?? "[]",
                Pacote = Booleano(linha, "pacote") ? "Sim" : "",
                Adicional = Texto(linha, "adicional")
            });
        }
        return new ResultadoImportacao(novos, repetidos, ignorados);
    }

    private static async Task<ResultadoImportacao> ImportarProdutos(LanePetsDbContext db, List<JsonElement> linhas, bool simular)
    {
        var existentes = (await db.Produtos.AsNoTracking().ToListAsync())
            .Select(x => Chave(x.Codigo)).ToHashSet();
        int novos = 0, repetidos = 0, ignorados = 0;
        foreach (var linha in linhas)
        {
            var codigo = Texto(linha, "codigo");
            var nome = Texto(linha, "nome");
            if (codigo.Length == 0 && nome.Length == 0) { ignorados++; continue; }
            if (!existentes.Add(Chave(codigo.Length > 0 ? codigo : nome))) { repetidos++; continue; }
            novos++;
            if (simular) continue;
            db.Produtos.Add(new Produto
            {
                Id = NovoId("PRD"),
                Codigo = codigo,
                Nome = nome,
                Categoria = Texto(linha, "categoria"),
                ValorCompra = Numero(linha, "valorCompra"),
                ValorVenda = Numero(linha, "valorVenda"),
                Estoque = (int)Numero(linha, "estoque"),
                EstoqueMinimo = (int)Numero(linha, "estoqueMinimo"),
                ControlaEstoque = Numero(linha, "estoque") > 0
            });
        }
        return new ResultadoImportacao(novos, repetidos, ignorados);
    }

    private static async Task<ResultadoImportacao> ImportarPets(LanePetsDbContext db, List<JsonElement> linhas, bool simular)
    {
        var existentes = (await db.Pets.AsNoTracking().ToListAsync())
            .Select(x => Chave(x.Dono, x.PetNome)).ToHashSet();
        var clientes = await db.Clientes.ToListAsync();
        int novos = 0, repetidos = 0, ignorados = 0;
        foreach (var linha in linhas)
        {
            var dono = Texto(linha, "dono");
            var pet = Texto(linha, "pet");
            if (pet.Length == 0) { ignorados++; continue; }
            if (!existentes.Add(Chave(dono, pet))) { repetidos++; continue; }
            novos++;
            if (simular) continue;

            var telefone = Texto(linha, "telefone");
            var endereco = Texto(linha, "endereco");
            var cliente = clientes.FirstOrDefault(c => Normalizador.Texto(c.Nome) == Normalizador.Texto(dono));
            if (cliente is null && dono.Length > 0)
            {
                cliente = new Cliente { Id = NovoId("CLI"), Nome = dono, Telefone = telefone, Endereco = endereco, Origem = "importacao_painel", Status = "ativo" };
                db.Clientes.Add(cliente);
                clientes.Add(cliente);
            }

            db.Pets.Add(new Pet
            {
                Id = NovoId("PET"),
                ClienteId = cliente?.Id ?? "",
                Dono = dono,
                PetNome = pet,
                Tipo = Texto(linha, "tipo"),
                Raca = Texto(linha, "raca"),
                Telefone = telefone,
                Endereco = endereco,
                PacoteJson = Bruto(linha, "pacote") ?? "",
                Unidade = Texto(linha, "unidade")
            });
        }
        return new ResultadoImportacao(novos, repetidos, ignorados);
    }

    private static async Task<ResultadoImportacao> ImportarAgendamentos(LanePetsDbContext db, List<JsonElement> linhas, bool simular)
    {
        var existentes = (await db.Agendamentos.AsNoTracking().ToListAsync())
            .Select(x => Chave(x.Dono, x.Pet, Normalizador.Data(x.DataHora), (x.DataHora ?? "").Length >= 16 ? x.DataHora[11..16] : "")).ToHashSet();
        int novos = 0, repetidos = 0, ignorados = 0;
        foreach (var linha in linhas)
        {
            var dataHora = Texto(linha, "dataHora");
            var pet = Texto(linha, "pet");
            if (dataHora.Length == 0 || pet.Length == 0) { ignorados++; continue; }
            var dono = Texto(linha, "dono");
            if (!existentes.Add(Chave(dono, pet, Normalizador.Data(dataHora), dataHora.Length >= 16 ? dataHora[11..16] : ""))) { repetidos++; continue; }
            novos++;
            if (simular) continue;
            db.Agendamentos.Add(new Agendamento
            {
                Id = NovoId("AGD"),
                Pet = pet,
                Dono = dono,
                Telefone = Texto(linha, "telefone"),
                DataHora = dataHora,
                ServicosJson = Bruto(linha, "servicos") ?? "[]",
                Total = Numero(linha, "total"),
                Transporte = Texto(linha, "transporte"),
                ValorTransporte = Numero(linha, "valorTransporte"),
                Status = StatusAgendamento.Exibir(Texto(linha, "status")),   // item 4: importacao grava o nome novo
                PagamentoStatus = Texto(linha, "pagamentoStatus"),
                FormaPagamento = Texto(linha, "formaPagamento"),
                Obs = Texto(linha, "obs"),
                Unidade = Texto(linha, "unidade")
            });
        }
        return new ResultadoImportacao(novos, repetidos, ignorados);
    }

    private static async Task<ResultadoImportacao> ImportarLancamentos(LanePetsDbContext db, List<JsonElement> linhas, bool simular)
    {
        var existentes = (await db.EntradasESaidas.AsNoTracking().ToListAsync())
            .Select(x => Chave(Normalizador.Data(x.Data), x.Descricao, x.Tipo, x.Valor.ToString("0.00"))).ToHashSet();
        int novos = 0, repetidos = 0, ignorados = 0;
        foreach (var linha in linhas)
        {
            var data = Texto(linha, "data");
            var valor = Numero(linha, "valor");
            if (data.Length == 0) { ignorados++; continue; }
            var descricao = Texto(linha, "descricao");
            var tipo = Texto(linha, "tipo");
            if (!existentes.Add(Chave(Normalizador.Data(data), descricao, tipo, valor.ToString("0.00")))) { repetidos++; continue; }
            novos++;
            if (simular) continue;
            db.EntradasESaidas.Add(new EntradaSaida
            {
                Id = NovoId("FIN"),
                Data = data,
                Descricao = descricao,
                Tipo = tipo,
                Valor = valor,
                Unidade = Texto(linha, "unidade"),
                Origem = "importacao_painel"
            });
        }
        return new ResultadoImportacao(novos, repetidos, ignorados);
    }

    // -----------------------------------------------------------------------
    // AUXILIARES DE LEITURA DO JSON (o localStorage guarda numeros como texto
    // em alguns registros antigos, entao cada campo e lido com tolerancia)
    // -----------------------------------------------------------------------

    private static string NovoId(string prefixo) => prefixo + "-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    private static string Chave(params string?[] partes) => string.Join("|", partes.Select(Normalizador.Texto));

    private static List<JsonElement> Lista(JsonElement corpo, string nome)
        => corpo.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.Array
            ? valor.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList()
            : new List<JsonElement>();

    public static string Texto(JsonElement objeto, string nome)
    {
        if (!objeto.TryGetProperty(nome, out var valor)) return "";
        return valor.ValueKind switch
        {
            JsonValueKind.String => valor.GetString()?.Trim() ?? "",
            JsonValueKind.Number => valor.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    private static decimal Numero(JsonElement objeto, string nome)
    {
        if (!objeto.TryGetProperty(nome, out var valor)) return 0m;
        if (valor.ValueKind == JsonValueKind.Number && valor.TryGetDecimal(out var d)) return d;
        var texto = (valor.ValueKind == JsonValueKind.String ? valor.GetString() : null) ?? "";
        texto = texto.Replace("R$", "").Trim();
        if (texto.Contains(',')) texto = texto.Replace(".", "").Replace(',', '.');
        return decimal.TryParse(texto, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    private static bool Booleano(JsonElement objeto, string nome)
        => objeto.TryGetProperty(nome, out var valor) && (valor.ValueKind == JsonValueKind.True || (valor.ValueKind == JsonValueKind.String && Normalizador.Texto(valor.GetString()) is "sim" or "true"));

    private static string? Bruto(JsonElement objeto, string nome)
        => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind is JsonValueKind.Array or JsonValueKind.Object
            ? valor.GetRawText()
            : null;
}
