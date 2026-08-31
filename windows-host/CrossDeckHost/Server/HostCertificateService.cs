using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CrossDeckHost.Server;

/// <summary>
/// Owns the host's stable, self-signed TLS identity. The private key is protected with the
/// current Windows user's DPAPI and never written as a plaintext PFX.
/// </summary>
public sealed class HostCertificateService
{
    private const string FileName = "host-identity.pfx.dpapi";
    private readonly string _protectedCertificatePath;

    public X509Certificate2 Certificate { get; }
    public string Fingerprint { get; }
    public string SecurityCode => FormatSecurityCode(Fingerprint);

    public HostCertificateService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CrossDeckHost");
        Directory.CreateDirectory(appDataDir);
        _protectedCertificatePath = Path.Combine(appDataDir, FileName);

        Certificate = LoadOrCreate(appDataDir);
        Fingerprint = Convert.ToHexString(SHA256.HashData(Certificate.RawData));
    }

    private X509Certificate2 LoadOrCreate(string appDataDir)
    {
        try
        {
            if (File.Exists(_protectedCertificatePath))
            {
                var protectedBytes = File.ReadAllBytes(_protectedCertificatePath);
                var pfxBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return new X509Certificate2(
                    pfxBytes,
                    (string?)null,
                    X509KeyStorageFlags.Exportable |
                    X509KeyStorageFlags.UserKeySet |
                    X509KeyStorageFlags.PersistKeySet);
            }
        }
        catch
        {
            // A corrupt or inaccessible identity is replaced with a new identity. Existing
            // clients will detect the fingerprint change and require deliberate re-pairing.
        }

        var certificate = CreateSelfSignedCertificate();
        var pfx = certificate.Export(X509ContentType.Pfx);
        var protectedPfx = ProtectedData.Protect(pfx, null, DataProtectionScope.CurrentUser);
        var tempPath = Path.Combine(appDataDir, $"{FileName}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempPath, protectedPfx);
        File.Move(tempPath, _protectedCertificatePath, true);
        return certificate;
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=CrossDeck Host",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature,
            false));

        var serverAuth = new OidCollection
        {
            new Oid("1.3.6.1.5.5.7.3.1") // TLS Web Server Authentication
        };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(serverAuth, false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.AddYears(5);
        var generated = request.CreateSelfSigned(notBefore, notAfter);
        return new X509Certificate2(
            generated.Export(X509ContentType.Pfx),
            (string?)null,
            X509KeyStorageFlags.Exportable |
            X509KeyStorageFlags.UserKeySet |
            X509KeyStorageFlags.PersistKeySet);
    }

    private static string FormatSecurityCode(string fingerprint)
    {
        return string.Join("-", fingerprint
            .Chunk(4)
            .Take(2)
            .Concat(fingerprint.Chunk(4).TakeLast(2))
            .Select(group => new string(group)));
    }
}
