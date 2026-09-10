using AioKin.Common;
using AioKin.Models.InputModel.Auth.Admin;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Services.Auth.StaffManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Admin;

/// <summary>Quan ly ho so nhan vien (khong bao gom mat khau — xem /auth/admin).</summary>
[ApiController]
[Route("admin/staff")]
[Produces("application/json")]
[Authorize(Roles = $"{Roles.ADMIN},{Roles.SUPERADMIN}")]
public class StaffManagementController : ControllerBase
{
    private readonly IStaffManagementService _service;

    public StaffManagementController(IStaffManagementService service)
    {
        _service = service;
    }

    /// <summary>Danh sach nhan vien, co phan trang va loc.</summary>
    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? roleId = null,
        [FromQuery] int? locationId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? keyword = null)
    {
        var result = await _service.GetPagedAsync(page, pageSize, roleId, locationId, isActive, keyword);
        return Ok(OperationResult.Ok(data: result));
    }

    /// <summary>Chi tiet mot nhan vien.</summary>
    [HttpGet("{staffId:int}")]
    public async Task<IActionResult> GetById(int staffId)
    {
        var staff = await _service.GetByIdAsync(staffId);

        return staff is null
            ? this.ToActionResult(OperationResult.Fail("NotFound", "Khong tim thay nhan vien."))
            : Ok(OperationResult.Ok(data: staff));
    }

    /// <summary>Cap nhat thong tin nhan vien. Chi SuperAdmin (co the doi role).</summary>
    [HttpPut("{staffId:int}")]
    [Authorize(Roles = Roles.SUPERADMIN)]
    public async Task<IActionResult> Update(int staffId, [FromBody] StaffUpdateRequest model)
        => this.ToActionResult(await _service.UpdateAsync(staffId, model));

    /// <summary>Bat/tat trang thai hoat dong. Tat se thu hoi moi phien cua nhan vien do.</summary>
    [HttpPatch("{staffId:int}/status")]
    [Authorize(Roles = Roles.SUPERADMIN)]
    public async Task<IActionResult> ChangeStatus(int staffId, [FromBody] StaffStatusRequest model)
        => this.ToActionResult(await _service.ChangeStatusAsync(staffId, model, User.GetUsername() ?? "unknown"));

    /// <summary>Vo hieu hoa nhan vien (xoa mem).</summary>
    [HttpDelete("{staffId:int}")]
    [Authorize(Roles = Roles.SUPERADMIN)]
    public async Task<IActionResult> Delete(int staffId)
        => this.ToActionResult(await _service.DeleteAsync(staffId, User.GetUsername() ?? "unknown"));
}
