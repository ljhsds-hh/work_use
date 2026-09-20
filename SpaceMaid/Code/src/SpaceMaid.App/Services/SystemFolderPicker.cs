using System.Runtime.InteropServices;
using System.Text;

namespace SpaceMaid.App.Services;

/// <summary>
/// 系统"选择文件夹"对话框（Win32 <c>SHBrowseForFolder</c>）。
///
/// 为什么不用现成控件：net8.0 的 WPF 里没有 <c>OpenFolderDialog</c>（.NET 9 才有），
/// 而引入 WinForms 只为拿一个文件夹框会给整个工程带来 <c>Application</c> / <c>Window</c> 的命名冲突。
/// 这里直接调系统自己的文件夹选择对话框：**不是自绘伪控件**（需求 5.3-1）。
///
/// 字符集固定 Unicode（<c>W</c> 入口 + <c>CharSet.Unicode</c>），因此中文路径不会乱码。
/// </summary>
public sealed class FolderPickerService : IFolderPicker
{
    private const uint BifReturnOnlyFsDirs = 0x0001;
    private const uint BifNewDialogStyle = 0x0040;
    private const uint BifEditBox = 0x0010;

    public string? PickFolder(string title, string? initialDirectory)
    {
        var browse = new BROWSEINFO
        {
            hwndOwner = GetOwnerHandle(),
            lpszTitle = string.IsNullOrWhiteSpace(title) ? "请选择文件夹" : title,
            ulFlags = BifReturnOnlyFsDirs | BifNewDialogStyle | BifEditBox
        };

        var buffer = new StringBuilder(260);
        var pidl = SHBrowseForFolder(ref browse);
        if (pidl == IntPtr.Zero)
        {
            return null; // 用户取消
        }

        try
        {
            return SHGetPathFromIDList(pidl, buffer) ? buffer.ToString() : null;
        }
        finally
        {
            // 必须释放：否则每次打开对话框都泄漏一块 shell 内存
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    private static IntPtr GetOwnerHandle()
    {
        try
        {
            var window = System.Windows.Application.Current?.MainWindow;
            return window is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(window).Handle;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;

        public IntPtr pidlRoot;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszDisplayName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszTitle;

        public uint ulFlags;

        public IntPtr lpfn;

        public IntPtr lParam;

        public int iImage;
    }
}
