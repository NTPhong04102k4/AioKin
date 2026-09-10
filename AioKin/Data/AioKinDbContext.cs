using AioKin.Data.Entities.Core;
using AioKin.Data.Entities.Security;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Data;

/// <summary>
/// DbContext cho phan Security (auth). Ten bang/cot theo snake_case — cau hinh
/// <c>UseSnakeCaseNamingConvention()</c> trong Program.cs lo phan chuyen doi.
/// </summary>
public class AioKinDbContext(DbContextOptions<AioKinDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Staff> Staffs => Set<Staff>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Location> Locations => Set<Location>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            // UserUUID la id cong khai, moi API tra cuu qua no — phai unique va co index.
            entity.HasIndex(u => u.UserUUID).IsUnique();
            entity.HasIndex(u => u.UserCode).IsUnique();
            entity.HasIndex(u => u.Username).IsUnique();

            // Email/Phone unique nhung cho phep nhieu NULL: tai khoan Facebook co the
            // khong tra email, va tai khoan dang ky bang email thi khong co phone.
            entity.HasIndex(u => u.Email).IsUnique().HasFilter("email IS NOT NULL");
            entity.HasIndex(u => u.Phone).IsUnique().HasFilter("phone IS NOT NULL");

            // Tra cuu khi dang nhap SSO — mot social id chi gan duoc mot tai khoan.
            entity.HasIndex(u => u.IDSocial).IsUnique().HasFilter("id_social IS NOT NULL");

            // FullName do Postgres tinh, khong bao gio ghi tu ung dung. Dung concat_ws
            // de mot ve NULL khong nuot ca chuoi nhu toan tu || se lam.
            entity.Property(u => u.FullName)
                .HasComputedColumnSql("""TRIM(CONCAT_WS(' ', "first_name", "last_name"))""", stored: true);
        });

        modelBuilder.Entity<Staff>(entity =>
        {
            entity.HasIndex(s => s.Username).IsUnique();
            entity.HasIndex(s => s.Email).IsUnique();
            entity.HasIndex(s => s.StaffCode).IsUnique();

            entity.HasOne(s => s.Role)
                .WithMany(r => r.Staffs)
                .HasForeignKey(s => s.RoleID)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(s => s.Location)
                .WithMany()
                .HasForeignKey(s => s.LocationID)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Role>(entity => entity.HasIndex(r => r.RoleName).IsUnique());

        modelBuilder.Entity<Location>(entity => entity.HasIndex(l => l.LocationCode).IsUnique());
    }
}
