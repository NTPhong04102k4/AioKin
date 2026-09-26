namespace AioKin.Services.Common.Storage;

/// <summary>
/// Upload/download noi dung dang text (da gzip ca hai chieu) len Supabase Storage. Dung truoc
/// tien cho snapshot cua sync_catchup (Task 3, xem SyncService.BuildSnapshotFallbackAsync) —
/// tinh nang externalize noi dung prompt > 8KB (spec, phan hoan lai) se dung lai chinh service
/// nay, khong tao ban thu hai.
///
/// Dang ky OPTIONAL trong Program.cs (chi khi co Storage:BaseUrl) — noi goi (SyncService) nhan
/// tham so nullable va tu xu ly truong hop chua cau hinh (P13: khong duoc 500, phai tra loi ro
/// rang), giong het cach IEmailService/BrevoEmailService xuong cap khi thieu Brevo API key.
/// </summary>
public interface IBlobStorageService
{
    Task<string> UploadAsync(string path, string content, CancellationToken cancellationToken = default);
    Task<string> DownloadAsync(string path, CancellationToken cancellationToken = default);
}
