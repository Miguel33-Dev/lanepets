namespace LanePets.DTOs;
public record ApiResponse(object? Data, bool Ok = true, string? Error = null);
public record LoginRequest(string Action, string Senha);
public record FinanceLoginRequest(string Action, string Token, string Senha);
public record TestRequest(string Action, string? Token, string? Confirmacao, string? Id, string? DataHora, string? Status, string? Unidade);
