namespace AgriSage.Application.Common.Interfaces;

public enum PasswordVerification
{
    Failed,
    Success,
    SuccessRehashNeeded
}

// Password hashing implementation lives in Infrastructure; Application never sees the algorithm.
public interface IPasswordHashService
{
    string Hash(string password);

    // A null hash (unknown account) is verified against a dummy hash so the response time does not reveal it.
    PasswordVerification Verify(string? passwordHash, string password);
}
