using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Orders;
using AgriSage.Domain.Features.Deliveries.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Deliveries;

// FLOW_2 §8 (task F2.6): delivery attempts. The assigned DELIVERY_STAFF member or Operate staff start and complete them;
// only Operate cancels one. Completing an attempt that delivered something is one transaction (the "attempt completer"):
//   Delivery.CompleteAttempt → L1's FulfillmentPostingService (SALE movement from the reserved lots, reservation consumed,
//   order items and status, IFulfillmentFinancialPosting) → the movement linked to the attempt → one SaveChanges.
// A failed attempt posts nothing: no stock movement, no debt; the delivery goes RETRY_PENDING.
public sealed class DeliveryAttemptService(
    IAgriSageDbContext context,
    DeliveryAccess access,
    DeliveryQueries queries,
    DeliveryProofUrls proofUrls,
    FulfillmentPostingService posting,
    IRowLockService locks,
    IDateTimeProvider clock,
    AuditTrail audit) : IDeliveryAttemptService
{
    public async Task<DeliveryAttemptResponse> StartAsync(Guid deliveryId, StartAttemptRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var delivery = await LockAndLoadAsync(actor, deliveryId, cancellationToken);
        var memberId = await access.MemberIdAsync(actor, cancellationToken);

        // No items = everything still undelivered on the delivery.
        var attempted = request.Items is null
            ? delivery.Items.Where(i => !i.IsDeleted).SelectMany(i => i.LotAllocations)
                .Where(a => !a.IsDeleted && a.UndeliveredQuantity > 0)
                .ToDictionary(a => a.Id, a => a.UndeliveredQuantity)
            : request.Items.ToDictionary(i => i.AllocationId, i => i.AttemptedBaseQuantity);

        var attempt = delivery.StartAttempt(memberId, clock.UtcNow, attempted);
        audit.Record("DELIVERY_ATTEMPT_STARTED", "DELIVERY", delivery.Id, delivery.StoreId, null,
            new { attempt.AttemptNumber, items = attempted.Count });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await queries.AttemptsAsync(deliveryId, cancellationToken)).Single(a => a.Id == attempt.Id);
    }

    public async Task<DeliveryResponse> CompleteAsync(
        Guid deliveryId, Guid attemptId, CompleteAttemptRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);
        var now = clock.UtcNow;
        var proof = proofUrls.Accept(request.ProofImageUrl, "proofImageUrl");
        var delivered = (request.Items ?? []).ToDictionary(i => i.AllocationId, i => i.DeliveredBaseQuantity);
        var total = delivered.Values.Sum();
        if (total > 0 && (proof is null || Texts.Clean(request.ReceiverName) is null))
        {
            throw new BusinessRuleException("Goods were handed over: the receiver's name and a proof photo are required.");
        }

        if (total == 0 && Texts.Clean(request.FailureReasonCode) is null)
        {
            throw new BusinessRuleException("Nothing was delivered: give the failure reason.");
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var orderId = await access.Scope(actor).AsNoTracking().Where(d => d.Id == deliveryId).Select(d => (Guid?)d.OrderId)
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Delivery", deliveryId);
        await locks.LockOrderAsync(orderId, cancellationToken);
        var delivery = await LockAndLoadAsync(actor, deliveryId, cancellationToken);
        var attempt = delivery.Attempts.FirstOrDefault(a => a.Id == attemptId && !a.IsDeleted)
            ?? throw new NotFoundException("Delivery attempt", attemptId);

        delivery.CompleteAttempt(attemptId, now, delivered, Texts.Clean(request.ReceiverName), proof,
            Texts.Clean(request.FailureReasonCode)?.ToUpperInvariant(), Texts.Clean(request.Note));

        string? movementNumber = null;
        if (total > 0)
        {
            var allocations = delivery.Items.Where(i => !i.IsDeleted)
                .SelectMany(i => i.LotAllocations.Select(a => (Allocation: a, Item: i)))
                .ToDictionary(x => x.Allocation.Id);
            var order = await context.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderId, cancellationToken);
            var lines = delivered.Where(d => d.Value > 0)
                .Select(d => new FulfillmentLine(allocations[d.Key].Item.OrderItemId, allocations[d.Key].Allocation.InventoryLotId, d.Value))
                .ToList();

            var result = await posting.PostAsync(order, lines, FulfillmentSource.Delivery, delivery.Id, attempt.Id, actor.UserId, now,
                cancellationToken);
            delivery.LinkSaleStockMovement(attempt.Id, result.Movement.Id);
            movementNumber = result.Movement.MovementNumber;
        }

        audit.Record("DELIVERY_ATTEMPT_COMPLETED", "DELIVERY", delivery.Id, delivery.StoreId, null,
            new
            {
                attempt.AttemptNumber, status = EnumText.Format(attempt.Status), delivered = total, movement = movementNumber,
                delivery = EnumText.Format(delivery.Status)
            });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(deliveryId, cancellationToken);
    }

    public async Task<DeliveryResponse> CancelAsync(Guid deliveryId, Guid attemptId, CancelAttemptRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.OperatorAsync(cancellationToken);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var delivery = await LockAndLoadAsync(actor, deliveryId, cancellationToken);
        var attempt = delivery.Attempts.FirstOrDefault(a => a.Id == attemptId && !a.IsDeleted)
            ?? throw new NotFoundException("Delivery attempt", attemptId);

        delivery.CancelAttempt(attemptId);
        audit.Record("DELIVERY_ATTEMPT_CANCELLED", "DELIVERY", delivery.Id, delivery.StoreId, null,
            new { attempt.AttemptNumber }, request.Reason.Trim());
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(deliveryId, cancellationToken);
    }

    public async Task<IReadOnlyList<DeliveryAttemptResponse>> ListAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);
        await access.EnsureVisibleAsync(actor, deliveryId, cancellationToken);

        return await queries.AttemptsAsync(deliveryId, cancellationToken);
    }

    private async Task<Delivery> LockAndLoadAsync(DeliveryAccess.Actor actor, Guid id, CancellationToken cancellationToken)
    {
        await locks.LockDeliveryAsync(id, cancellationToken);

        return await access.Scope(actor)
                .Include(d => d.Items).ThenInclude(i => i.LotAllocations)
                .Include(d => d.Attempts).ThenInclude(a => a.Items)
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException("Delivery", id);
    }
}
