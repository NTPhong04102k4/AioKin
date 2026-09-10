using System.Text.Json;
using AioKin.Data;
using AioKin.Models.ViewModel.Ability;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Auth.Permissions;

public class PermissionService : IPermissionService
{
    private readonly AioKinDbContext _db;
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(AioKinDbContext db, ILogger<PermissionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AbilityRuleResponse>> GetRulesForRoleAsync(
        string roleName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return [];

        var json = await _db.Roles
            .AsNoTracking()
            .Where(r => r.RoleName == roleName && r.IsActive)
            .Select(r => r.Permissions)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            // Deserialize roi serialize lai van giu nguyen thu tu: mang JSON vao List<T> ra
            // dung thu tu cu. Di qua kieu tuong minh thay vi day thang chuoi tho ra ngoai de
            // JSON hong bi chan ngay tai day, va de Swagger co schema that de mo ta.
            var rules = JsonSerializer.Deserialize<List<AbilityRuleResponse>>(json);
            return rules is null ? [] : rules;
        }
        catch (JsonException ex)
        {
            // Cam khi nghi ngo. Phat mot bo rule doc dang phan nua con nguy hiem hon la khong
            // phat gi: no co the vo tinh mo quyen chu khong chi dong bot.
            _logger.LogError(ex,
                "roles.permissions cua role {RoleName} khong phai JSON hop le — tra ve rong (cam tat ca).",
                roleName);
            return [];
        }
    }
}
