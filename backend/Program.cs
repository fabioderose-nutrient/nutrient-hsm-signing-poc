using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var engine = new HttpClient { BaseAddress = new Uri(Required("DOCUMENT_ENGINE_URL")), Timeout = TimeSpan.FromMinutes(5) };
engine.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", $"token=\"{Required("DOCUMENT_ENGINE_TOKEN")}\"");
var signingServiceUrl = Required("SIGNING_SERVICE_URL");
var signingService = new HttpClient();
var defaultOwnerPassword = Environment.GetEnvironmentVariable("OWNER_PASSWORD") ?? "";
var defaultPermissions = Required("PDF_PERMISSIONS");
var defaultFieldText = Required("FIELD_TEXT");
var defaultSignerName = Environment.GetEnvironmentVariable("SIGNER_NAME") ?? "";
var defaultSignatureReason = Environment.GetEnvironmentVariable("SIGNATURE_REASON") ?? "";
var defaultSignatureLocation = Environment.GetEnvironmentVariable("SIGNATURE_LOCATION") ?? "";
var field = new FieldLayout(
    int.Parse(Required("FIELD_PAGE"), CultureInfo.InvariantCulture),
    Number("FIELD_X"), Number("FIELD_Y"), Number("FIELD_WIDTH"), Number("FIELD_HEIGHT"),
    Required("FIELD_NAME"), Required("FIELD_FONT"), int.Parse(Required("FIELD_FONT_SIZE"), CultureInfo.InvariantCulture));

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache" });

app.MapGet("/api/defaults", () => Results.Json(new
{
    fieldText = defaultFieldText,
    field = new { page = field.Page, x = field.X, y = field.Y, width = field.Width, height = field.Height, font = field.Font, fontSize = field.FontSize },
    permissions = SplitList(defaultPermissions),
    signerName = defaultSignerName,
    signatureReason = defaultSignatureReason,
    signatureLocation = defaultSignatureLocation
}));

app.MapPost("/api/process", async (HttpRequest request) =>
{
    if (!request.HasFormContentType || (await request.ReadFormAsync()).Files["file"] is not { Length: > 0 } file)
    {
        return Error(400, "Send multipart/form-data with the PDF in the field 'file'");
    }
    var form = request.Form;
    var protect = Switch(form, "password");
    var ownerPassword = Value(form, "ownerPassword") ?? defaultOwnerPassword;
    if (protect && ownerPassword.Length == 0)
    {
        return Error(400, "No owner password: send 'ownerPassword' or set OWNER_PASSWORD");
    }
    using var buffer = new MemoryStream();
    await file.CopyToAsync(buffer);
    var pdf = buffer.ToArray();
    try
    {
        if (Switch(form, "field"))
        {
            var instructions = new { parts = new[] { new { file = "document" } }, actions = new[] { new { type = "applyInstantJson", file = "instant" } }, output = new { type = "pdf" } };
            pdf = await Build("field", pdf, instructions, FieldInstantJson(Value(form, "fieldText") ?? defaultFieldText));
        }
        if (protect)
        {
            var permissions = SplitList(Value(form, "permissions") ?? defaultPermissions);
            var instructions = new { parts = new[] { new { file = "document" } }, output = new { type = "pdf", owner_password = ownerPassword, user_permissions = permissions } };
            pdf = await Build("password", pdf, instructions);
        }
        if (Switch(form, "sign"))
        {
            var metadata = new SignatureMetadata(
                Value(form, "signerName") ?? defaultSignerName,
                Value(form, "signatureReason") ?? defaultSignatureReason,
                Value(form, "signatureLocation") ?? defaultSignatureLocation);
            pdf = await Sign(pdf, protect ? ownerPassword : null, metadata);
        }
    }
    catch (EngineException exception)
    {
        return Results.Json(new { error = $"Document Engine returned HTTP {exception.Status} at step '{exception.Step}'", status = exception.Status, body = exception.Body }, statusCode: 502);
    }
    catch (HttpRequestException exception)
    {
        return Error(502, $"Service unreachable: {exception.Message}");
    }
    return Results.File(pdf, "application/pdf", Path.GetFileNameWithoutExtension(file.FileName) + "-processed.pdf");
});

app.Run();

