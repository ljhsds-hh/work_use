using System.Windows;
using DllTool.App.Services;
using Microsoft.Extensions.Logging;
using MessageBox = HandyControl.Controls.MessageBox;

namespace DllTool.App.Services;

/// <summary>
/// 通过 HandyControl 消息框实现的覆盖确认。
/// </summary>
public sealed class MessageOverwriteConfirmation(ILogger<MessageOverwriteConfirmation> logger) : IOverwriteConfirmation
{
    public bool Confirm()
    {
        var result = MessageBox.Show(
            "已完成旧版DLL备份，即将使用新版DLL覆盖目标目录内对应文件，是否确认执行覆盖操作？",
            "覆盖确认",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        bool confirmed = result == MessageBoxResult.OK;
        logger.LogInformation("覆盖确认弹窗用户选择：{Result}", confirmed ? "确定" : "取消");
        return confirmed;
    }
}
