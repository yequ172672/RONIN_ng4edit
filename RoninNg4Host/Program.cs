using RoninNg4Host;
using Microsoft.AspNetCore.Http.Features;
using YakumoLib.Formats;

int port = 6170;
for (int i = 0; i + 1 < args.Length; i++)
    if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int parsed)) port = parsed;
if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Services.AddSingleton<HostState>();
builder.Services.AddSingleton<Ng4AssetService>();
builder.Services.AddHostedService<HostIdleShutdownService>();
const long MaxGlbUploadBytes = 128L * 1024 * 1024;
const long MaxModUploadBytes = 1200L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxModUploadBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = MaxModUploadBytes);
WebApplication app = builder.Build();
HostState activityState = app.Services.GetRequiredService<HostState>();
app.Use(async (context, next) =>
{
    using IDisposable requestLease = activityState.BeginRequest();
    await next();
});

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", protocol_version = 1 }));
app.MapGet("/status", (HostState state) =>
{
    var snapshot = state.Snapshot();
    return Results.Ok(new { state = snapshot.Status, assets_directory = snapshot.AssetsDirectory, error = snapshot.Error, asset_count = snapshot.Library?.Count ?? 0 });
});
app.MapPost("/configure", async (ConfigureRequest request, Ng4AssetService service, CancellationToken cancellationToken) =>
{
    int count = await service.ConfigureAsync(request.AssetsDirectory, cancellationToken);
    return Results.Ok(new { ok = true, asset_count = count });
});
app.MapGet("/tree", (string? path, Ng4AssetService service) => Results.Ok(new { items = service.GetTree(path) }));
app.MapGet("/browser/tree", (string? path, Ng4AssetService service) => Results.Ok(service.GetBrowserTree(path)));
app.MapGet("/mesh/rngp", (string id, Ng4AssetService service) => Results.File(service.BuildRngp(id), "application/octet-stream", enableRangeProcessing: false));
app.MapGet("/mesh/preview-materials", (string id, Ng4AssetService service) => Results.Ok(service.GetPreviewMaterials(id)));
app.MapGet("/texture/raw", (string id, int? max_size, Ng4AssetService service, HttpResponse response) =>
{
    TexturePreviewRgba texture = service.GetRawTexture(id, max_size ?? 512);
    response.Headers["X-Texture-Width"] = texture.Width.ToString();
    response.Headers["X-Texture-Height"] = texture.Height.ToString();
    response.Headers["X-Texture-Format"] = "RGBA8";
    response.Headers["X-Texture-Encoding"] = "raw";
    response.Headers["X-Texture-Source-Width"] = texture.SourceWidth.ToString();
    response.Headers["X-Texture-Source-Height"] = texture.SourceHeight.ToString();
    response.Headers["X-Texture-Mip"] = texture.Mip.ToString();
    response.Headers["X-Texture-Mip-Level"] = texture.Mip.ToString();
    response.Headers["X-Texture-SRGB"] = texture.Srgb ? "true" : "false";
    response.Headers["X-Texture-Row-Origin"] = "top-left";
    return Results.File(texture.Rgba, "application/octet-stream", enableRangeProcessing: false);
});
app.MapGet("/mesh/glb", (string id, Ng4AssetService service) => Results.File(service.BuildGlb(id), "model/gltf-binary", "model.glb"));
app.MapGet("/mesh/template-mdl", (string id, Ng4AssetService service) => Results.File(service.GetTemplateMdl(id), "application/octet-stream", "modeldata.mdl"));
app.MapPost("/mesh/convert-glb", async (HttpRequest request, Ng4AssetService service, CancellationToken cancellationToken) =>
{
    IFormCollection form = await request.ReadFormAsync(cancellationToken);
    string id = form["asset_id"].ToString();
    IFormFile glb = form.Files.GetFile("glb") ?? throw new InvalidDataException("Multipart field 'glb' is required.");
    if (glb.Length > MaxGlbUploadBytes) throw new InvalidDataException("GLB upload exceeds 128 MiB.");
    await using Stream stream = glb.OpenReadStream();
    ConversionResult result = await service.ConvertGlbAsync(id, stream, cancellationToken);
    request.HttpContext.Response.Headers["X-Ronin-Batch-Count"] = result.BatchCount.ToString();
    request.HttpContext.Response.Headers["X-Ronin-Group-Count"] = result.GroupCount.ToString();
    return Results.File(result.Data, "application/octet-stream", "replacement.mdl");
}).DisableAntiforgery();
app.MapPost("/mesh/texture-set", async (TextureSetRequest request, Ng4AssetService service, HttpResponse response, CancellationToken cancellationToken) =>
{
    using TemporaryTextureArchive archive = service.ExportTextureSetZip(request.AssetId, request.Format);
    response.ContentType = "application/zip";
    response.Headers.ContentDisposition = $"attachment; filename=texture-set-{request.Format.ToLowerInvariant()}.zip";
    await response.SendFileAsync(archive.Path, cancellationToken);
});
app.MapPost("/mods/ng4mod", async (HttpRequest request, Ng4AssetService service, CancellationToken cancellationToken) =>
{
    IFormCollection form = await request.ReadFormAsync(cancellationToken);
    string id = form["asset_id"].ToString();
    string metadataJson = form["metadata"].ToString();
    if (metadataJson.Length > 64 * 1024) throw new InvalidDataException("NG4MOD metadata exceeds 64 KiB.");
    Ng4ModUploadMetadata metadata = Ng4ModUploadMetadata.Parse(metadataJson);
    IFormFile glb = form.Files.GetFile("glb") ?? throw new InvalidDataException("Multipart field 'glb' is required.");
    IFormFile textures = form.Files.GetFile("texture_set") ?? throw new InvalidDataException("Multipart field 'texture_set' is required.");
    IFormFile? icon = form.Files.GetFile("cover") ?? form.Files.GetFile("icon");
    if (glb.Length > MaxGlbUploadBytes) throw new InvalidDataException("GLB upload exceeds 128 MiB.");
    if (textures.Length > 1024L * 1024 * 1024) throw new InvalidDataException("Texture-set upload exceeds 1 GiB.");
    if (icon?.Length > 8L * 1024 * 1024) throw new InvalidDataException("PNG cover upload exceeds 8 MiB.");

    await using Stream glbStream = glb.OpenReadStream();
    await using Stream textureStream = textures.OpenReadStream();
    await using Stream? iconStream = icon?.OpenReadStream();
    using TemporaryNg4ModPackage package = await service.ExportNg4ModAsync(
        id, glbStream, textureStream, metadata, iconStream, icon?.FileName, cancellationToken);
    request.HttpContext.Response.Headers["X-Ronin-Batch-Count"] = package.BatchCount.ToString();
    request.HttpContext.Response.Headers["X-Ronin-Group-Count"] = package.GroupCount.ToString();
    request.HttpContext.Response.Headers["X-Ronin-Changed-Textures"] = package.ChangedTextures.ToString();
    request.HttpContext.Response.ContentType = "application/vnd.ronin.ng4mod";
    request.HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{package.FileName}\"";
    await request.HttpContext.Response.SendFileAsync(package.Path, cancellationToken);
}).DisableAntiforgery();
app.MapPost("/mods/ng4mod-batch", async (HttpRequest request, Ng4AssetService service, CancellationToken cancellationToken) =>
{
    IFormCollection form = await request.ReadFormAsync(cancellationToken);
    IReadOnlyList<Ng4ModBatchItem> items = Ng4ModBatchContract.Parse(form["models"].ToString());
    Ng4ModUploadMetadata metadata = Ng4ModUploadMetadata.Parse(form["metadata"].ToString());
    var uploads = new List<(string AssetId, Stream Glb, Stream TextureSet)>();
    try
    {
        foreach (Ng4ModBatchItem item in items)
        {
            IFormFile glb = form.Files.GetFile(item.GlbField) ?? throw new InvalidDataException($"Multipart field '{item.GlbField}' is required.");
            IFormFile textures = form.Files.GetFile(item.TextureField) ?? throw new InvalidDataException($"Multipart field '{item.TextureField}' is required.");
            if (glb.Length > MaxGlbUploadBytes || textures.Length > 1024L * 1024 * 1024)
                throw new InvalidDataException("NG4MOD batch contains an oversized model or texture upload.");
            uploads.Add((item.AssetId, glb.OpenReadStream(), textures.OpenReadStream()));
        }
        IFormFile? icon = form.Files.GetFile("cover") ?? form.Files.GetFile("icon");
        if (icon?.Length > 8L * 1024 * 1024) throw new InvalidDataException("PNG cover upload exceeds 8 MiB.");
        await using Stream? iconStream = icon?.OpenReadStream();
        using TemporaryNg4ModBatchPackage package = await service.ExportNg4ModBatchAsync(uploads, metadata, iconStream, icon?.FileName, cancellationToken);
        request.HttpContext.Response.Headers["X-Ronin-Batch-Count"] = package.BatchCount.ToString();
        request.HttpContext.Response.Headers["X-Ronin-Group-Count"] = package.GroupCount.ToString();
        request.HttpContext.Response.Headers["X-Ronin-Changed-Textures"] = package.ChangedTextures.ToString();
        request.HttpContext.Response.ContentType = "application/vnd.ronin.ng4mod";
        request.HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{package.FileName}\"";
        await request.HttpContext.Response.SendFileAsync(package.Path, cancellationToken);
    }
    finally
    {
        foreach ((_, Stream glb, Stream textures) in uploads)
        {
            await glb.DisposeAsync();
            await textures.DisposeAsync();
        }
    }
}).DisableAntiforgery();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    Exception? error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    context.Response.StatusCode = error switch
    {
        KeyNotFoundException => StatusCodes.Status404NotFound,
        ArgumentException or InvalidDataException or DirectoryNotFoundException or BadHttpRequestException => StatusCodes.Status400BadRequest,
        InvalidOperationException => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };
    await context.Response.WriteAsJsonAsync(new { error = error?.Message ?? "Unknown host error." });
}));

app.Run();

public sealed record ConfigureRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("assets_directory")]
    public string AssetsDirectory { get; init; } = string.Empty;
}
public sealed record TextureSetRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("asset_id")]
    public string AssetId { get; init; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("format")]
    public string Format { get; init; } = "png";
}
