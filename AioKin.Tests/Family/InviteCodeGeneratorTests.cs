using AioKin.Services.Family;
using Xunit;

namespace AioKin.Tests.Family;

public class InviteCodeGeneratorTests
{
    [Fact]
    public void Ma_dai_dung_10_ky_tu()
    {
        Assert.Equal(10, InviteCodeGenerator.Next().Length);
    }

    [Fact]
    public void Ma_khong_chua_ky_tu_de_doc_nham()
    {
        // I/1, O/0, U/V doc qua dien thoai hoac chep tu anh chup man hinh la nham. Loai han
        // chung ra re hon nhieu so voi viec ho tro nguoi dung go sai ma.
        var forbidden = new[] { 'I', 'L', 'O', 'U' };

        for (var i = 0; i < 500; i++)
        {
            var code = InviteCodeGenerator.Next();
            Assert.DoesNotContain(code, c => forbidden.Contains(c));
            Assert.All(code, c => Assert.Contains(c, InviteCodeGenerator.Alphabet));
        }
    }

    [Fact]
    public void Sinh_nhieu_ma_thi_khong_trung_nhau()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 5_000; i++)
            Assert.True(codes.Add(InviteCodeGenerator.Next()), "Sinh ra ma trung trong 5000 lan.");
    }
}
