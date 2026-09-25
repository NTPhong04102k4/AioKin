# PromptVault — Tối ưu dung lượng lưu trữ & Tự động bổ sung ngữ cảnh Prompt (AI Enrichment)

**Phạm vi tài liệu:** Giải quyết 2 vấn đề thực tế khi scale ứng dụng:
1. Database phình to vì lưu toàn bộ nội dung prompt/lịch sử chỉnh sửa trực tiếp trong Postgres.
2. User thường nhập ý tưởng ngắn, thiếu ngữ cảnh — cần cơ chế tự động mở rộng thành prompt đầy đủ bằng AI.

---

## 1. Tối ưu dung lượng — Tiered Storage (DB + Cloud Object Storage)

### 1.1. Vấn đề

- Bảng `vault.prompts.content` lưu text trực tiếp trong Postgres → mỗi row có thể vài KB, nhân với `prompt_versions` (mỗi lần sửa lưu bản đầy đủ) và `backup_snapshots` → DB phình rất nhanh.
- Supabase free-tier chỉ có 500MB–1GB database storage, nhưng Storage (object storage, S3-compatible) tính phí theo GB, rẻ hơn nhiều và không giới hạn row size.

### 1.2. Chiến lược: Nội dung ngắn giữ trong DB, nội dung dài đẩy ra Storage

```
content <= 8KB    → lưu thẳng trong Postgres (đọc/search FTS bình thường)
content >  8KB    → upload lên Supabase Storage (gzip), DB chỉ giữ path + preview 200 ký tự
prompt_versions   → LUÔN externalize (lịch sử ít đọc lại, không cần FTS)
```

### 1.3. Schema bổ sung

```sql
ALTER TABLE vault.prompts
    ADD COLUMN content_size_bytes integer,
    ADD COLUMN is_externalized boolean NOT NULL DEFAULT false,   -- true = content_full nằm ở Storage
    ADD COLUMN content_storage_path character varying(500);       -- vd: 'prompts/{space_id}/{prompt_id}.md.gz'

-- Tự tính size mỗi khi content thay đổi
CREATE OR REPLACE FUNCTION vault.fn_prompts_calc_size()
RETURNS TRIGGER AS $$
BEGIN
    NEW.content_size_bytes := octet_length(NEW.content);
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_prompts_calc_size
    BEFORE INSERT OR UPDATE OF content ON vault.prompts
    FOR EACH ROW
    EXECUTE FUNCTION vault.fn_prompts_calc_size();

-- prompt_versions: content giờ optional (NULL nếu đã externalize hết)
ALTER TABLE vault.prompt_versions
    ADD COLUMN content_storage_path character varying(500),
    ALTER COLUMN content DROP NOT NULL;
```

### 1.4. Backend `.NET` — `IBlobStorageService` (Supabase Storage)

```csharp
public interface IBlobStorageService
{
    Task<string> UploadAsync(string path, string content);
    Task<string> DownloadAsync(string path);
}

public class SupabaseStorageService : IBlobStorageService
{
    private readonly HttpClient _http;
    private readonly string _bucket = "prompt-contents";

    public async Task<string> UploadAsync(string path, string content)
    {
        var compressed = GzipCompress(content);
        var response = await _http.PostAsync(
            $"/storage/v1/object/{_bucket}/{path}",
            new ByteArrayContent(compressed));
        response.EnsureSuccessStatusCode();
        return path;
    }

    public async Task<string> DownloadAsync(string path)
    {
        var response = await _http.GetAsync($"/storage/v1/object/{_bucket}/{path}");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return GzipDecompress(bytes);
    }

    private static byte[] GzipCompress(string text)
    {
        using var output = new MemoryStream();
        using var gzip = new GZipStream(output, CompressionLevel.Optimal);
        using var writer = new StreamWriter(gzip);
        writer.Write(text);
        writer.Flush();
        return output.ToArray();
    }

    private static string GzipDecompress(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }
}
```

### 1.5. `PromptService.cs` — quyết định externalize khi lưu

```csharp
public async Task<Prompt> SavePromptAsync(Prompt prompt)
{
    const int THRESHOLD = 8 * 1024; // 8KB

    if (Encoding.UTF8.GetByteCount(prompt.Content) > THRESHOLD)
    {
        var path = $"prompts/{prompt.SpaceId}/{prompt.PromptId}.md.gz";
        await _blobStorage.UploadAsync(path, prompt.Content);

        prompt.ContentStoragePath = path;
        prompt.IsExternalized = true;
        prompt.Content = prompt.Content[..Math.Min(200, prompt.Content.Length)] + "…"; // preview cho FTS/list
    }
    else
    {
        prompt.IsExternalized = false;
        prompt.ContentStoragePath = null;
    }

    _db.Prompts.Update(prompt);
    await _db.SaveChangesAsync();
    return prompt;
}

public async Task<string> GetFullContentAsync(Guid promptId)
{
    var prompt = await _db.Prompts.FindAsync(promptId);
    if (!prompt.IsExternalized) return prompt.Content;

    // Cache lại (Redis/memory) 1 thời gian ngắn — tránh gọi Storage API mỗi lần mở prompt
    return await _cache.GetOrCreateAsync($"prompt-content:{promptId}",
        async _ => await _blobStorage.DownloadAsync(prompt.ContentStoragePath));
}
```

