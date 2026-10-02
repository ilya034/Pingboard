using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pingboard.Api.Infrastructure;
using Pingboard.Application.Common;
using Pingboard.Domain;

namespace Pingboard.Application.Tests.Api;

/// <summary>
///     Маппинг исключений в HTTP-статусы — это то, на что опирается фронт и алерты.
///     Ошибки, замапленные не туда, дороже всего: битый JSON превращался в «алертный» 500,
///     гонка регистраций — в 503 «база не отвечает», а любая SQL-ошибка пряталась за тем же 503.
/// </summary>
public sealed class ExceptionStatusMapperTests
{
    private static PostgresException Postgres(string sqlState, string? constraintName = null)
    {
        return new PostgresException("ошибка сервера", "ERROR", "ERROR", sqlState, constraintName: constraintName);
    }

    [Fact]
    public void Malformed_json_body_is_400_not_500()
    {
        var exception = new BadHttpRequestException(
            "Failed to read parameter \"RegisterRequest request\" from the request body as JSON.",
            StatusCodes.Status400BadRequest);

        var mapped = ExceptionStatusMapper.Map(exception);

        Assert.Equal(StatusCodes.Status400BadRequest, mapped.Status);
    }

    [Fact]
    public void Domain_validation_error_uses_the_field_name_as_key_without_duplicating_it()
    {
        var mapped = ExceptionStatusMapper.Map(DomainValidationException.For("Email", "некорректный email"));

        Assert.Equal(StatusCodes.Status400BadRequest, mapped.Status);
        Assert.Equal(["некорректный email"], mapped.Errors!["Email"]);
        Assert.DoesNotContain("Email:", mapped.Detail);
    }

    [Fact]
    public void Racing_registrations_on_one_email_are_400_on_the_email_field()
    {
        var exception = new DbUpdateException(
            "See inner exception",
            Postgres(PostgresErrorCodes.UniqueViolation, ExceptionStatusMapper.UsersEmailIndex));

        var mapped = ExceptionStatusMapper.Map(exception);

        Assert.Equal(StatusCodes.Status400BadRequest, mapped.Status);
        Assert.Equal(["A user with this email is already registered."], mapped.Errors!["Email"]);
    }

    [Fact]
    public void Other_unique_violations_are_409()
    {
        var exception = new DbUpdateException(
            "See inner exception",
            Postgres(PostgresErrorCodes.UniqueViolation, "ux_something_else"));

        Assert.Equal(StatusCodes.Status409Conflict, ExceptionStatusMapper.Map(exception).Status);
    }

    [Fact]
    public void Server_side_sql_error_is_not_reported_as_an_unavailable_database()
    {
        // Синтаксическая ошибка — это 500 (ошибка сервиса), а не 503 «БД не отвечает»:
        // ложный 503 уводит дежурного не туда и маскирует реальную проблему.
        var exception = new DbUpdateException("See inner exception", Postgres("42601"));

        Assert.Equal(StatusCodes.Status500InternalServerError, ExceptionStatusMapper.Map(exception).Status);
    }

    [Theory]
    [InlineData("08006")] // connection_failure
    [InlineData("57P03")] // cannot_connect_now: сервер поднимается или выключается
    [InlineData("53300")] // too_many_connections: кончились соединения
    public void Transient_postgres_errors_are_503(string sqlState)
    {
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ExceptionStatusMapper.Map(Postgres(sqlState)).Status);
    }

    [Fact]
    public void Connection_errors_and_timeouts_are_503()
    {
        Assert.Equal(StatusCodes.Status503ServiceUnavailable,
            ExceptionStatusMapper.Map(new NpgsqlException("connection refused")).Status);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable,
            ExceptionStatusMapper.Map(new TimeoutException()).Status);
    }

    [Fact]
    public void Unauthorized_carries_the_same_challenge_as_the_bearer_scheme()
    {
        var mapped = ExceptionStatusMapper.Map(new InvalidCredentialsException("Неверный email или пароль."));

        Assert.Equal(StatusCodes.Status401Unauthorized, mapped.Status);
        Assert.Equal(ErrorResponseFormat.BearerChallenge, mapped.WwwAuthenticate);
    }

    [Fact]
    public void Unknown_exception_is_500_without_internal_details_in_the_body()
    {
        var mapped = ExceptionStatusMapper.Map(new InvalidOperationException("строка подключения с паролем"));

        Assert.Equal(StatusCodes.Status500InternalServerError, mapped.Status);
        Assert.DoesNotContain("паролем", mapped.Detail);
    }
}
