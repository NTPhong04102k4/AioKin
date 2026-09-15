using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Core;

/// <summary>
/// Mot the noi dung tren man Kham pha cua app.
///
/// Ten entity la DiscoveryItem chu khong phai Post vi day la ten ma tang domain ben app
/// dung, va cung la chuoi <c>subject</c> trong rule CASL (<see cref="SubjectType"/>).
/// Route thi van la <c>/posts</c> — do la duong dan ApiService ben Android dang goi, doi
/// route la hong app. Lech ten giua route va entity la co y.
/// </summary>
[Table("discovery_items", Schema = "core")]
public class DiscoveryItem
{
    /// <summary>Chuoi <c>subject</c> trong rule phan quyen. Phai khop hang so cung ten ben app.</summary>
    public const string SubjectType = "DiscoveryItem";

    [Key]
    public int DiscoveryItemID { get; set; }

    /// <summary>
    /// Tac gia. Phat ra ngoai duoi ten <c>userId</c> vi DTO ben app doc field do.
    ///
    /// CANH BAO: ban DTO hien tai suy ra danh muc bang <c>userId % 3</c> — mot cach lam tam
    /// tu thoi con goi jsonplaceholder. Nen du lieu seed duoc chon sao cho
    /// <see cref="Category"/> trung dung voi ket qua suy ra do; sua AuthorUserID ma khong
    /// sua Category thi app se hien sai danh muc trong khi API van tra dung.
    /// </summary>
    public int AuthorUserID { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    public required string Body { get; set; }

    /// <summary>
    /// TECHNOLOGY / HEALTH / LIFE — khop enum <c>DiscoveryCategory</c> ben app. Luu chuoi
    /// chu khong luu so: rule CASL so sanh <c>category</c> voi mot chuoi JSON, va enum ben
    /// Kotlin cung phat ra dang <c>.name</c>.
    /// </summary>
    [MaxLength(20)]
    public required string Category { get; set; }

    /// <summary>So phut doc uoc tinh. App dang tu tinh tu do dai body; field nay de thay the.</summary>
    public int ReadingMinutes { get; set; }

    /// <summary>Bai chua publish khong xuat hien trong danh sach lan chi tiet.</summary>
    public bool IsPublished { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
