using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BLRP.ClothingLocator;

internal sealed record PreviewUploadProgress(int Completed, int Total, int Uploaded, int Unchanged);
internal sealed record PreviewUploadResult(string PublicUrl, int Uploaded, int Unchanged);

internal static class BackblazePreviewUpload
{
    internal static async Task<PreviewUploadResult> RunAsync(string output, string bucketName, string keyId, string applicationKey,
        IProgress<PreviewUploadProgress>? progress, CancellationToken cancellationToken, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(bucketName) || string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(applicationKey))
            throw new ArgumentException("Enter the Backblaze bucket name, application key ID and application key.");
        var manifest = JsonSerializer.Deserialize<ClothingPreviewManifest>(
            await File.ReadAllTextAsync(Path.Combine(output, ClothingPreviewExporter.ManifestName), cancellationToken));
        if (manifest == null || !manifest.Completed) throw new InvalidDataException("Complete the preview export before uploading.");
        using var client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromMinutes(2);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.backblazeb2.com/b2api/v4/b2_authorize_account");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(keyId + ":" + applicationKey)));
        using JsonDocument authorization = await SendAsync(client, request, cancellationToken);
        string token = authorization.RootElement.GetProperty("authorizationToken").GetString()!;
        JsonElement storage = authorization.RootElement.GetProperty("apiInfo").GetProperty("storageApi");
        string api = storage.GetProperty("apiUrl").GetString()!;
        string download = storage.GetProperty("downloadUrl").GetString()!;
        JsonElement allowed = storage.GetProperty("allowed");
        var capabilities = allowed.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()).ToHashSet();
        if (!capabilities.Contains("listFiles") || !capabilities.Contains("writeFiles"))
            throw new InvalidOperationException("The Backblaze application key needs listFiles and writeFiles permissions for this bucket.");
        string? bucketId = null;
        if (allowed.TryGetProperty("buckets", out JsonElement buckets) && buckets.ValueKind == JsonValueKind.Array)
        {
            JsonElement bucket = buckets.EnumerateArray().FirstOrDefault(value => value.GetProperty("name").GetString() == bucketName);
            // Bucket-scoped keys may omit the name unless name-listing is allowed.
            if (bucket.ValueKind != JsonValueKind.Object && buckets.GetArrayLength() == 1 && buckets[0].GetProperty("name").ValueKind == JsonValueKind.Null)
                bucket = buckets[0];
            if (bucket.ValueKind == JsonValueKind.Object) bucketId = bucket.GetProperty("id").GetString();
        }
        if (bucketId == null)
        {
            using JsonDocument list = await ApiAsync(client, api, token, "b2_list_buckets",
                new { accountId = authorization.RootElement.GetProperty("accountId").GetString(), bucketName }, cancellationToken);
            JsonElement bucket = list.RootElement.GetProperty("buckets").EnumerateArray().SingleOrDefault();
            if (bucket.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Bucket not found or not permitted by this application key.");
            bucketId = bucket.GetProperty("bucketId").GetString();
        }
        const string prefix = "clothing-previews/";
        string? namePrefix = allowed.GetProperty("namePrefix").GetString();
        if (namePrefix != null && !prefix.StartsWith(namePrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The application key must allow the clothing-previews/ prefix.");
        var existing = new Dictionary<string, (long Length, string Sha1)>(StringComparer.Ordinal);
        string? next = null;
        do
        {
            using JsonDocument list = await ApiAsync(client, api, token, "b2_list_file_names",
                new { bucketId, prefix, startFileName = next, maxFileCount = 10000 }, cancellationToken);
            foreach (JsonElement file in list.RootElement.GetProperty("files").EnumerateArray())
                existing[file.GetProperty("fileName").GetString()!] = (file.GetProperty("contentLength").GetInt64(), file.GetProperty("contentSha1").GetString()!);
            next = list.RootElement.GetProperty("nextFileName").GetString();
        } while (next != null);
        ClothingPreviewImage[] images = manifest.Images.Values.GroupBy(image => image.ImageHash).Select(group => group.First()).ToArray();
        var queue = new ConcurrentQueue<ClothingPreviewImage>(images);
        int completed = 0, uploaded = 0, unchanged = 0;
        async Task WorkerAsync()
        {
            string? uploadUrl = null, uploadToken = null;
            while (queue.TryDequeue(out ClothingPreviewImage? image))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetFileName(image.File) != image.File) throw new InvalidDataException("Unsafe preview cache filename.");
                byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(output, image.File), cancellationToken);
                if (Convert.ToHexString(SHA256.HashData(bytes)) != image.ImageHash) throw new InvalidDataException("Preview cache changed: " + image.File);
                string sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
                string name = prefix + image.ImageHash + ".webp";
                if (existing.TryGetValue(name, out var remote) && remote.Length == bytes.Length && remote.Sha1 == sha1)
                {
                    Interlocked.Increment(ref unchanged);
                }
                else
                {
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            if (uploadUrl == null)
                            {
                                using JsonDocument endpoint = await ApiAsync(client, api, token, "b2_get_upload_url", new { bucketId }, cancellationToken);
                                uploadUrl = endpoint.RootElement.GetProperty("uploadUrl").GetString();
                                uploadToken = endpoint.RootElement.GetProperty("authorizationToken").GetString();
                            }
                            using var upload = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                            upload.Headers.TryAddWithoutValidation("Authorization", uploadToken);
                            upload.Headers.Add("X-Bz-File-Name", name);
                            upload.Headers.Add("X-Bz-Content-Sha1", sha1);
                            upload.Headers.Add("X-Bz-Info-b2-cache-control", Uri.EscapeDataString("public, max-age=31536000, immutable"));
                            upload.Content = new ByteArrayContent(bytes);
                            upload.Content.Headers.ContentType = new MediaTypeHeaderValue("image/webp");
                            using JsonDocument result = await SendAsync(client, upload, cancellationToken);
                            if (result.RootElement.GetProperty("contentSha1").GetString() != sha1) throw new InvalidDataException("Backblaze upload checksum mismatch.");
                            break;
                        }
                        catch (HttpRequestException exception) when (attempt < 4 && (exception.StatusCode == null
                            || exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                            || (int)exception.StatusCode >= 500))
                        {
                            uploadUrl = null;
                            await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellationToken);
                        }
                    }
                    Interlocked.Increment(ref uploaded);
                }
                progress?.Report(new PreviewUploadProgress(Interlocked.Increment(ref completed), images.Length, uploaded, unchanged));
            }
        }
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => WorkerAsync()));
        string publicUrl = download.TrimEnd('/') + "/file/" + Uri.EscapeDataString(bucketName) + "/" + prefix.TrimEnd('/');
        // Verify anonymous downloads before advertising this location to NUI.
        if (images.Length > 0)
        {
            using var check = new HttpRequestMessage(HttpMethod.Get, publicUrl + "/" + images[0].ImageHash + ".webp");
            using HttpResponseMessage response = await client.SendAsync(check, cancellationToken);
            response.EnsureSuccessStatusCode();
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != images[0].ImageHash)
                throw new InvalidDataException("Public download did not match the uploaded preview. Check that the bucket is public.");
        }
        return new PreviewUploadResult(publicUrl, uploaded, unchanged);
    }

    private static async Task<JsonDocument> ApiAsync(HttpClient client, string api, string token, string method, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, api.TrimEnd('/') + "/b2api/v4/" + method);
        request.Headers.TryAddWithoutValidation("Authorization", token);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await SendAsync(client, request, cancellationToken);
    }

    private static async Task<JsonDocument> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Backblaze request failed ({(int)response.StatusCode}). Check bucket access and application-key permissions.", null, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    internal static void SelfTest(string output)
    {
        using var handler = new UploadCheckHandler();
        PreviewUploadResult first = RunAsync(output, "check-bucket", "test-id", "test-key", null, CancellationToken.None, handler).GetAwaiter().GetResult();
        PreviewUploadResult cached = RunAsync(output, "check-bucket", "test-id", "test-key", null, CancellationToken.None, handler).GetAwaiter().GetResult();
        if (first.Uploaded < 1 || cached.Uploaded != 0 || cached.Unchanged != first.Uploaded || handler.Retries != 1)
            throw new InvalidOperationException("Backblaze upload, retry or unchanged-file checks failed.");
        handler.Public = false;
        try
        {
            RunAsync(output, "check-bucket", "test-id", "test-key", null, CancellationToken.None, handler).GetAwaiter().GetResult();
            throw new InvalidOperationException("A private bucket was reported as publicly readable.");
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized) { }
    }

    private sealed class UploadCheckHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, byte[]> _files = new();
        public int Retries;
        public bool Public = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("b2_authorize_account")) return Json(new
            {
                accountId = "test-account", authorizationToken = "account-token",
                apiInfo = new { storageApi = new { apiUrl = "https://api.example.test", downloadUrl = "https://download.example.test",
                    allowed = new { buckets = new[] { new { id = "test-bucket", name = (string?)null } },
                        capabilities = new[] { "listFiles", "writeFiles" }, namePrefix = "clothing-previews/" } } }
            });
            if (path.Contains("/file/"))
            {
                if (request.Headers.Contains("Authorization")) throw new InvalidOperationException("Upload credentials leaked to public downloads.");
                return Public ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(_files["clothing-previews/" + path.Split('/').Last()]) }
                    : new(HttpStatusCode.Unauthorized);
            }
            if (path.EndsWith("b2_list_file_names")) return Json(new
            {
                files = _files.Select(file => new { fileName = file.Key, contentLength = file.Value.Length,
                    contentSha1 = Convert.ToHexString(SHA1.HashData(file.Value)).ToLowerInvariant() }).ToArray(),
                nextFileName = (string?)null
            });
            if (path.EndsWith("b2_get_upload_url")) return Json(new { uploadUrl = "https://upload.example.test/upload", authorizationToken = "upload-token" });
            if (path == "/upload")
            {
                if (Interlocked.CompareExchange(ref Retries, 1, 0) == 0) return new(HttpStatusCode.ServiceUnavailable);
                byte[] bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                string name = request.Headers.GetValues("X-Bz-File-Name").Single();
                string sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
                if (name != "clothing-previews/" + Convert.ToHexString(SHA256.HashData(bytes)) + ".webp"
                    || request.Headers.GetValues("X-Bz-Content-Sha1").Single() != sha1
                    || request.Headers.GetValues("Authorization").Single() != "upload-token"
                    || request.Content.Headers.ContentType!.MediaType != "image/webp"
                    || !Uri.UnescapeDataString(request.Headers.GetValues("X-Bz-Info-b2-cache-control").Single()).Contains("immutable"))
                    throw new InvalidOperationException("Backblaze content, integrity, scoped-token or cache headers were wrong.");
                _files[name] = bytes;
                return Json(new { contentSha1 = sha1 });
            }
            throw new InvalidOperationException("Unexpected Backblaze request: " + path);
        }
    }
}
