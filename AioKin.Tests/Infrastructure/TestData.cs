using AioKin.Common;
using AioKin.Data.Entities.Security;

namespace AioKin.Tests.Infrastructure;

/// <summary>
/// Du lieu mau cho test. Moi ham deu tra ve doi tuong hop le NGAY khi tao — user thieu
/// UserCode thi khong compile, con hai user trung UserCode thi hong o rang buoc unique,
/// va ca hai loi do khong lien quan gi den thu dang duoc kiem tra.
/// </summary>
public static class TestData
{
    /// <summary>
    /// Mot khach hang hop le, moi lan goi mot danh tinh khac. Username va UserCode deu
    /// unique trong database nen ca hai phai ngau nhien.
    /// </summary>
    public static User NewUser()
    {
        var suffix = Guid.NewGuid().ToString("N");

        return new User
        {
            UserCode = $"UC{suffix}"[..20],
            Username = $"u{suffix}"[..20],
            PasswordHash = "x"
        };
    }

    /// <summary>
    /// Mot nhan vien hop le, gan vao mot Role/Location co san. Mat khau khong dung de dang
    /// nhap trong test (token duoc cap thang qua IAccessTokenService) nhung van phai hash
    /// hop le vi PasswordHash/PasswordSalt la truong bat buoc cua Staff.
    /// </summary>
    public static Staff NewStaff(int roleId, int locationId)
    {
        var suffix = Guid.NewGuid().ToString("N");
        PasswordHelper.CreatePasswordHash("Test@12345", out var hash, out var salt);

        return new Staff
        {
            StaffCode = $"STF{suffix}"[..20],
            Username = $"s{suffix}"[..20],
            PasswordHash = hash,
            PasswordSalt = salt,
            FullName = "Test Staff",
            Email = $"{suffix}@test.local",
            LocationID = locationId,
            RoleID = roleId,
            IsActive = true,
            CreatedBy = 0
        };
    }
}
