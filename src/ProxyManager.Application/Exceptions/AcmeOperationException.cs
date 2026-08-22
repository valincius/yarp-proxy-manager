namespace ProxyManager.Application.Exceptions;

/// <summary>
/// An ACME operation failed (account creation, challenge validation, issuance, ...).
/// Mapped to a 422 ProblemDetails response so the UI can show the real CA/network
/// error instead of a generic 500.
/// </summary>
public sealed class AcmeOperationException(string message, Exception? inner = null) : Exception(message, inner);
