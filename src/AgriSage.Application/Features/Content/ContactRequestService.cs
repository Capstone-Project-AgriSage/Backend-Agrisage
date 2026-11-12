using System.Linq.Expressions;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Content.Entities;
using AgriSage.Domain.Features.Content.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Content;

public sealed class ContactRequestService(IAgriSageDbContext db, ICurrentUserService actor,
    IDateTimeProvider clock, IDatabaseErrorClassifier databaseErrors) : IContactRequestService
{
    public async Task<ContactRequestResponse> CreateAsync(ContactRequestCreate request, CancellationToken token)
    {
        var number = await DocumentNumbers.NextAsync(db.ContactRequests.IgnoreQueryFilters().AsNoTracking().Select(r => r.RequestNumber),
            "CR", BusinessCalendar.Today(clock.UtcNow), token);
        var contact = new ContactRequest(number, request.ContactName.Trim(), request.Subject.Trim(), request.Message.Trim(),
            actor.IsAuthenticated ? actor.UserId : null, Texts.Clean(request.ContactPhone), ContactNormalizer.NormalizeEmail(request.ContactEmail));
        db.ContactRequests.Add(contact);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException ex) when (databaseErrors.IsUniqueViolation(ex))
        { throw new ConflictException("Yêu cầu khác vừa được tạo. Vui lòng gửi lại."); }
        return await GetAsync(contact.Id, false, token);
    }

    public async Task<PagedResult<ContactRequestResponse>> ListAsync(ContactRequestList request, bool mine, CancellationToken token)
    {
        var query = Scope(mine);
        if (EnumText.TryParse<ContactRequestStatus>(request.Status, out var status)) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(r => r.RequestNumber.ToLower().Contains(term) || r.ContactName.ToLower().Contains(term) || r.Subject.ToLower().Contains(term));
        }
        var total = await query.LongCountAsync(token);
        var items = await query.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).Skip(request.Skip).Take(request.PageSize).Select(Projection).ToListAsync(token);
        return new(items, request.Page, request.PageSize, total);
    }

    public async Task<ContactRequestResponse> GetAsync(Guid id, bool mine, CancellationToken token) =>
        await Scope(mine).Where(r => r.Id == id).Select(Projection).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("Contact request", id);

    public async Task<ContactRequestResponse> UpdateAsync(Guid id, ContactRequestUpdate request, CancellationToken token)
    {
        var contact = await db.ContactRequests.SingleOrDefaultAsync(r => r.Id == id, token) ?? throw new NotFoundException("Contact request", id);
        if (!EnumText.TryParse<ContactRequestStatus>(request.Status, out var status)) throw new BusinessRuleException("Invalid contact status.");
        if (status == ContactRequestStatus.Resolved)
        {
            if (string.IsNullOrWhiteSpace(request.ResolutionNote)) throw new BusinessRuleException("Vui lòng nhập phản hồi trước khi hoàn thành.");
            contact.Resolve(clock.UtcNow, request.ResolutionNote.Trim());
        }
        else contact.UpdateHandling(status, Texts.Clean(request.ResolutionNote));
        await db.SaveChangesAsync(token);
        return await GetAsync(id, false, token);
    }

    private IQueryable<ContactRequest> Scope(bool mine)
    {
        var query = db.ContactRequests.AsNoTracking();
        if (!mine) return query;
        var id = actor.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        return query.Where(r => r.UserId == id);
    }
    private static readonly Expression<Func<ContactRequest, ContactRequestResponse>> Projection = r => new(
        r.Id, r.RequestNumber, r.ContactName, r.ContactPhone, r.ContactEmail, r.Subject, r.Message,
        EnumText.Format(r.Status), r.ResolvedAt, r.ResolutionNote, r.CreatedAt, r.UpdatedAt);
}
