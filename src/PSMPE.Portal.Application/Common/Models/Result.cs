namespace PSMPE.Portal.Application.Common.Models;

public enum ResultErrorType
{
    None = 0,
    NotFound,
    Forbidden,
    Validation
}

public class Result
{
    public bool Succeeded { get; }
    public string? Error { get; }
    public ResultErrorType ErrorType { get; }

    protected Result(bool succeeded, string? error, ResultErrorType errorType)
    {
        Succeeded = succeeded;
        Error = error;
        ErrorType = errorType;
    }

    public static Result Success() => new(true, null, ResultErrorType.None);
    public static Result Forbidden(string error) => new(false, error, ResultErrorType.Forbidden);
    public static Result NotFound(string error) => new(false, error, ResultErrorType.NotFound);
    public static Result Failure(string error) => new(false, error, ResultErrorType.Validation);
}
