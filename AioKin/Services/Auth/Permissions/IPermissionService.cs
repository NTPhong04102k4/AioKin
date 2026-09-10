using AioKin.Models.ViewModel.Ability;

namespace AioKin.Services.Auth.Permissions;

/// <summary>
/// Bo rule phan quyen phat cho client, doc tu cot <c>roles.permissions</c>.
///
/// Mot bo rule dung chung cho ca web lan app: web chay CASL that, app chay ban cai lai ngu
/// nghia trong <c>data/ability/</c>. Vi vay backend la noi duy nhat quyet dinh ai lam duoc gi.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Rule cua mot role, giu nguyen thu tu da luu — rule dung sau thang rule dung truoc.
    /// Role khong ton tai hoac JSON hong thi tra danh sach rong, tuc cam tat ca: khong co
    /// rule nao khop thi ben app mac dinh la tu choi.
    /// </summary>
    Task<IReadOnlyList<AbilityRuleResponse>> GetRulesForRoleAsync(
        string roleName,
        CancellationToken cancellationToken = default);
}
