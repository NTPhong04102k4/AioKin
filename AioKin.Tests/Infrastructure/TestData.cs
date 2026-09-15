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
}
