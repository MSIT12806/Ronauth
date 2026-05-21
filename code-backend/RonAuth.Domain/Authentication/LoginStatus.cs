namespace RonAuth.Domain.Authentication;

public enum LoginStatus
{
    Success,
    Failed,
    RequiresSecondFactor,
    LockedOut,
}