object FieldInstantJson(string text)
{
    var widgetId = Guid.NewGuid().ToString("N");
    return new
    {
        format = "https://pspdfkit.com/instant-json/v1",
        formFields = new[]
        {
            new
            {
                v = 1, type = "pspdfkit/form-field/text", name = field.Name, label = field.Name, annotationIds = new[] { widgetId },
                multiLine = true, doNotSpellCheck = false, doNotScroll = false, comb = false, defaultValue = text
            }
        },
        annotations = new[]
        {
            new
            {
                v = 1, type = "pspdfkit/widget", id = widgetId, pageIndex = field.Page - 1,
                bbox = new[] { field.X, field.Y, field.Width, field.Height }, formFieldName = field.Name,
                font = field.Font, fontSize = field.FontSize, fontColor = "#000000", backgroundColor = "#FFFFFF", borderWidth = 0,
                horizontalAlign = "left", verticalAlign = "top", opacity = 1
            }
        },
        formFieldValues = new[] { new { v = 1, type = "pspdfkit/form-field-value", name = field.Name, value = text } }
    };
}

async Task<byte[]> Build(string step, byte[] pdf, object instructions, object? instantJson = null)
{
    var content = new MultipartFormDataContent { { JsonPart(instructions), "instructions" }, { PdfPart(pdf), "document", "document.pdf" } };
    if (instantJson is not null)
    {
        content.Add(JsonPart(instantJson), "instant", "instant.json");
    }
    return await CallEngine(step, "/api/build", content);
}

async Task<byte[]> Sign(byte[] pdf, string? pdfPassword, SignatureMetadata metadata)
{
    var signerName = metadata.SignerName.Length > 0 ? metadata.SignerName : (await SignerCertificate()).GetNameInfo(X509NameType.SimpleName, false);
    var signatureMetadata = new { signerName, signatureReason = NullIfEmpty(metadata.Reason), signatureLocation = NullIfEmpty(metadata.Location) };
    var data = new { signatureType = "cades", cadesLevel = "b-b", signatureContainer = "raw", hashAlgorithm = "sha256", signatureMetadata };
    var content = new MultipartFormDataContent { { PdfPart(pdf), "file", "document.pdf" }, { JsonPart(data), "data" } };
    return await CallEngine("sign", "/api/sign", content, pdfPassword);
}

async Task<byte[]> CallEngine(string step, string path, MultipartFormDataContent content, string? pdfPassword = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
    if (pdfPassword is not null)
    {
        request.Headers.Add("pspdfkit-pdf-password", "base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(pdfPassword)));
    }
    using var response = await engine.SendAsync(request);
    var body = await response.Content.ReadAsByteArrayAsync();
    return response.IsSuccessStatusCode ? body : throw new EngineException(step, (int)response.StatusCode, Encoding.UTF8.GetString(body));
}

async Task<X509Certificate2> SignerCertificate()
{
    using var response = await signingService.PostAsJsonAsync(signingServiceUrl, new { action = "get_certificates" });
    response.EnsureSuccessStatusCode();
    var payload = await response.Content.ReadFromJsonAsync<CertificatesPayload>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
        ?? throw new InvalidOperationException("Empty get_certificates response from the signing service");
    return X509Certificate2.CreateFromPem(Encoding.ASCII.GetString(Convert.FromBase64String(payload.Certificates[0])));
}

static bool Switch(IFormCollection form, string name) =>
    form[name].ToString().Trim().ToLowerInvariant() is "true" or "on" or "1";

static string? Value(IFormCollection form, string name) =>
    form[name].ToString() is { Length: > 0 } value ? value : null;

static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);

static StringContent JsonPart(object value) => new(JsonSerializer.Serialize(value, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }), Encoding.UTF8, "application/json");

static string[] SplitList(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

static string? NullIfEmpty(string value) => value.Length > 0 ? value : null;

static ByteArrayContent PdfPart(byte[] pdf) => new(pdf) { Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") } };

static double Number(string name) => double.Parse(Required(name), CultureInfo.InvariantCulture);

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Environment variable {name} is required");

record FieldLayout(int Page, double X, double Y, double Width, double Height, string Name, string Font, int FontSize);

record CertificatesPayload(string[] Certificates);

record SignatureMetadata(string SignerName, string Reason, string Location);

sealed class EngineException(string step, int status, string body) : Exception($"Document Engine error at step '{step}'")
{
    public string Step { get; } = step;
    public int Status { get; } = status;
    public string Body { get; } = body;
}
