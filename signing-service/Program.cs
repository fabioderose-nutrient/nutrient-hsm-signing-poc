using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

var p12Path = Environment.GetEnvironmentVariable("SIGNING_P12_PATH");
var signer = string.IsNullOrEmpty(p12Path) ? Pkcs11Signer() : Pkcs12Signer(p12Path);

var probe = RandomNumberGenerator.GetBytes(32);
using (var publicKey = signer.Certificate.GetRSAPublicKey() ?? throw new InvalidOperationException("The signer certificate has no RSA public key"))
{
    if (!publicKey.VerifyData(probe, signer.Sign(probe), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
    {
        throw new InvalidOperationException("The private key does not match the signer certificate");
    }
}

var certificatesResponse = new CertificatesResponse(
    [EncodePem(signer.Certificate)],
    signer.CaCertificates.Select(EncodePem).ToArray());

var app = builder.Build();

app.Logger.LogInformation("Signing key from {KeySource} matches certificate '{Subject}' issued by '{Issuer}'",
    signer.KeySource, signer.Certificate.Subject, signer.Certificate.Issuer);

app.MapPost("/sign", (SigningRequest request) =>
{
    app.Logger.LogInformation("Signing service action '{Action}'", request.Action);
    switch (request.Action)
    {
        case "get_certificates":
            return Results.Json(certificatesResponse);
        case "sign" when request.HashAlgorithm != "sha256":
            return Results.BadRequest($"Unsupported hash algorithm '{request.HashAlgorithm}', only sha256 is supported");
        case "sign" when request.DataToBeSigned is { Length: > 0 } encoded:
            var data = Convert.FromBase64String(encoded);
            var signature = signer.Sign(data);
            app.Logger.LogInformation("Signed {Length} bytes with the {KeySource}, signature is {SignatureLength} bytes", data.Length, signer.KeySource, signature.Length);
            return Results.Bytes(signature, "application/octet-stream");
        case "sign":
            return Results.BadRequest("Missing data_to_be_signed");
        default:
            return Results.BadRequest($"Unsupported action '{request.Action}'");
    }
});

app.Run();

static Signer Pkcs11Signer()
{
    var keyLabel = Required("PKCS11_KEY_LABEL");
    var hsm = new Pkcs11(Required("PKCS11_LIBRARY"), int.Parse(Required("PKCS11_SLOT")), Required("PKCS11_PIN"), keyLabel);
    var certificate = X509Certificate2.CreateFromPem(File.ReadAllText(Required("SIGNER_CERTIFICATE_PATH")));
    var caCertificates = new X509Certificate2Collection();
    caCertificates.ImportFromPemFile(Required("CA_CERTIFICATES_PATH"));
    return new Signer(hsm.Sign, certificate, caCertificates, $"PKCS#11 key '{keyLabel}' in slot {hsm.SlotId}");
}

static Signer Pkcs12Signer(string path)
{
    var bundle = new X509Certificate2Collection();
    bundle.Import(path, Environment.GetEnvironmentVariable("SIGNING_P12_PASSWORD"), X509KeyStorageFlags.EphemeralKeySet);
    var certificate = bundle.FirstOrDefault(candidate => candidate.HasPrivateKey) ?? throw new InvalidOperationException($"No certificate with a private key in {path}");
    var key = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("The PKCS#12 private key is not an RSA key");
    var caCertificates = new X509Certificate2Collection(bundle.Where(candidate => !candidate.HasPrivateKey).ToArray());
    return new Signer(data =>
    {
        lock (key)
        {
            return key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }, certificate, caCertificates, $"PKCS#12 file '{path}'");
}

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Environment variable {name} is required");

static string EncodePem(X509Certificate2 certificate) =>
    Convert.ToBase64String(Encoding.ASCII.GetBytes(certificate.ExportCertificatePem()));

record Signer(Func<byte[], byte[]> Sign, X509Certificate2 Certificate, X509Certificate2Collection CaCertificates, string KeySource);

record SigningRequest(string? Action, string? HashAlgorithm, string? DataToBeSigned);

record CertificatesResponse(string[] Certificates, string[] CaCertificates);
