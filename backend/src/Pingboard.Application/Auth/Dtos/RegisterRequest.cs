namespace Pingboard.Application.Auth.Dtos;

public sealed record RegisterRequest(string? Email, string? Password);
