using HandyControl.Controls;

namespace CreateTpl.Services;

/// <summary>基于 HandyControl Growl 的消息通知实现（窗口右上角气泡提醒）。</summary>
public class GrowlMessageService : IMessageService
{
    public void Info(string message) => Growl.Info(message);

    public void Success(string message) => Growl.Success(message);

    public void Warning(string message) => Growl.Warning(message);

    public void Error(string message) => Growl.Error(message);
}
