namespace AioKin.Models.InputModel.Vault;

/// <summary>
/// Duy nhat mot duong ghi (create/update/delete) cho Prompt — xem spec muc 6.2.
///
/// Khac ban brief goc: KHONG con truong DeviceId tren request. Thiet bi ghi vao
/// Prompt.UpdatedDeviceId/sync.devices luon phai lay tu CHINH phien dang nhap cua caller
/// (session_token claim -> IAccessTokenService.GetByHashAsync), khong bao gio tin mot truong
/// trong body ma client co the gia mao thanh thiet bi bat ky — carry-forward P16, cung mot
/// cach lam voi BiometricController.Register (P6). Xem SyncController.Push.
/// </summary>
public class SyncPushRequest
{
    public required Guid SpaceUuid { get; set; }
    public required List<PushPromptEntry> Entities { get; set; }
}

public class PushPromptEntry
{
    /// <summary>Sinh boi client, dung nguyen lam id vinh vien — xem Global Constraints cua plan truoc.</summary>
    public required Guid PromptId { get; set; }

    /// <summary>"insert" | "update" | "delete". Gia tri khac se bi tu choi rieng entry nay (per-entry rejection).</summary>
    public required string Operation { get; set; }

    /// <summary>Version client biet luc bat dau sua. Bo qua khi Operation = "insert" (thuong gui 0).</summary>
    public int BaseVersion { get; set; }

    /// <summary>Null khi Operation = "delete".</summary>
    public PromptPayload? Payload { get; set; }
}

public class PromptPayload
{
    /// <summary>
    /// KHONG dung "required" cho Title/Content: entry loi cau truc (thieu/rong/qua dai) phai
    /// bi tu choi RIENG entry do (validate thu cong trong SyncService), khong duoc lam
    /// System.Text.Json nem loi luc bind [FromBody] roi keo sap ca batch xuong 400 (P4-style
    /// ruling ve per-entry rejection).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Ca hai deu tu client sinh khi tao category moi ngay trong luc sua prompt.</summary>
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }

    /// <summary>
    /// Fix round 1, finding 4 (consistency voi G12): "CategoryId omitted/null" gio la GIU
    /// NGUYEN category hien co (giong het ngu nghia cua Tags/Variables = null o tren), khong
    /// con tu dong XOA category nhu truoc. Muon xoa han category, client phai gui tuong minh
    /// ClearCategory = true. Chon co rieng thay vi mot JsonElement?/wrapper type de khong can
    /// custom JsonConverter — don gian, tuong minh, du cho ca 3 truong hop: omit (giu nguyen),
    /// CategoryId co gia tri (gan/tao), ClearCategory=true (xoa).
    /// </summary>
    public bool ClearCategory { get; set; }

    /// <summary>Expo gap G12: null = giu nguyen tag hien co, mang rong tuong minh [] = xoa het.</summary>
    public List<TagRef>? Tags { get; set; }

    /// <summary>Expo gap G12: null = giu nguyen variable hien co, mang rong tuong minh [] = xoa het.</summary>
    public List<PromptVariablePayload>? Variables { get; set; }
}

public class TagRef
{
    public required Guid TagId { get; set; }
    public required string Name { get; set; }
}

public class PromptVariablePayload
{
    public required Guid VariableId { get; set; }
    public required string VarKey { get; set; }
    public string? Label { get; set; }
    public string? DefaultValue { get; set; }
    public string VarType { get; set; } = "text";
}
