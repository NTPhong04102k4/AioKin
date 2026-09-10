using System.Text.Json;
using System.Text.Json.Serialization;
using AioKin.Common.Json;

namespace AioKin.Models.ViewModel.Ability;

/// <summary>
/// Mot rule phan quyen theo dung hinh dang JSON cua CASL, de mot bo rule dung chung duoc cho
/// ca web (CASL that) lan app Android (ban cai lai ngu nghia trong <c>data/ability/</c>).
///
/// THU TU CAC PHAN TU LA NGU NGHIA, KHONG PHAI TRANG TRI: rule dung sau thang rule dung
/// truoc. <c>can(read, X)</c> roi <c>cannot(read, X, {...})</c> nghia la "tat ca tru nhung
/// cai do"; dao hai dong thi luat cam bien mat ma khong bao loi. Vi vay khong tang nao trong
/// duong di duoc phep sap xep lai danh sach nay — doc tu database ra sao thi phat ra y nguyen.
///
/// Khong boc <c>OperationResult</c>: <c>getAbilityRules()</c> ben app khai kieu tra ve la
/// <c>List&lt;AbilityRuleDto&gt;</c> nen Gson cho mot mang JSON tran.
/// </summary>
public class AbilityRuleResponse
{
    /// <summary>Hanh dong, vi du <c>read</c> / <c>update</c>. <c>manage</c> la dai dien cho moi hanh dong.</summary>
    [JsonPropertyName("action")]
    [JsonConverter(typeof(StringOrArrayJsonConverter))]
    public List<string> Action { get; set; } = [];

    /// <summary>Loai doi tuong, vi du <c>DiscoveryItem</c>. <c>all</c> la dai dien cho moi loai.</summary>
    [JsonPropertyName("subject")]
    [JsonConverter(typeof(StringOrArrayJsonConverter))]
    public List<string> Subject { get; set; } = [];

    /// <summary>
    /// Dieu kien kieu MongoDB, so khop voi cac field ma model ben app cho phep
    /// (<c>abilityAttributes()</c>). Bo qua khi rule ap cho moi instance.
    ///
    /// De kieu <see cref="JsonElement"/> de gia tri di qua nguyen ven — toan tu duoc ho tro
    /// la <c>$eq $ne $in $nin $gt $gte $lt $lte</c> cong voi so sanh bang truc tiep; thu khac
    /// (<c>$or</c>, <c>$regex</c>, duong dan co dau cham) ben app tra false, tuc la cam.
    /// </summary>
    [JsonPropertyName("conditions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Conditions { get; set; }

    /// <summary><c>true</c> nghia la luat cam (<c>cannot</c>).</summary>
    [JsonPropertyName("inverted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Inverted { get; set; }

    /// <summary>Cau giai thich khi bi cam; app hien lai cho nguoi dung.</summary>
    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}
