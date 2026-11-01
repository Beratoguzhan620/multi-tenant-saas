namespace Helpdesk.Core;

/// <summary>The stable error codes returned in problem responses.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string Forbidden = "forbidden";
    public const string InvalidState = "invalid_state";
    public const string EmailTaken = "email_taken";
    public const string SlugTaken = "slug_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string PlanLimitReached = "plan_limit_reached";
    public const string RateLimited = "rate_limited";
}
