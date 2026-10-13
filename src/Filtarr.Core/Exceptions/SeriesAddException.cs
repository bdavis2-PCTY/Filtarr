namespace Filtarr.Core.Exceptions;

/// <summary>Why a series could not be added; <see cref="StatusCode"/> is the HTTP status the API should answer with.</summary>
public class SeriesAddException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
