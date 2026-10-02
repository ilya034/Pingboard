using Pingboard.Domain.Entities;

namespace Pingboard.Domain.Tests.Entities;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 30, 12, 00, 00, TimeSpan.Zero);

    [Fact]
    public void Register_NormalizesEmail()
    {
        var user = User.Register("  Demo@Pingboard.LOCAL ", "hash", Now);

        Assert.Equal("demo@pingboard.local", user.Email);
        Assert.Equal(Now, user.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@starts-with-at")]
    [InlineData("ends-with-at@")]
    public void Register_WithInvalidEmail_Throws(string email)
    {
        Assert.Throws<DomainValidationException>(() => User.Register(email, "hash", Now));
    }

    [Fact]
    public void Register_WithEmptyHash_Throws()
    {
        Assert.Throws<DomainValidationException>(() => User.Register("a@b.com", "", Now));
    }
}
