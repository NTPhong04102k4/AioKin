using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Data.Entities.Core;

/// <summary>
/// Mot muc tren man Lich trinh. Du lieu rieng cua tung nguoi dung — moi truy van deu phai
/// loc theo chu so huu, khong bao gio tra ca bang.
///
/// Ten entity theo domain ben app va theo chuoi <c>subject</c> trong rule CASL
/// (<see cref="SubjectType"/>); route thi giu <c>/todos</c> cho khop ApiService ben Android.
/// </summary>
[Table("schedule_items", Schema = "core")]
public class ScheduleItem
{
    /// <summary>Chuoi <c>subject</c> trong rule phan quyen. Phai khop hang so cung ten ben app.</summary>
    public const string SubjectType = "ScheduleItem";

    [Key]
    public int ScheduleItemID { get; set; }

    /// <summary>
    /// Chu so huu, tro toi khoa chinh cua bang users.
    ///
    /// KHONG phat gia tri nay ra JSON: id noi bo la Guid, trong khi <c>userId</c> ben DTO la
    /// so nguyen — khong nhet vua. Ma cung khong can: endpoint da gioi han theo token roi,
    /// lap lai id cua chinh nguoi goi tren tung dong chi la nhieu. Ban DTO hien tai co doc
    /// <c>userId</c> nhung khong dung no vao viec gi.
    /// </summary>
    public Guid UserID { get; set; }

    [ForeignKey(nameof(UserID))]
    public UserDb? User { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    /// <summary>Co the rong — adapter ben app tu an dong dia diem khi khong co.</summary>
    [MaxLength(200)]
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Thoi diem bat dau. Luu <c>DateTime</c> co timezone trong Postgres, phat ra JSON duoi
    /// dang epoch millis vi domain model ben app giu <c>startAtMillis: Long</c> — chuoi da
    /// format phu thuoc Locale cua may nen la viec cua tang UI.
    /// </summary>
    public DateTime StartAt { get; set; }

    public bool IsDone { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
