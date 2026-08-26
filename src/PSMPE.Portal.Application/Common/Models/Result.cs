namespace PSMPE.Portal.Application.Common.Models;

public enum ResultErrorType
{
    None = 0,
    NotFound,
    Forbidden,
    Validation,
    /// <summary>Maps to 409. Distinct from Validation because "already in use" is a race the caller
    /// resolves by choosing another value, not by fixing malformed input.</summary>
    Conflict
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
    public static Result Conflict(string error) => new(false, error, ResultErrorType.Conflict);
}

public class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool succeeded, T? value, string? error, ResultErrorType errorType) : base(succeeded, error, errorType) =>
        Value = value;

    public static Result<T> Success(T value) => new(true, value, null, ResultErrorType.None);
    public static new Result<T> Forbidden(string error) => new(false, default, error, ResultErrorType.Forbidden);
    public static new Result<T> NotFound(string error) => new(false, default, error, ResultErrorType.NotFound);
    public static new Result<T> Failure(string error) => new(false, default, error, ResultErrorType.Validation);
    public static new Result<T> Conflict(string error) => new(false, default, error, ResultErrorType.Conflict);
}
