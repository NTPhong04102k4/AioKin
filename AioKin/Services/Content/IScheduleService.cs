using AioKin.Models.ViewModel.Content;

namespace AioKin.Services.Content;

/// <summary>
/// Lich trinh cua tung nguoi dung. Moi phuong thuc deu nhan <c>userUuid</c> lay tu token,
/// khong bao gio tu tham so client gui len — nhan id tu client nghia la doi mot con so la
/// doc duoc lich cua nguoi khac.
/// </summary>
public interface IScheduleService
{
    /// <summary>Lich cua mot nguoi dung, sap theo thoi diem bat dau tang dan.</summary>
    Task<IReadOnlyList<ScheduleItemResponse>> GetForUserAsync(Guid userUuid, CancellationToken cancellationToken = default);
}
