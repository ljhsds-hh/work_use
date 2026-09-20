using System.Windows;

namespace SpaceMaid.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 需求 3.6：exe 清单已固定 requireAdministrator；此处做运行期自检，被以普通权限拉起时拒绝进入清理流程。
        // 具体处理在 Task 12/13 接入 CoreServices 与界面后完善；CLI 分支在 Task 14 接入。
    }
}
