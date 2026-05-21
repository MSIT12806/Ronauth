namespace RonAuth.Domain.Authentication;

public static class LoginFailureCatalog
{
    public const string CaptchaInvalidCode = "A01";
    public const string OtpInvalidCode = "A02";
    public const string AccountErrorCode = "A03";
    public const string AccountDisabledCode = "A04";
    public const string TooManyFailuresCode = "A05";
    public const string VerificationExpiredCode = "A07";
    public const string ExceptionCode = "A08";

    public const int MaxFailedAttempts = 5;
    public const int LockoutMinutes = 15;

    public static string ToDisplayMessage(string code)
    {
        return code switch
        {
            CaptchaInvalidCode => $"[{code}]驗證碼錯誤",
            VerificationExpiredCode => $"[{code}]驗證碼過期",
            OtpInvalidCode => $"[{code}]OTP 驗證碼錯誤",
            _ => $"[{code}]登入失敗",
        };
    }
}