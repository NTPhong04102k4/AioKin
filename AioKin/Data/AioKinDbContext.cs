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
    public DbSet<DiscoveryItem> DiscoveryItems => Set<DiscoveryItem>();
    public DbSet<ScheduleItem> ScheduleItems => Set<ScheduleItem>();

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

            // FullName do Postgres tinh, khong bao gio ghi tu ung dung.
            //
            // Bieu thuc phai IMMUTABLE thi Postgres moi cho lam cot GENERATED. CONCAT_WS
            // va CONCAT chi la STABLE (chung phu thuoc ham xuat cua kieu du lieu), nen
            // dung chung se bi tu choi voi loi 42P17. COALESCE + toan tu || deu immutable:
            // COALESCE lo phan mot ve NULL khong nuot ca chuoi, NULLIF tra ve NULL thay vi
            // chuoi rong khi ca hai ve deu trong.
            entity.Property(u => u.FullName)
                .HasComputedColumnSql(
                    """NULLIF(TRIM(COALESCE("first_name", '') || ' ' || COALESCE("last_name", '')), '')""",
                    stored: true);
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

        modelBuilder.Entity<DiscoveryItem>(entity =>
        {
            // Truy van duy nhat cua man Kham pha la "bai da publish, moi nhat truoc" — index
            // phu dung thu tu do de Postgres khong phai sort lai ca bang sau moi lan loc.
            entity.HasIndex(d => new { d.IsPublished, d.CreatedDate });

            // Danh muc duoc loc o tang UI, nhung khi so bai lon len thi bo loc se chuyen
            // xuong duoi nay; co san index thi luc do khong phai them migration.
            entity.HasIndex(d => d.Category);
        });

        modelBuilder.Entity<ScheduleItem>(entity =>
        {
            // Moi truy van deu la "lich cua mot nguoi, sap theo gio bat dau" — index ghep
            // theo dung cap do phuc vu ca phan loc lan phan sap xep trong mot lan quet.
            entity.HasIndex(s => new { s.UserID, s.StartAt });

            // Cascade chu khong Restrict: lich trinh khong ton tai doc lap voi chu cua no.
            // Restrict o day nghia la xoa mot tai khoan se that bai voi loi khoa ngoai, con
            // du lieu rieng tu cua nguoi da roi di thi nam lai trong bang.
            entity.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserID)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
