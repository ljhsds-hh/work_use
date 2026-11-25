using QuietRemind.Services;
using Xunit;

namespace QuietRemind.Tests;

public class UserSessionTests
{
    [Fact]
    public void 登录会话标识_可读取且同进程稳定()
    {
        var first = UserSession.TryGetLogonSessionId();
        var second = UserSession.TryGetLogonSessionId();

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.Equal(first, second);
    }
}
