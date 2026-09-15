using AioKin.Models.ViewModel.Content;

namespace AioKin.Services.Content;

/// <summary>Noi dung man Kham pha. Chi doc — chua co duong ghi nao trong hop dong API cua app.</summary>
public interface IDiscoveryService
{
    /// <summary>Danh sach the da publish, moi nhat truoc.</summary>
    Task<IReadOnlyList<DiscoveryItemResponse>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Mot the theo id. Null khi khong ton tai hoac chua publish.</summary>
    Task<DiscoveryItemResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}
