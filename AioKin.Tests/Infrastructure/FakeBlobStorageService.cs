using AioKin.Services.Common.Storage;

namespace AioKin.Tests.Infrastructure;

/// <summary>
/// Test double cho IBlobStorageService (Task 3, progress.md: "No fake/in-memory
/// IBlobStorageService test double exists — build one so the snapshot path is testable").
/// Dang ky nhu mot singleton trong ApiFixture (thay the hoan toan SupabaseStorageService that,
/// vi Storage:BaseUrl khong duoc cau hinh trong moi truong test) — luu thang trong bo nho thay
/// vi goi Supabase that, va co the mo phong "khong kha dung" qua SimulateUnavailable de test
/// nhanh loi P13 (blob storage loi luc tao snapshot) ma khong can ha Storage that xuong that.
///
/// La MOT singleton dung chung cho ca ApiFixture (chay xuyen suot mot [Collection] test), nen
/// moi test bat SimulateUnavailable = true PHAI tu tra ve false trong finally — tranh lam ro
/// cac test chay SAU trong cung collection (xUnit chay tuan tu cac test CUNG mot collection,
/// khong dong thoi, nen khong co race, nhung state van "ri" qua neu khong tu don).
/// </summary>
public class FakeBlobStorageService : IBlobStorageService
{
    private readonly Dictionary<string, string> _store = new();

    public bool SimulateUnavailable { get; set; }

    /// <summary>
    /// Fix round 2, finding 4: mo phong HttpClient TU TIMEOUT (nem TaskCanceledException) MA
    /// KHONG lien quan gi den cancellationToken cua request — dung y het tinh huong that:
    /// SupabaseStorageService dung HttpClient co Timeout=30s (Program.cs); khi timeout no nem
    /// TaskCanceledException du CHINH request goi PullAsync khong he bi huy. Truoc fix round 2,
    /// catch trong SyncService loc "ex is not OperationCanceledException" — TaskCanceledException
    /// la mot lop con cua OperationCanceledException nen se LOT QUA catch va rot thanh 500 chua
    /// xu ly, dung diem ma finding 4 (round 1) yeu cau phai xuong cap nhe nhang.
    /// </summary>
    public bool SimulateTimeout { get; set; }

    public Task<string> UploadAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        if (SimulateUnavailable)
            throw new InvalidOperationException("Blob storage khong kha dung (mo phong cho test).");

        if (SimulateTimeout)
            // Dung mot CancellationToken RIENG (KHONG PHAI cancellationToken cua tham so) de
            // TaskCanceledException nem ra khong lien quan gi toi token cua request — giong het
            // cach HttpClient.Timeout tu huy request NOI BO cua no, doc lap voi token cua caller.
            throw new TaskCanceledException("Blob storage timeout (mo phong cho test).", null, new CancellationToken(true));

        _store[path] = content;
        return Task.FromResult(path);
    }

    public Task<string> DownloadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (SimulateUnavailable)
            throw new InvalidOperationException("Blob storage khong kha dung (mo phong cho test).");

        return _store.TryGetValue(path, out var content)
            ? Task.FromResult(content)
            : throw new FileNotFoundException($"Khong tim thay '{path}' trong FakeBlobStorageService.");
    }
}
