using System.Runtime.InteropServices;

namespace SpaceMaid.App.Services;

/// <summary>
/// 把文本输出到父进程的控制台。
///
/// 为什么需要它：本程序是 <c>WinExe</c>（没有自己的控制台），从 PowerShell 里跑
/// <c>SpaceMaid.exe --dry-run</c> 时若不做处理，摘要就会"人间蒸发"。
/// 通过 <c>AttachConsole(ATTACH_PARENT_PROCESS)</c> 附着到调用者的控制台即可正常打印；
/// 没有可附着的控制台（例如从资源管理器双击）时静默——产物与日志都在磁盘上，不影响可复核性。
/// </summary>
internal static class ConsoleOutput
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    /// <summary>尽力打印；任何失败都不抛出（输出只是给人看的冗余信息，不是功能）。</summary>
    public static void Write(string message)
    {
        try
        {
            AttachConsole(AttachParentProcess);
            Console.Out.WriteLine(message);
            Console.Out.Flush();
        }
        catch (Exception)
        {
            // 忽略：没有控制台可附着
        }
    }
}
