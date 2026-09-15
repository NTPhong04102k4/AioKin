using AioKin.Data.Entities.Core;

namespace AioKin.Models.ViewModel.Content;

/// <summary>
/// Mot the Kham pha nhu app nhan duoc.
///
/// KHONG boc trong <c>OperationResult</c> nhu cac endpoint khac. <c>ApiService.getDiscoveryItems()</c>
/// khai kieu tra ve la <c>List&lt;DiscoveryItemDto&gt;</c>, tuc Gson mong doi mot mang JSON tran.
/// Boc them mot lop vo la app parse loi ngay. Doi thi phai doi ca hai dau cung luc.
///
/// <c>category</c> va <c>readingMinutes</c> la field that, con DTO ban hien tai van tu suy ra
/// hai gia tri do tu <c>userId</c> va do dai <c>body</c>. Gson bo qua field thua nen phat them
/// khong lam hong gi, va den luc DTO doc field that thi khong phai doi backend nua.
/// </summary>
public class DiscoveryItemResponse
{
    public int Id { get; set; }

    /// <summary>Tac gia. DTO ban hien tai dung so nay de suy ra danh muc — xem DiscoveryItem.AuthorUserID.</summary>
    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>TECHNOLOGY / HEALTH / LIFE.</summary>
    public string Category { get; set; } = string.Empty;

    public int ReadingMinutes { get; set; }

    public static DiscoveryItemResponse From(DiscoveryItem item) => new()
    {
        Id = item.DiscoveryItemID,
        UserId = item.AuthorUserID,
        Title = item.Title,
        Body = item.Body,
        Category = item.Category,
        ReadingMinutes = item.ReadingMinutes
    };
}
