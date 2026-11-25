using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Debt;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;

namespace AgriSage.UnitTests.Domain.Features.Debt;

// F3.23 (FLOW_3 §8j): a reminder made outside the system, logged afterwards. Only a record: these rules keep it consistent.
public class DebtFollowUpTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 25);

    // An account owing 3 000 000.
    private static DebtAccount Owing()
    {
        var account = new DebtAccount(Guid.NewGuid(), Guid.NewGuid());
        account.CreateCreditSaleEntry("DE-1", DebtEntrySourceType.Pickup, Guid.NewGuid(), Guid.NewGuid(), 3_000_000m, 0,
            Today.AddDays(-20), Actor, Now.AddDays(-50));
        return account;
    }

    private static DebtFollowUp Record(DebtAccount account, DebtFollowUpChannel channel = DebtFollowUpChannel.PhoneCall,
        DebtFollowUpOutcome outcome = DebtFollowUpOutcome.NoAnswer, string? note = null, DateTimeOffset? contactedAt = null,
        DateOnly? promisedDate = null, decimal? promisedAmount = null, decimal overdue = 3_000_000m) =>
        account.RecordFollowUp(channel, outcome, note, contactedAt ?? Now, promisedDate, promisedAmount, overdue, Actor, Now);

    [Fact]
    public void A_reminder_keeps_the_balance_and_changes_nothing_on_the_account()
    {
        var account = Owing();
        var followUp = Record(account, outcome: DebtFollowUpOutcome.Promised, note: "  Hẹn bán lúa xong trả  ",
            promisedDate: Today.AddDays(5), promisedAmount: 1_000_000m, overdue: 2_000_000m);

        Assert.Equal(account.Id, followUp.DebtAccountId);
        Assert.Equal(3_000_000m, followUp.OutstandingAmount);
        Assert.Equal(2_000_000m, followUp.OverdueAmount);
        Assert.Equal("Hẹn bán lúa xong trả", followUp.Note);
        Assert.Equal(Actor, followUp.CreatedBy);
        Assert.Equal(3_000_000m, account.CurrentBalance);
        Assert.Equal(DebtAccountStatus.Active, account.Status);
    }

    [Fact]
    public void A_promise_needs_its_date_and_only_goes_with_PROMISED()
    {
        var account = Owing();
        Assert.Throws<DomainException>(() => Record(account, outcome: DebtFollowUpOutcome.Promised));
        Assert.Throws<DomainException>(() => Record(account, outcome: DebtFollowUpOutcome.NoAnswer, promisedDate: Today));
        Assert.Throws<DomainException>(() => Record(account, outcome: DebtFollowUpOutcome.Refused, promisedAmount: 100m));
        Assert.Throws<DomainException>(() => Record(account, outcome: DebtFollowUpOutcome.Promised, promisedDate: Today, promisedAmount: 0m));
        Assert.Throws<DomainException>(() => Record(account, outcome: DebtFollowUpOutcome.Promised, promisedDate: Today, promisedAmount: 3_000_000.01m));
        Assert.Throws<DomainException>(() => Record(account, outcome: DebtFollowUpOutcome.Promised, promisedDate: Today, promisedAmount: 10.005m));
        Assert.Null(Record(account, outcome: DebtFollowUpOutcome.Promised, promisedDate: Today).PromisedAmount);
        Assert.Equal(3_000_000m, Record(account, outcome: DebtFollowUpOutcome.Promised, promisedDate: Today, promisedAmount: 3_000_000m).PromisedAmount);
    }

    [Fact]
    public void Other_needs_a_note_and_a_note_has_at_most_1000_characters()
    {
        var account = Owing();
        Assert.Throws<DomainException>(() => Record(account, channel: DebtFollowUpChannel.Other));
        Assert.Throws<DomainException>(() => Record(account, outcome: DebtFollowUpOutcome.Other, note: "   "));
        Assert.Equal("Nhờ con gái nhắn", Record(account, channel: DebtFollowUpChannel.Other, note: "Nhờ con gái nhắn").Note);
        Assert.Throws<DomainException>(() => Record(account, note: new string('x', 1001)));
        Assert.Null(Record(account, note: "").Note);
    }

    [Fact]
    public void The_contact_can_be_logged_up_to_30_days_later_but_never_ahead()
    {
        var account = Owing();
        Assert.Equal(Now.AddDays(-30), Record(account, contactedAt: Now.AddDays(-30)).ContactedAt);
        Assert.Throws<DomainException>(() => Record(account, contactedAt: Now.AddDays(-30).AddMinutes(-1)));
        Assert.Equal(Now.AddMinutes(4), Record(account, contactedAt: Now.AddMinutes(4)).ContactedAt); // client clock slightly ahead
        Assert.Throws<DomainException>(() => Record(account, contactedAt: Now.AddMinutes(6)));
    }

    [Fact]
    public void Nothing_owed_means_nothing_to_remind()
    {
        Assert.Throws<DomainException>(() => new DebtAccount(Guid.NewGuid(), Guid.NewGuid())
            .RecordFollowUp(DebtFollowUpChannel.PhoneCall, DebtFollowUpOutcome.NoAnswer, null, Now, null, null, 0m, Actor, Now));
        Assert.Throws<DomainException>(() => Record(Owing(), overdue: 3_000_000.01m));
        Assert.Throws<DomainException>(() => Record(Owing(), overdue: -1m));
    }

    [Theory]
    // promisedDate offset, promisedAmount, paid since contact, current balance -> status
    [InlineData(3, null, 0, 3_000_000, DebtPromiseStatus.Waiting)]
    [InlineData(0, null, 0, 3_000_000, DebtPromiseStatus.Waiting)]           // the promised day itself is not yet late
    [InlineData(-1, null, 0, 3_000_000, DebtPromiseStatus.Broken)]
    [InlineData(-1, null, 1_999_999, 1_000_001, DebtPromiseStatus.Broken)]  // short of the overdue amount (target 2 000 000)
    [InlineData(-1, null, 2_000_000, 1_000_000, DebtPromiseStatus.Kept)]     // the overdue amount at the time was paid
    [InlineData(-1, 500_000, 500_000, 2_500_000, DebtPromiseStatus.Kept)]    // the promised amount was paid
    [InlineData(5, 500_000, 600_000, 2_400_000, DebtPromiseStatus.Kept)]     // kept before the date
    [InlineData(-10, 2_000_000, 100_000, 0, DebtPromiseStatus.Kept)]         // owes nothing any more (e.g. a return)
    public void The_promise_label_follows_the_payments_since_the_contact(int dateOffset, int? amount, int paid, int balance,
        DebtPromiseStatus expected)
    {
        var status = DebtFollowUpPolicy.PromiseStatus(Today.AddDays(dateOffset), amount, 3_000_000m, 2_000_000m, paid, balance, Today);
        Assert.Equal(expected, status);
    }

    [Fact]
    public void Without_a_promise_there_is_no_label_and_without_overdue_the_whole_balance_is_the_target()
    {
        Assert.Null(DebtFollowUpPolicy.PromiseStatus(null, null, 3_000_000m, 0m, 0m, 3_000_000m, Today));
        Assert.Equal(3_000_000m, DebtFollowUpPolicy.PromiseTarget(null, 3_000_000m, 0m));
        Assert.Equal(2_000_000m, DebtFollowUpPolicy.PromiseTarget(null, 3_000_000m, 2_000_000m));
        Assert.Equal(700_000m, DebtFollowUpPolicy.PromiseTarget(700_000m, 3_000_000m, 2_000_000m));
    }

    [Fact]
    public void The_request_validator_mirrors_the_rules()
    {
        var validator = new DebtFollowUpRequestValidator();
        Assert.True(validator.Validate(new DebtFollowUpRequest("PHONE_CALL", "NO_ANSWER")).IsValid);
        Assert.True(validator.Validate(new DebtFollowUpRequest("visit", "promised", PromisedDate: Today, PromisedAmount: 500_000m)).IsValid);
        Assert.False(validator.Validate(new DebtFollowUpRequest("FAX", "NO_ANSWER")).IsValid);
        Assert.False(validator.Validate(new DebtFollowUpRequest("PHONE_CALL", "MAYBE")).IsValid);
        Assert.False(validator.Validate(new DebtFollowUpRequest("PHONE_CALL", "PROMISED")).IsValid);
        Assert.False(validator.Validate(new DebtFollowUpRequest("PHONE_CALL", "NO_ANSWER", PromisedDate: Today)).IsValid);
        Assert.False(validator.Validate(new DebtFollowUpRequest("PHONE_CALL", "REFUSED", PromisedAmount: 1m)).IsValid);
        Assert.False(validator.Validate(new DebtFollowUpRequest("PHONE_CALL", "PROMISED", PromisedDate: Today, PromisedAmount: -1m)).IsValid);
        Assert.False(validator.Validate(new DebtFollowUpRequest("OTHER", "NO_ANSWER")).IsValid);
        Assert.True(validator.Validate(new DebtFollowUpRequest("OTHER", "NO_ANSWER", "Gửi thư tay")).IsValid);
        Assert.False(validator.Validate(new DebtFollowUpRequest("PHONE_CALL", "NO_ANSWER", new string('x', 1001))).IsValid);
    }
}
