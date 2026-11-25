using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Debt.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Debt;

// F3.23: staff can log an outside contact, or explicitly send an SMS through the shared gateway and then log it.
// Neither action changes the debt, limit or credit status. Follow-ups are append-only.
public sealed class DebtFollowUpService(IAgriSageDbContext db, ICurrentUserService currentUser, IDateTimeProvider clock,
    DebtFollowUpQueries queries, ISmsTextSender sms) : IDebtFollowUpService
{
    // A promise to pay is for a date within a year of the contact.
    public const int MaxPromiseDays = 365;

    public async Task<DebtFollowUpResponse> SendSmsAsync(Guid farmerProfileId, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var customer = await db.DebtAccounts.AsNoTracking()
            .Where(a => a.StoreId == store && a.FarmerProfileId == farmerProfileId)
            .Select(a => new { a.CurrentBalance, a.FarmerProfile.User.PhoneNumber })
            .SingleOrDefaultAsync(token) ?? throw new NotFoundException("Customer debt account", farmerProfileId);
        if (customer.CurrentBalance <= 0) throw new BusinessRuleException("The customer owes nothing, so there is no debt to remind.");
        if (!ContactNormalizer.TryNormalizePhone(customer.PhoneNumber, out var phone))
            throw new BusinessRuleException("The customer has no valid phone number for SMS.");
        if (!sms.IsConfigured) throw new MessageDeliveryUnavailableException();

        var amount = customer.CurrentBalance.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        var text = $"AgriSage: Ban con no {amount} VND. Vui long xem cong no va thanh toan, hoac lien he cua hang.";
        await sms.SendAsync(phone!, text, "DebtReminder", token);
        return await RecordAsync(farmerProfileId,
            new DebtFollowUpRequest("MESSAGE", "OTHER", "Đã gửi SMS nhắc nợ qua hệ thống."), token);
    }

    public async Task<DebtFollowUpResponse> RecordAsync(Guid farmerProfileId, DebtFollowUpRequest request, CancellationToken token)
    {
        var actor = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var store = await ActiveStore.GetIdAsync(db, token);
        var account = await db.DebtAccounts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.StoreId == store && a.FarmerProfileId == farmerProfileId, token)
            ?? throw new NotFoundException("Customer debt account", farmerProfileId);
        if (account.CurrentBalance <= 0) throw new BusinessRuleException("The customer owes nothing, so there is no debt to remind.");

        var now = clock.UtcNow;
        var today = BusinessCalendar.Today(now);
        var contactedAt = (request.ContactedAt ?? now).ToUniversalTime();
        if (request.PromisedDate is { } promised)
        {
            var contactDay = BusinessCalendar.Today(contactedAt);
            if (promised < contactDay || promised > contactDay.AddDays(MaxPromiseDays))
                throw new BusinessRuleException($"The promised date must be from the contact day up to {MaxPromiseDays} days after it.");
        }

        var overdue = await db.DebtEntries.AsNoTracking()
            .Where(e => e.DebtAccountId == account.Id && e.OutstandingAmount > 0 && e.DueDate < today)
            .SumAsync(e => (decimal?)e.OutstandingAmount, token) ?? 0m;
        EnumText.TryParse<DebtFollowUpChannel>(request.Channel, out var channel);
        EnumText.TryParse<DebtFollowUpOutcome>(request.Outcome, out var outcome);
        var followUp = account.RecordFollowUp(channel, outcome, request.Note, contactedAt, request.PromisedDate,
            request.PromisedAmount, overdue, actor, now);
        db.DebtFollowUps.Add(followUp);
        await db.SaveChangesAsync(token);

        return await queries.GetAsync(followUp.Id, today, token);
    }

    public async Task<PagedResult<DebtFollowUpResponse>> ListAsync(Guid farmerProfileId, DebtFollowUpListRequest request,
        CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var accountId = await db.DebtAccounts.AsNoTracking()
            .Where(a => a.StoreId == store && a.FarmerProfileId == farmerProfileId)
            .Select(a => (Guid?)a.Id).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("Customer debt account", farmerProfileId);
        return await queries.PageAsync(accountId, request, BusinessCalendar.Today(clock.UtcNow), token);
    }
}
