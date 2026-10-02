namespace Pingboard.Application.Common;

/// <summary>Доступ к чужому ресурсу (403).</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }
}
