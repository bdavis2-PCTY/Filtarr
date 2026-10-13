namespace Filtarr.Core.Exceptions;

/// <summary>Sonarr refused a request; the message is Sonarr's own explanation.</summary>
public class SonarrException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
