namespace AgriSage.Infrastructure.Persistence.Seed;

// Only these deliberately safe messages may be displayed by the seed command.
public sealed class ReferenceSeedException(string message) : InvalidOperationException(message);
