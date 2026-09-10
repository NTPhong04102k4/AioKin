using System.Net;

namespace AioKin.Services.Auth.Email;

/// <summary>
/// Noi dung HTML cho email giao dich. Moi gia tri do nguoi dung kiem soat deu di qua
/// <see cref="WebUtility.HtmlEncode(string)"/> — ten hien thi la du lieu nguoi dung nhap, khong
/// duoc phep tro thanh markup trong hom thu cua nguoi khac.
/// </summary>
public static class EmailTemplates
{
    private const string BrandName = "AioKin";

    public static (string Subject, string Html) Otp(string username, string otpCode, int minutes) =>
        ($"[{BrandName}] Ma xac thuc cua ban: {otpCode}",
         Layout(
             "Xac thuc dia chi email",
             $"""
              <p>Xin chao <strong>{E(username)}</strong>,</p>
              <p>Dung ma duoi day de hoan tat dang ky tai khoan {BrandName}:</p>
              {CodeBlock(otpCode)}
              <p>Ma co hieu luc trong <strong>{minutes} phut</strong> va chi dung duoc mot lan.</p>
              <p class="muted">Neu ban khong yeu cau ma nay, hay bo qua email.</p>
              """));

    public static (string Subject, string Html) Welcome(string username) =>
        ($"[{BrandName}] Chao mung ban den voi {BrandName}",
         Layout(
             $"Chao mung, {E(username)}",
             $"""
              <p>Tai khoan cua ban da duoc kich hoat. Ban co the dang nhap ngay bay gio.</p>
              <p class="muted">Neu can ho tro, tra loi truc tiep email nay.</p>
              """));

    public static (string Subject, string Html) PasswordResetOtp(string username, string otpCode, int minutes) =>
        ($"[{BrandName}] Ma dat lai mat khau: {otpCode}",
         Layout(
             "Yeu cau dat lai mat khau",
             $"""
              <p>Xin chao <strong>{E(username)}</strong>,</p>
              <p>Chung toi nhan duoc yeu cau dat lai mat khau cho tai khoan cua ban. Nhap ma sau de tiep tuc:</p>
              {CodeBlock(otpCode)}
              <p>Ma co hieu luc trong <strong>{minutes} phut</strong>.</p>
              <p class="muted">Neu ban khong yeu cau, hay bo qua email nay — mat khau hien tai van giu nguyen.</p>
              """));

    public static (string Subject, string Html) TemporaryPassword(string username, string temporaryPassword, int minutes) =>
        ($"[{BrandName}] Mat khau tam thoi cua ban",
         Layout(
             "Mat khau tam thoi",
             $"""
              <p>Xin chao <strong>{E(username)}</strong>,</p>
              <p>Dung mat khau tam duoi day de dat mat khau moi:</p>
              {CodeBlock(temporaryPassword)}
              <p>Mat khau tam het han sau <strong>{minutes} phut</strong> va chi dung duoc mot lan.</p>
              """));

    public static (string Subject, string Html) PasswordChanged(string username) =>
        ($"[{BrandName}] Mat khau cua ban vua duoc thay doi",
         Layout(
             "Mat khau da duoc thay doi",
             $"""
              <p>Xin chao <strong>{E(username)}</strong>,</p>
              <p>Mat khau tai khoan cua ban vua duoc thay doi thanh cong.</p>
              <p class="muted">Neu khong phai ban thuc hien, hay dat lai mat khau ngay va lien he ho tro.</p>
              """));

    public static (string Subject, string Html) Contact(string fromUserEmail, string subject, string message) =>
        ($"[{BrandName} Contact] {subject}",
         Layout(
             "Lien he tu nguoi dung",
             $"""
              <p><strong>Tu:</strong> {E(fromUserEmail)}</p>
              <p><strong>Tieu de:</strong> {E(subject)}</p>
              <hr />
              <p style="white-space:pre-wrap">{E(message)}</p>
              """));

    private static string CodeBlock(string code) =>
        $"""<p style="font-size:28px;font-weight:700;letter-spacing:6px;margin:24px 0;color:#111">{E(code)}</p>""";

    private static string Layout(string heading, string body) =>
        $$"""
         <!doctype html>
         <html><body style="margin:0;padding:24px;background:#f5f5f4;font-family:system-ui,-apple-system,Segoe UI,sans-serif;color:#1c1917">
           <div style="max-width:520px;margin:0 auto;background:#fff;border-radius:12px;padding:32px">
             <h1 style="margin:0 0 16px;font-size:20px">{{E(heading)}}</h1>
             <style>.muted{color:#78716c;font-size:13px}</style>
             {{body}}
             <p style="margin-top:32px;color:#a8a29e;font-size:12px">{{BrandName}}</p>
           </div>
         </body></html>
         """;

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
