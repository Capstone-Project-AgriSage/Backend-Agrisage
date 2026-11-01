namespace AgriSage.Domain.Features.Diagnosis.Enums;

// What kind of problem a `diseases` row describes (the table also holds the five AI classes).
public enum ProblemType
{
    Disease,
    Pest,
    Weed,
    Nutrition,
    Growth
}