### 1.6. Background job — Retrofit dữ liệu cũ đã tồn tại trong DB

Áp dụng threshold mới cho dữ liệu **đã có sẵn** trước khi tính năng này ra đời — chạy 1 lần (hoặc theo batch định kỳ đến khi hết):

```csharp
public class RetrofitExternalizeJob : IHostedService
{
    private readonly IServiceProvider _services;
    private Timer? _timer;

    public Task StartAsync(CancellationToken ct)
    {
        _timer = new Timer(async _ => await RunBatchAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(30));
        return Task.CompletedTask;
    }

    private async Task RunBatchAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IBlobStorageService>();

        // Chỉ xử lý prompt CHƯA externalize và content > 8KB, theo batch 100 dòng/lần
        // tránh lock bảng lâu hoặc quá tải Storage API cùng lúc
        var candidates = await db.Prompts
            .Where(p => !p.IsExternalized && p.ContentSizeBytes > 8192)
            .OrderBy(p => p.UpdatedDate)
            .Take(100)
            .ToListAsync();

        if (candidates.Count == 0)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite); // hết việc thì dừng hẳn
            return;
        }

        foreach (var prompt in candidates)
        {
            var path = $"prompts/{prompt.SpaceId}/{prompt.PromptId}.md.gz";
            await storage.UploadAsync(path, prompt.Content);

            var fullContent = prompt.Content;
            prompt.ContentStoragePath = path;
            prompt.IsExternalized = true;
            prompt.Content = fullContent[..Math.Min(200, fullContent.Length)] + "…";
        }

        await db.SaveChangesAsync();
    }

    public Task StopAsync(CancellationToken ct)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }
}
```

Đăng ký trong `Program.cs`:
```csharp
builder.Services.AddHostedService<RetrofitExternalizeJob>();
```

---

## 2. Tự động bổ sung ngữ cảnh Prompt (AI Enrichment)

### 2.1. Ý tưởng

User nhập prompt ngắn (thiếu ngữ cảnh) → hệ thống gọi LLM (Claude/GPT) để mở rộng thành prompt đầy đủ (vai trò, ngữ cảnh, định dạng output, ràng buộc, biến động `{variable}` gợi ý) → user xem preview, chỉnh sửa nếu cần → lưu.

### 2.2. Schema bổ sung

```sql
ALTER TABLE vault.prompts
    ADD COLUMN raw_input text,                            -- input gốc user nhập (ngắn)
    ADD COLUMN is_ai_enriched boolean NOT NULL DEFAULT false,
    ADD COLUMN enrichment_model character varying(50),      -- 'claude-sonnet-5', 'gpt-4o'...
    ADD COLUMN enriched_at timestamp with time zone;

-- Cache kết quả enrich theo hash(raw_input) — tránh gọi LLM lại cho input giống nhau, tiết kiệm chi phí
CREATE TABLE vault.enrichment_cache (
    cache_key character varying(64) NOT NULL,   -- sha256(raw_input + category + space_type)
    enriched_content text NOT NULL,
    model character varying(50) NOT NULL,
    hit_count integer NOT NULL DEFAULT 1,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_enrichment_cache PRIMARY KEY (cache_key)
);
```

### 2.3. Luồng xử lý end-to-end

```
1. User nhập raw_input ngắn: "viết caption quảng cáo sản phẩm skincare"
2. Client gọi POST /api/v1/prompts/enrich { rawInput, category, spaceType }
3. Backend kiểm tra enrichment_cache theo hash(rawInput + category + spaceType)
   → có cache: trả ngay, tăng hit_count
   → chưa có: build system prompt, gọi LLM API
4. LLM trả về prompt đầy đủ + gợi ý {variable} → lưu cache → trả về client
5. Client hiển thị PREVIEW (side-by-side raw vs enriched) → user chỉnh sửa nếu muốn
6. User bấm Lưu → content = bản đầy đủ (đã/chưa sửa), raw_input = bản gốc, is_ai_enriched = true
```

### 2.4. Backend `.NET` — `PromptEnrichmentService.cs`

