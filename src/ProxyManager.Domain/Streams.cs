namespace ProxyManager.Domain;

public enum StreamProtocol
{
    Tcp,
    Udp,
}

/// <summary>A raw TCP/UDP forwarder entry (YARP is HTTP-only, so streams get a dedicated subsystem).</summary>
public sealed class Stream
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public StreamProtocol Protocol { get; set; } = StreamProtocol.Tcp;

    /// <summary>Port the proxy listens on for this stream.</summary>
    public int ListenPort { get; set; }

    public string ForwardHost { get; set; } = string.Empty;

    public int ForwardPort { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
