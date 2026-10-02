namespace Pingboard.Application.Auth.Dtos;

public sealed record AuthTokenDto(string AccessToken, DateTimeOffset ExpiresAt);