```csharp
public interface IPromptEnrichmentService
{
    Task<EnrichmentResult> EnrichAsync(string rawInput, string? category, string spaceType);
}

public class PromptEnrichmentService : IPromptEnrichmentService
{
    private readonly HttpClient _http;
    private readonly AppDbContext _db;

    public async Task<EnrichmentResult> EnrichAsync(string rawInput, string? category, string spaceType)
    {
        var cacheKey = ComputeSha256($"{rawInput}|{category}|{spaceType}");
        var cached = await _db.EnrichmentCache.FindAsync(cacheKey);
        if (cached != null)
        {
            cached.HitCount++;
            await _db.SaveChangesAsync();
            return new EnrichmentResult { EnrichedContent = cached.EnrichedContent, Model = cached.Model };
        }

        var systemPrompt = BuildSystemPrompt(category, spaceType);

        var response = await _http.PostAsJsonAsync("https://api.anthropic.com/v1/messages", new
        {
            model = "claude-sonnet-5",
            max_tokens = 1024,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = rawInput } }
        });

        var body = await response.Content.ReadFromJsonAsync<AnthropicResponse>();
        var enrichedText = body!.Content[0].Text;
        var variables = ExtractVariables(enrichedText);

        _db.EnrichmentCache.Add(new EnrichmentCacheEntry
        {
            CacheKey = cacheKey,
            EnrichedContent = enrichedText,
            Model = "claude-sonnet-5"
        });
        await _db.SaveChangesAsync();

        return new EnrichmentResult
        {
            EnrichedContent = enrichedText,
            SuggestedVariables = variables,
            Model = "claude-sonnet-5"
        };
    }

    private string BuildSystemPrompt(string? category, string spaceType) => $"""
        Bạn là chuyên gia viết prompt cho không gian '{spaceType}'{(category != null ? $", chủ đề {category}" : "")}.
        Từ ý tưởng ngắn của user, viết lại thành 1 prompt đầy đủ, rõ vai trò AI, ngữ cảnh,
        yêu cầu định dạng, ràng buộc độ dài/tone. Nếu phù hợp, chèn biến động dạng {{ten_bien}}
        để user tự điền khi dùng lại (vd: {{ten_san_pham}}, {{doi_tuong_khach_hang}}).
        Trả lời CHỈ bằng nội dung prompt đã hoàn thiện, không giải thích thêm.
        """;

    private static List<string> ExtractVariables(string text) =>
        Regex.Matches(text, @"\{(\w+)\}").Select(m => m.Groups[1].Value).Distinct().ToList();

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
```

### 2.5. `PromptEnrichmentController.cs`

```csharp
[ApiController]
[Route("api/v1/prompts")]
[Authorize]
public class PromptEnrichmentController : ControllerBase
{
    private readonly IPromptEnrichmentService _enrichment;
    public PromptEnrichmentController(IPromptEnrichmentService enrichment) => _enrichment = enrichment;

    [HttpPost("enrich")]
    public async Task<IActionResult> Enrich([FromBody] EnrichPromptRequest request)
    {
        var result = await _enrichment.EnrichAsync(request.RawInput, request.Category, request.SpaceType);
        return Ok(result); // { enrichedContent, suggestedVariables[], model }
    }
}

public record EnrichPromptRequest(string RawInput, string? Category, string SpaceType);
public class EnrichmentResult
{
    public string EnrichedContent { get; set; } = default!;
    public List<string> SuggestedVariables { get; set; } = new();
    public string Model { get; set; } = default!;
}
```

### 2.6. React Native (Expo) — Màn hình tạo prompt có nút "Bổ sung ngữ cảnh"

```tsx
function CreatePromptScreen() {
  const [rawInput, setRawInput] = useState('');
  const [enriched, setEnriched] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const handleEnrich = async () => {
    setLoading(true);
    try {
      const { data } = await api.post('/prompts/enrich', {
        rawInput,
        category: selectedCategory,
        spaceType: currentSpace.type,
      });
      setEnriched(data.enrichedContent);
    } finally {
      setLoading(false);
    }
  };

  const handleSave = async () => {
    await api.post('/prompts', {
      spaceId: currentSpace.id,
      title: rawInput.slice(0, 50),
      rawInput,                          // lưu bản gốc
      content: enriched ?? rawInput,     // lưu bản đã enrich (nếu có)
      isAiEnriched: enriched !== null,
    });
  };

  return (
    <View>
      <TextInput
        placeholder="Nhập ý tưởng ngắn... vd: viết caption quảng cáo skincare"
        value={rawInput}
        onChangeText={setRawInput}
        multiline
      />
      <Button title="✨ Bổ sung ngữ cảnh (AI)" onPress={handleEnrich} disabled={!rawInput || loading} />

      {loading && <ActivityIndicator />}

      {enriched && (
        <View style={{ marginTop: 12 }}>
          <Text style={{ fontWeight: '600' }}>Prompt đầy đủ (có thể chỉnh sửa):</Text>
          <TextInput
            multiline
            value={enriched}
            onChangeText={setEnriched}   // user vẫn sửa được trước khi lưu
            style={{ borderWidth: 1, padding: 8, minHeight: 150 }}
          />
        </View>
      )}

      <Button title="Lưu prompt" onPress={handleSave} disabled={!rawInput} />
    </View>
  );
}
```

