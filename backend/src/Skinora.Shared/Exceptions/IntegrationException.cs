namespace Skinora.Shared.Exceptions;

public class IntegrationException : Exception
{
    public string ServiceName { get; }

    public IntegrationException(string serviceName, string message)
        : base(message)
    {
        ServiceName = serviceName;
    }

    public IntegrationException(string serviceName, string message, Exception innerException)
        : base(message, innerException)
    {
        ServiceName = serviceName;
    }
}
