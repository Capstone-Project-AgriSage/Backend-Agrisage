using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Common.Interfaces;

// Lets Application/Api recognize provider errors without knowing the database provider.
public interface IDatabaseErrorClassifier
{
    bool IsUniqueViolation(DbUpdateException exception);

    // True when the database could not hand out a connection because a connection limit was reached: the server or
    // its pooler refused a new client, or this application's own pool had none left. Retrying a moment later may work.
    bool IsConnectionUnavailable(Exception exception);
}