### 2.7. UI Preview dạng so sánh song song (Raw vs Enriched)

Giúp user thấy rõ AI đã thêm gì so với input gốc, dễ quyết định giữ/sửa:

```tsx
function EnrichmentPreview({ rawInput, enrichedContent, suggestedVariables, onAccept, onEdit }: {
  rawInput: string;
  enrichedContent: string;
  suggestedVariables: string[];
  onAccept: () => void;
  onEdit: (text: string) => void;
}) {
  const [editedContent, setEditedContent] = useState(enrichedContent);
  const colors = useTheme();

  return (
    <View style={{ gap: 12 }}>
      <View style={{ backgroundColor: colors.surface, padding: 10, borderRadius: 8 }}>
        <Text style={{ fontSize: 12, color: colors.text, opacity: 0.6 }}>BẠN ĐÃ NHẬP</Text>
        <Text style={{ color: colors.text }}>{rawInput}</Text>
      </View>

      <View style={{ backgroundColor: colors.primary + '15', padding: 10, borderRadius: 8 }}>
        <Text style={{ fontSize: 12, color: colors.primary }}>✨ AI ĐÃ BỔ SUNG</Text>
        <TextInput
          multiline
          value={editedContent}
          onChangeText={(t) => { setEditedContent(t); onEdit(t); }}
          style={{ color: colors.text, minHeight: 120 }}
        />
      </View>

      {suggestedVariables.length > 0 && (
        <View style={{ flexDirection: 'row', gap: 6, flexWrap: 'wrap' }}>
          {suggestedVariables.map((v) => (
            <View key={v} style={{ backgroundColor: colors.border, paddingHorizontal: 8, paddingVertical: 4, borderRadius: 12 }}>
              <Text style={{ fontSize: 12 }}>{`{${v}}`}</Text>
            </View>
          ))}
        </View>
      )}

      <Button title="Dùng bản này" onPress={onAccept} />
    </View>
  );
}
```

---

## 3. Tổng kết cơ chế bổ sung

| Vấn đề | Giải pháp | Lợi ích |
|---|---|---|
| DB đầy vì content dài | Externalize content > 8KB ra Supabase Storage (gzip), DB giữ preview + path | Giảm dung lượng DB, backup nhẹ hơn, storage rẻ hơn |
| Data cũ đã lưu trước khi có threshold | Background job retrofit theo batch 100 dòng/30 phút | Không lock bảng, không quá tải Storage API |
| Lịch sử version phình to | `prompt_versions` luôn externalize | Không cần FTS cho version cũ |
| User nhập prompt sơ sài | Nút "Bổ sung ngữ cảnh" gọi LLM enrich, giữ cả `raw_input` gốc | Prompt chất lượng hơn, tự động gợi ý `{variable}` |
| Gọi LLM tốn phí lặp lại | Cache theo hash(raw_input) ở `enrichment_cache` | Giảm chi phí API, tăng tốc độ trả lời |
| User cần so sánh trước/sau AI | UI preview song song Raw vs Enriched, cho sửa trước khi lưu | Minh bạch, tránh AI tự quyết định thay user |

## 4. Sơ đồ tổng thể luồng xử lý

```
┌────────────────────┐     ngắn      ┌──────────────────────┐
│  User nhập prompt   │──────────────▶│ POST /prompts/enrich  │
└────────────────────┘               └──────────┬────────────┘
                                                  │
                                    check cache (hash raw_input)
                                                  │
                              ┌───────────────────┴───────────────────┐
                              │ Có cache                    Chưa có   │
                              ▼                                       ▼
                     Trả enriched_content                  Gọi LLM API (Claude/GPT)
                     ngay, tăng hit_count                   → lưu cache → trả kết quả
                              │                                       │
                              └───────────────────┬───────────────────┘
                                                   ▼
                                   Client hiển thị Preview (Raw vs Enriched)
                                                   │
                                          User chỉnh sửa (nếu muốn)
                                                   │
                                                   ▼
                                        POST /prompts (Save)
                                                   │
                                    content > 8KB? ──Yes──▶ Upload Supabase Storage (gzip)
                                                   │              DB lưu path + preview
                                                  No
                                                   │
                                                   ▼
                                        Lưu thẳng trong Postgres
```
