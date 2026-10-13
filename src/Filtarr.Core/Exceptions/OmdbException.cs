namespace Filtarr.Core.Exceptions;

/// <summary>OMDb could not be used (bad key, daily limit reached, unreachable or timing out).</summary>
public class OmdbException(string message, Exception? inner = null) : Exception(message, inner);
