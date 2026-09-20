using System.Threading;
using System.Windows;
using System.Windows.Media;
using CodeMemo.Models;
using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>
/// 外观服务的真实验证：在 STA 线程里按 App.xaml 的方式挂一整套皮肤资源，
/// 反复切换皮肤 / 字号 / 跟随系统，确认画刷真的跟着换（皮肤是「看得见」的行为，必须真加载资源字典才验证得了）。
/// </summary>
public class WindowsAppearanceServiceTests
{
    private sealed record Probe(
        string LightRegion,
        string DarkRegion,
        string LightText,
        string DarkText,
        double LightBodyFont,
        double LargeBodyFont,
        string UnknownThemeRegion,
        string SystemLightRegion,
        string SystemDarkRegion,
        string AfterFollowOffRegion,
        int SystemStartCount,
        int SystemStopCount);

    // 整个测试进程只能有一个 Application 实例，且资源字典加载要求 STA —— 用 Lazy 保证只跑一次
    private static readonly Lazy<Probe> Probed = new(RunProbe);

    [Fact]
    public void 暗色皮肤把区域画刷换成深色()
    {
        var probe = Probed.Value;

        Assert.True(IsLight(probe.LightRegion), $"默认皮肤应为浅色，实际 {probe.LightRegion}");
        Assert.True(IsDark(probe.DarkRegion), $"暗色皮肤应为深色，实际 {probe.DarkRegion}");
    }

    [Fact]
    public void 暗色皮肤下正文画刷换成浅色()
    {
        var probe = Probed.Value;

        Assert.Equal("#FF212121", probe.LightText);
        Assert.Equal("#FFFFFFFF", probe.DarkText);
    }

    [Fact]
    public void 字号资源按档位写入()
    {
        var probe = Probed.Value;

        Assert.Equal(13.5, probe.LightBodyFont);
        Assert.Equal(16, probe.LargeBodyFont);
    }

    [Fact]
    public void 未知皮肤名按明亮处理()
    {
        var probe = Probed.Value;

        Assert.True(IsLight(probe.UnknownThemeRegion), $"未知皮肤应回落浅色，实际 {probe.UnknownThemeRegion}");
    }

    [Fact]
    public void 跟随系统_系统亮就明亮_系统暗就暗色()
    {
        var probe = Probed.Value;

        Assert.True(IsLight(probe.SystemLightRegion), $"系统是亮色时应为浅色，实际 {probe.SystemLightRegion}");
        Assert.True(IsDark(probe.SystemDarkRegion), $"系统转暗后应变深色，实际 {probe.SystemDarkRegion}");
    }

    [Fact]
    public void 切到固定主题后_系统亮暗变化不再影响外观()
    {
        var probe = Probed.Value;

        Assert.True(IsDark(probe.AfterFollowOffRegion), $"固定暗色后系统转亮不应变浅，实际 {probe.AfterFollowOffRegion}");
    }

    [Fact]
    public void 跟随系统时开始监听_切回固定主题时停止监听()
    {
        var probe = Probed.Value;

        Assert.Equal(1, probe.SystemStartCount);
        Assert.Equal(1, probe.SystemStopCount);
    }

    private static bool IsLight(string color) => ToByte(color, 0) > 200;

    private static bool IsDark(string color) => ToByte(color, 0) < 80;

    private static byte ToByte(string color, int channel)
    {
        var value = ColorConverter.ConvertFromString(color);
        var c = (Color)value;
        return channel switch { 0 => c.R, 1 => c.G, _ => c.B };
    }

    private static Probe RunProbe()
    {
        Probe? probe = null;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application();
                var service = new WindowsAppearanceService();

                service.Apply(AppSettingValues.ThemeDefault, AppSettingValues.FontNormal);
                var lightRegion = Describe(app, "SecondaryRegionBrush");
                var lightText = Describe(app, "PrimaryTextBrush");
                var lightFont = (double)app.Resources[AppearanceScale.FontBodyKey];

                // 关键：同一进程里反复切换也要每次都真正换色（HC 的样式字典算过一次就不再变）
                service.Apply(AppSettingValues.ThemeDark, AppSettingValues.FontLarge);
                var darkRegion = Describe(app, "SecondaryRegionBrush");
                var darkText = Describe(app, "PrimaryTextBrush");
                var largeFont = (double)app.Resources[AppearanceScale.FontBodyKey];

                service.Apply("Neon", AppSettingValues.FontNormal);
                var unknown = Describe(app, "SecondaryRegionBrush");

                // 跟随系统：喂假的系统状态，避免依赖真机设置
                var system = new FakeSystemThemeSource { Light = true };
                var systemService = new WindowsAppearanceService(system);

                systemService.Apply(AppSettingValues.ThemeSystem, AppSettingValues.FontNormal);
                var systemLight = Describe(app, "SecondaryRegionBrush");

                system.Light = false;
                system.RaiseChanged();
                var systemDark = Describe(app, "SecondaryRegionBrush");

                // 切到固定暗色后，系统再怎么变都不该动外观，也不该继续监听
                systemService.Apply(AppSettingValues.ThemeDark, AppSettingValues.FontNormal);
                system.Light = true;
                system.RaiseChanged();
                var afterFollowOff = Describe(app, "SecondaryRegionBrush");

                probe = new Probe(lightRegion, darkRegion, lightText, darkText, lightFont, largeFont, unknown,
                    systemLight, systemDark, afterFollowOff, system.StartCount, system.StopCount);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(60)) || probe is null)
        {
            throw new InvalidOperationException($"外观探针未完成：{failure?.Message ?? "超时"}");
        }
        return probe;
    }

    private static string Describe(Application app, string key)
        => app.TryFindResource(key) is SolidColorBrush brush
            ? brush.Color.ToString()
            : "(未解析)";
}
