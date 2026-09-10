namespace AioKin.Models.InputModel.Auth.User;

/// <summary>
/// Ket qua chung cua mot thao tac nghiep vu. ErrorCode la khoa on dinh cho client
/// va cung la thu ma <see cref="AioKin.Common.OperationResultHttpExtensions"/> dung
/// de chon HTTP status — Message chi de hien thi cho nguoi dung.
/// </summary>
public class OperationResult
{
    public bool Success { get; set; }

    /// <summary>Vi du: "EmailExists", "InvalidCredentials", "NotFound".</summary>
    public string? ErrorCode { get; set; }

    public string? Message { get; set; }

    public object? Data { get; set; }

    public static OperationResult Ok(string? message = null, object? data = null)
        => new() { Success = true, Message = message, Data = data };

    public static OperationResult Fail(string errorCode, string message)
        => new() { Success = false, ErrorCode = errorCode, Message = message };
}
