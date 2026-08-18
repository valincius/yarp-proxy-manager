using ProxyManager.Application;

namespace ProxyManager.Proxy;

public sealed class ConfigReloadNotifier(ProxyConfigReloader reloader) : IConfigReloadNotifier
{
    public void Notify() => reloader.RequestReload();
}
