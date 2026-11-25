using System.Runtime.InteropServices;

namespace QuietRemind.Services;

/// <summary>
/// 当前登录会话标识。使用进程令牌的 TokenStatistics.AuthenticationId（登录会话 LUID）：
/// 同一次登录会话内（含手动启动、计划任务拉起、权限提升）稳定；注销重登、重启、快速启动重新登录后变化。
/// 退出标记记录该 LUID，用于判断标记是否属于“本次登录会话”，不依赖 TickCount64 或日志时间。
/// </summary>
public static class UserSession
{
    private const uint TokenQuery = 0x0008;
    private const int TokenStatisticsClass = 10;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenStatistics
    {
        public Luid TokenId;
        public Luid AuthenticationId;
        public long ExpirationTime;
        public int TokenType;
        public int ImpersonationLevel;
        public uint DynamicCharged;
        public uint DynamicAvailable;
        public uint GroupCount;
        public uint PrivilegeCount;
        public Luid ModifiedId;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle, int informationClass, out TokenStatistics information,
        int informationLength, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>当前登录会话 LUID 字符串；查询失败返回 null（调用方按“宁可多拉起，不可漏提醒”处理）。</summary>
    public static string? TryGetLogonSessionId()
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token))
            {
                return null;
            }
            if (!GetTokenInformation(token, TokenStatisticsClass, out var stats,
                    Marshal.SizeOf<TokenStatistics>(), out _))
            {
                return null;
            }
            return $"{stats.AuthenticationId.HighPart:X8}{stats.AuthenticationId.LowPart:X8}";
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                CloseHandle(token);
            }
        }
    }
}
