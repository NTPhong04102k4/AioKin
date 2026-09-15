using AioKin.Data.Entities.Core;

namespace AioKin.Models.ViewModel.Content;

/// <summary>
/// Mot muc Lich trinh nhu app nhan duoc. Cung ly do voi
/// <see cref="DiscoveryItemResponse"/>: mang JSON tran, khong boc <c>OperationResult</c>.
///
/// Ten field la <c>completed</c> chu khong phai <c>isDone</c> — do la ten trong
/// <c>ScheduleItemDto</c> ben app, va Gson khop theo ten.
///
/// <c>startAtMillis</c> va <c>location</c> la field that. DTO ban hien tai bo qua ca hai va
/// tu suy ra gio bat dau tu <c>id</c>, nen lich hien ra deu la gio bia; khi DTO doc hai field
/// nay thi lich moi dung. Phat truoc de khong phai doi backend luc do.
/// </summary>
public class ScheduleItemResponse
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Ten theo DTO ben app, khong phai IsDone.</summary>
    public bool Completed { get; set; }

    /// <summary>Epoch millis UTC — domain model ben app giu Long, khong phai chuoi da format.</summary>
    public long StartAtMillis { get; set; }

    public string Location { get; set; } = string.Empty;

    public static ScheduleItemResponse From(ScheduleItem item) => new()
    {
        Id = item.ScheduleItemID,
        Title = item.Title,
        Completed = item.IsDone,
        // DateTime trong Postgres la timestamptz; ep ve UTC truoc khi doi sang epoch, neu
        // khong thi mot gia tri Unspecified se bi coi la gio may chu va lech mui gio.
        StartAtMillis = new DateTimeOffset(DateTime.SpecifyKind(item.StartAt, DateTimeKind.Utc))
            .ToUnixTimeMilliseconds(),
        Location = item.Location
    };
}
