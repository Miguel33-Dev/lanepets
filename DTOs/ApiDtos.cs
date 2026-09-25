using LanePets.Services;

namespace LanePets.DTOs;

// ---------------------------------------------------------------------------
// Respostas com forma fixa (item 11.5, 26/09).
//
// O projeto responde com projecao explicita (CONTEXTO §6.6); objeto anonimo continua
// valendo para leitura de tela. Record entra aqui quando a resposta e um CONTRATO que
// script de teste e tela conferem campo a campo — os nomes saem em camelCase no JSON
// (padrao do ASP.NET), iguais aos que o objeto anonimo gerava.
//
// (Os tres records que existiam aqui — ApiResponse, LoginRequest, FinanceLoginRequest —
// nao eram usados por nenhum controller desde a conversao para C# e sairam.)
// ---------------------------------------------------------------------------

/// <summary>POST /api/admin/sync/{colecao}: o que foi gravado.</summary>
public sealed record SyncResposta(string Colecao, List<string> NovosIds, int Criados, int Alterados, int Apagados, string Message);

/// <summary>POST /api/admin/importar: contagem por colecao (simulada ou gravada).</summary>
public sealed record ImportacaoResposta(bool Simulacao, Dictionary<string, ImportacaoService.ResultadoImportacao> Relatorio, string Message);
