using PdfConverter.Api;
using PdfConverter.Api.Services;

const long maxUploadBytes = 50L * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadBytes;
});

builder.Services.AddSingleton<TableExtractor>();
builder.Services.AddSingleton<PdfToDocxConverter>();
builder.Services.AddSingleton<PdfToXlsxConverter>();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:5173", "http://localhost:5174"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

var apiKey = Environment.GetEnvironmentVariable("CONVERTER_API_KEY")
    ?? builder.Configuration["Converter:ApiKey"];
if (!string.IsNullOrWhiteSpace(apiKey))
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/convert"))
        {
            var provided = context.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (!string.Equals(provided, apiKey, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "A valid X-Api-Key header is required.",
                    code = "unauthorized",
                });
                return;
            }
        }

        await next();
    });
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "pdfjs-vue3-converter",
    version = "1.0.0",
}));

app.MapPost("/convert/{format}", async (
    string format,
    HttpRequest request,
    PdfToDocxConverter docxConverter,
    PdfToXlsxConverter xlsxConverter,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (format is not ("docx" or "xlsx"))
        {
            throw new ConversionException(404, "unsupported_format", "Supported formats are docx and xlsx.");
        }

        var bytes = await ReadBodyAsync(request, cancellationToken);
        ValidatePdf(bytes);

        var requestedName = request.Query["name"].FirstOrDefault();
        var baseName = FileNames.BaseName(requestedName);
        var title = string.IsNullOrWhiteSpace(requestedName)
            ? "Converted document"
            : baseName;

        var result = format == "docx"
            ? docxConverter.Convert(bytes, title)
            : xlsxConverter.Convert(bytes, title);

        var mime = format == "docx"
            ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        request.HttpContext.Response.Headers.CacheControl = "no-store";
        logger.LogInformation(
            "Converted PDF ({InputBytes} bytes) to {Format} ({OutputBytes} bytes).",
            bytes.Length,
            format,
            result.Length
        );

        return Results.File(result, mime, $"{baseName}.{format}");
    }
    catch (ConversionException exception)
    {
        return Results.Json(
            new { error = exception.Message, code = exception.Code },
            statusCode: exception.StatusCode
        );
    }
    catch (BadHttpRequestException exception)
    {
        logger.LogWarning(exception, "Invalid upload.");
        return Results.Json(
            new { error = "The upload is too large or malformed.", code = "invalid_upload" },
            statusCode: exception.StatusCode
        );
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(499);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "PDF conversion failed.");
        return Results.Json(
            new
            {
                error = "The PDF could not be converted. It may be damaged or use an unsupported layout.",
                code = "conversion_failed",
            },
            statusCode: 422
        );
    }
});

app.Run();

async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
{
    if (request.ContentLength is 0)
    {
        throw new ConversionException(400, "empty_body", "Upload a PDF file in the request body.");
    }

    await using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer, cancellationToken);
    if (buffer.Length == 0)
    {
        throw new ConversionException(400, "empty_body", "Upload a PDF file in the request body.");
    }

    if (buffer.Length > maxUploadBytes)
    {
        throw new ConversionException(413, "too_large", "The PDF exceeds the 50 MB upload limit.");
    }

    return buffer.ToArray();
}

void ValidatePdf(ReadOnlySpan<byte> bytes)
{
    if (bytes.Length < 5 || !bytes[..5].SequenceEqual("%PDF-"u8))
    {
        throw new ConversionException(400, "invalid_pdf", "The uploaded data is not a valid PDF file.");
    }
}
