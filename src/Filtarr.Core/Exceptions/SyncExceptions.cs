namespace Filtarr.Core.Exceptions;

public class SyncInProgressException() : InvalidOperationException("A sync is already running.");

/// <summary>A single filter can't be run; <see cref="StatusCode"/> is the HTTP status to answer with.</summary>
public class RuleRunException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
