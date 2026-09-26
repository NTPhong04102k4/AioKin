using System.IO.Compression;
using System.Text;

namespace AioKin.Services.Common.Storage;

/// <summary>
/// Trien khai that cua IBlobStorageService, goi Supabase Storage REST API. HttpClient duoc
/// dang ky qua AddHttpClient&lt;IBlobStorageService, SupabaseStorageService&gt; trong Program.cs
/// voi BaseAddress + header "apikey" da san — class nay khong tu cam service key.
/// </summary>
public class SupabaseStorageService : IBlobStorageService
{
    private readonly HttpClient _http;
    private const string Bucket = "promptvault";

    public SupabaseStorageService(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> UploadAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var compressed = Compress(content);
        using var body = new ByteArrayContent(compressed);
        body.Headers.ContentEncoding.Add("gzip");
        var response = await _http.PostAsync($"/storage/v1/object/{Bucket}/{path}", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return path;
    }

    public async Task<string> DownloadAsync(string path, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"/storage/v1/object/{Bucket}/{path}", cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return Decompress(bytes);
    }

    private static byte[] Compress(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        using (var writer = new StreamWriter(gzip, Encoding.UTF8))
            writer.Write(text);
        return output.ToArray();
    }

    private static string Decompress(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
