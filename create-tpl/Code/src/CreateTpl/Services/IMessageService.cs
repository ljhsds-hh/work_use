namespace CreateTpl.Services;

/// <summary>用户消息通知抽象：隔离 HandyControl Growl，保持 ViewModel 可测试。</summary>
public interface IMessageService
{
    void Info(string message);
    void Success(string message);
    void Warning(string message);
    void Error(string message);
}
