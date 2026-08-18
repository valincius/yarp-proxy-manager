namespace ProxyManager.Application.Exceptions;

/// <summary>Thrown when a write would create conflicting proxy configuration (e.g. overlapping domains).</summary>
public sealed class DomainConflictException(string message) : Exception(message);
