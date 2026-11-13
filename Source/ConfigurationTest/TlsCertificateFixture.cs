namespace ROOT.Etcd.ConfigurationTest;

/// <summary>
/// Assembly-level fixture that prepares the TLS certificates once before any test runs.
/// </summary>
public sealed class TlsCertificateFixture
{
    public TlsCertificateFixture()
    {
        TestCertificates.EnsureGenerated();
    }
}
