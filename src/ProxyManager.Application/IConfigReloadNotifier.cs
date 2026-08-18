using ProxyManager.Application.ProxyHosts;

namespace ProxyManager.Application;

/// <summary>
/// Raised after any configuration-changing write (hosts, redirects, streams, access lists…).
/// The host wires this to the YARP config reloader so changes take effect without a restart.
/// </summary>
public interface IConfigReloadNotifier
{
    void Notify();
}
