namespace AgriSage.Application.Common.Exceptions;

public sealed class MessageDeliveryUnavailableException : Exception
{
    public MessageDeliveryUnavailableException() : base("Verification message delivery is unavailable. Please try again later.") { }
}
