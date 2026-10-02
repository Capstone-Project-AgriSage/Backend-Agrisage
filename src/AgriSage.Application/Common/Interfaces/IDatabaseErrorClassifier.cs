using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Common.Interfaces;

// Lets Application/Api recognize provider errors without knowing the database provider.
public interface IDatabaseErrorClassifier
{
    bool IsUniqueViolation(DbUpdateException exception);
}
