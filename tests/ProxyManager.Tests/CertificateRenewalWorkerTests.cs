using FluentAssertions;
using ProxyManager.Certificates;
using ProxyManager.Domain;
using Xunit;

namespace ProxyManager.Tests;

public sealed class CertificateRenewalWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static Certificate AcmeCertificate(DateTimeOffset? notAfter, DateTimeOffset? lastAttempt) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test",
        Domains = ["app.example.com"],
        Provider = CertificateProvider.Acme,
        Status = CertificateStatus.Issued,
        NotAfter = notAfter,
        LastRenewalAttempt = lastAttempt,
    };

    [Theory]
    [InlineData(10, null, true)]                       // expires soon, never renewed
    [InlineData(10, 1, false)]                          // expires soon, retried an hour ago
    [InlineData(10, 1440, true)]                        // expires soon, retried a day ago
    [InlineData(60, null, false)]                       // expires far out
    public void IsDue_EvaluatesWindowAndRetryInterval(int daysToExpiry, int? minutesSinceAttempt, bool expected)
    {
        var notAfter = Now.AddDays(daysToExpiry);
        var lastAttempt = minutesSinceAttempt is { } minutes ? Now.AddMinutes(-minutes) : (DateTimeOffset?)null;
        var certificate = AcmeCertificate(notAfter, lastAttempt);

        CertificateRenewalWorker.IsDue(certificate, Now).Should().Be(expected);
    }

    [Fact]
    public void IsDue_ManualAndRevokedCertificatesAreNeverDue()
    {
        CertificateRenewalWorker.IsDue(new Certificate
        {
            Provider = CertificateProvider.Manual,
            Status = CertificateStatus.Issued,
            NotAfter = Now.AddDays(10),
        }, Now).Should().BeFalse();

        var revoked = AcmeCertificate(Now.AddDays(10), null);
        revoked.Status = CertificateStatus.Revoked;
        CertificateRenewalWorker.IsDue(revoked, Now).Should().BeFalse();
    }
}
