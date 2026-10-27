"""Generate the standalone PlantUML sources and a distributable documentation bundle."""

from pathlib import Path
import json
import re
import zipfile


ROOT = Path(__file__).resolve().parent
SOURCES = ROOT / "puml"

STYLE = """!pragma layout elk
left to right direction
hide empty description
skinparam backgroundColor White
skinparam shadowing false
skinparam linetype ortho
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam ArrowColor #555555
skinparam ArrowThickness 1
skinparam ArrowFontName Arial
skinparam ArrowFontSize 10
skinparam nodesep 80
skinparam ranksep 120
skinparam state {
    BackgroundColor White
    BorderColor #555555
    BorderThickness 1
    FontColor #111111
    FontStyle plain
    RoundCorner 14
    StartColor Black
    EndColor Black
}
"""

# Raw strings preserve PlantUML's intentional \n line breaks in transition labels.
DIAGRAMS = [
    ("01-user-account", "User Account", r"""
[*] -right-> ACTIVE : System / Create account
ACTIVE -right-> INACTIVE : Administrator / Deactivate account
INACTIVE --> ACTIVE : Administrator / Reactivate account
ACTIVE -right-> SUSPENDED : Administrator / Suspend account
SUSPENDED --> ACTIVE : Administrator / Lift suspension
ACTIVE -right-> LOCKED : Administrator / Lock account
LOCKED --> ACTIVE : Administrator / Unlock account
""", "Administrative transitions shown are representative. Application authorization still applies. Status changes invalidate existing sessions."),
    ("02-auth-session", "Authentication Session", r"""
[*] -right-> VALID : User / Log in successfully
VALID --> VALID : User / Use or refresh valid session
VALID -right-> EXPIRED : System / Session expiry reached
VALID -right-> REVOKED : User or Administrator / Log out or revoke session
VALID -right-> INVALIDATED : System / Security version changed
EXPIRED -right-> [*]
REVOKED -right-> [*]
INVALIDATED -right-> [*]
""", "These are derived session conditions, not a database status enum. Logging in again creates a new session."),
    ("03-cart", "Shopping Cart", r"""
[*] -right-> ACTIVE : Farmer / Create cart
ACTIVE --> ACTIVE : Farmer / Add, update, or remove products
ACTIVE --> ACTIVE : System / Reject invalid checkout
ACTIVE -right-> CONVERTED : Farmer / Complete checkout\nSystem / Create order atomically
ACTIVE -right-> ABANDONED : System / Mark cart abandoned
CONVERTED -right-> [*]
ABANDONED -right-> [*]
""", "A converted or abandoned cart is not reactivated; further shopping uses a new active cart."),
    ("04-order", "Sales Order", r"""
state "Order Fulfillment in Progress" as FULFILLMENT {
    state CONFIRMED
    state PREPARING
    state READY_FOR_FULFILLMENT
    state PARTIALLY_FULFILLED
    CONFIRMED -right-> PREPARING : Staff / Start preparation
    CONFIRMED --> READY_FOR_FULFILLMENT : Staff / Mark ready
    PREPARING -right-> READY_FOR_FULFILLMENT : Staff / Finish preparation
    CONFIRMED --> PARTIALLY_FULFILLED : System / Record partial fulfillment
    PREPARING --> PARTIALLY_FULFILLED : System / Record partial fulfillment
    READY_FOR_FULFILLMENT -right-> PARTIALLY_FULFILLED : System / Record partial fulfillment
    PARTIALLY_FULFILLED --> PARTIALLY_FULFILLED : System / Record more fulfillment\n[remainder still open]
}
[*] -right-> PENDING_CONFIRMATION : Farmer or Staff / Create order
PENDING_CONFIRMATION --> PENDING_CONFIRMATION : Farmer or Staff / Update pending order
PENDING_CONFIRMATION -right-> CONFIRMED : Staff / Confirm order\nSystem / Reserve stock and credit if required
PENDING_CONFIRMATION -down-> CANCELLED : Farmer or Staff / Cancel pending order
FULFILLMENT -right-> COMPLETED : System / Close fulfillment\n[all quantities fulfilled, none cancelled]
FULFILLMENT -right-> PARTIALLY_CANCELLED : System / Close fulfillment\n[some fulfilled, remaining quantities cancelled]
FULFILLMENT --> CANCELLED : Staff / Cancel remaining quantities\n[nothing fulfilled and no open remainder]
COMPLETED -right-> [*]
PARTIALLY_CANCELLED -right-> [*]
CANCELLED -right-> [*]
""", "FULFILLMENT is a visual group, not a stored status. Farmer cancellation is allowed only while PENDING_CONFIRMATION. Payments and refunds remain separate lifecycles."),
    ("05-payment", "Payment", r"""
[*] -right-> PENDING : Farmer / Sales Staff creates payOS link\nSales Staff records cash or bank transfer
PENDING -right-> PAID : payOS webhook / status synchronization\nSales Staff / Store Owner receives cash\nStore Owner confirms bank transfer
PENDING -down-> FAILED : payOS [link expired or failed]\nStore Owner rejects bank transfer
PENDING -down-> CANCELLED : Farmer cancels payOS link\npayOS [link cancelled]\nSales Staff / Store Owner cancels bank transfer\nSystem [order cancelled]
PAID -right-> PARTIALLY_REFUNDED : System / Record completed refund\n[0 < total refunded < payment amount]
PAID -up-> REFUNDED : System / Record completed refund\n[total refunded = payment amount]
PARTIALLY_REFUNDED -right-> REFUNDED : System / Record completed refund\n[total refunded = payment amount]
PARTIALLY_REFUNDED --> PARTIALLY_REFUNDED : System / Record additional completed refund\n[total refunded < payment amount]
REFUNDED -right-> [*]
FAILED -right-> [*]
CANCELLED -right-> [*]
""", "Expired payOS links map to FAILED in the current PayOsPaymentService. Invalid or duplicate webhooks do not create a second receipt of money. Retrying a failed or cancelled payment creates a new Payment."),
    ("06-delivery", "Delivery", r"""
state "Delivery Not Yet Closed" as OPEN_DELIVERY {
    state DRAFT
    state ASSIGNED
    state OUT_FOR_DELIVERY
    state PARTIALLY_DELIVERED
    state RETRY_PENDING
    DRAFT -right-> ASSIGNED : Staff / Assign delivery staff
    ASSIGNED -right-> OUT_FOR_DELIVERY : Delivery Staff / Dispatch delivery
    OUT_FOR_DELIVERY -right-> PARTIALLY_DELIVERED : System / Complete attempt\n[some delivered, remainder still open]
    OUT_FOR_DELIVERY -down-> RETRY_PENDING : System / Complete attempt\n[attempt delivered nothing, remainder still open]
    PARTIALLY_DELIVERED --> OUT_FOR_DELIVERY : Delivery Staff / Dispatch remaining goods
    RETRY_PENDING --> OUT_FOR_DELIVERY : Delivery Staff / Retry delivery
}
[*] -right-> DRAFT : Staff / Create delivery
OUT_FOR_DELIVERY -right-> DELIVERED : System / Complete attempt\n[nothing remains to deliver]
OPEN_DELIVERY --> CANCELLED : Staff / Cancel remainder\n[nothing ever delivered, no attempt in progress]
OPEN_DELIVERY --> DELIVERED : Staff / Cancel remainder\n[some goods already delivered, no attempt in progress]
DELIVERED -right-> [*]
CANCELLED -right-> [*]
""", "OPEN_DELIVERY is a visual group. Delivery has no PARTIALLY_CANCELLED status: after partial delivery, cancelling the remainder ends the Delivery as DELIVERED; cancelled quantities stay on its items."),
    ("07-delivery-attempt", "Delivery Attempt", r"""
[*] -right-> IN_PROGRESS : Delivery Staff / Start attempt
IN_PROGRESS -right-> SUCCESS : Delivery Staff / Complete attempt\n[all attempted quantities delivered]
IN_PROGRESS -right-> PARTIAL_SUCCESS : Delivery Staff / Complete attempt\n[some attempted quantities delivered]
IN_PROGRESS -right-> FAILED : Delivery Staff / Complete attempt\n[no goods delivered]
IN_PROGRESS -right-> CANCELLED : Delivery Staff / Cancel attempt
SUCCESS -right-> [*]
PARTIAL_SUCCESS -right-> [*]
FAILED -right-> [*]
CANCELLED -right-> [*]
""", "Only one attempt may be IN_PROGRESS for a Delivery at a time. Another attempt is a new object."),
    ("08-delivery-incident", "Delivery Incident", r"""
[*] -right-> OPEN : Staff / Report incident
OPEN --> OPEN : Staff / Add incident details
OPEN -right-> RESOLVED : Authorized Staff / Resolve incident\nand record resolution
RESOLVED -right-> [*]
""", "Inventory consequences, when applicable, require an appropriate posted stock movement."),
    ("09-goods-receipt", "Goods Receipt", r"""
[*] -right-> DRAFT : Staff / Create goods receipt
DRAFT --> DRAFT : Staff / Update receipt and items
DRAFT -right-> CONFIRMED : Staff / Confirm receipt\nSystem / Post stock-in and update weighted average cost
DRAFT -down-> CANCELLED : Staff / Cancel draft receipt
CONFIRMED -right-> [*]
CANCELLED -right-> [*]
""", "Only draft receipts are editable or cancellable. Confirmed receipt corrections require a separate adjustment or reversal operation."),
    ("10-inventory-lot", "Inventory Lot", r"""
[*] -right-> ACTIVE : System / Create inventory lot
ACTIVE -right-> QUARANTINED : Staff / Quarantine lot
QUARANTINED --> ACTIVE : Staff / Release quarantine\n[lot remains eligible]
ACTIVE -right-> BLOCKED : Staff / Block lot
BLOCKED --> ACTIVE : Staff / Unblock lot\n[lot remains eligible]
ACTIVE -right-> EXPIRED : System / Mark expired\n[expiry date is before business date]
ACTIVE -right-> DEPLETED : System / Record depleted status
DEPLETED --> ACTIVE : System / Restore eligibility\n[stock available and lot remains eligible]
""", "Representative business transitions. DEPLETED is a declared status, not a claim that current stock-posting code changes status automatically at zero stock. Actual expiry date is checked independently; expired, quarantined, blocked or depleted lots cannot be sold/reserved/allocated."),
    ("11-stock-movement", "Stock Movement", r"""
[*] -right-> DRAFT : System or Staff / Create stock movement
DRAFT --> DRAFT : Staff / Update draft movement
DRAFT -right-> POSTED : Owning use case / Post movement\nand update inventory
DRAFT -down-> CANCELLED : Staff / Cancel draft movement
POSTED -right-> REVERSED : Owning use case / Post corresponding reversal movement
CANCELLED -right-> [*]
REVERSED -right-> [*]
""", "STOCK_IN, SALE, RETURN_IN and ADJUSTMENT are movement types, not lifecycle states. POSTED remains eligible for reversal, so it is not connected directly to final."),
    ("12-stocktake", "Stocktake", r"""
[*] -right-> DRAFT : Staff / Create stocktake
DRAFT -right-> IN_PROGRESS : Staff / Start stocktake
DRAFT -down-> CANCELLED : Staff / Cancel draft stocktake
IN_PROGRESS --> IN_PROGRESS : Staff / Record physical count
IN_PROGRESS --> IN_PROGRESS : Staff / Refresh stale snapshot and recount
IN_PROGRESS -right-> COMPLETED : Staff / Complete stocktake\n[all items counted and validated]
IN_PROGRESS --> CANCELLED : Staff / Cancel stocktake
COMPLETED -right-> [*]
CANCELLED -right-> [*]
""", "Completion requires all active items to be counted; stale lines must be handled according to the snapshot rules."),
    ("13-inventory-reservation", "Inventory Reservation", r"""
[*] -right-> ACTIVE : System / Reserve inventory for order
ACTIVE --> ACTIVE : System / Release part\n[nothing consumed, remainder still reserved]
ACTIVE -right-> PARTIALLY_CONSUMED : Fulfillment / Consume part\n[remaining quantity > 0]
ACTIVE -right-> CONSUMED : Fulfillment / Consume all remaining quantity
ACTIVE -down-> RELEASED : System / Release all\n[nothing ever consumed]
ACTIVE -down-> CANCELLED : System / Cancel order\n[nothing ever consumed]
PARTIALLY_CONSUMED --> PARTIALLY_CONSUMED : System / Consume or release more\n[remaining quantity > 0]
PARTIALLY_CONSUMED -right-> CONSUMED : System / Consume or release remainder\n[remaining quantity = 0]
CONSUMED -right-> [*]
RELEASED -right-> [*]
CANCELLED -right-> [*]
""", "Statuses are derived from reserved, consumed and released quantities. After any consumption, release the unused remainder instead of cancelling the reservation."),
    ("14-credit-reservation", "Credit Reservation", r"""
[*] -right-> ACTIVE : System / Reserve credit for order
ACTIVE --> ACTIVE : System / Release part\n[nothing consumed, remainder still reserved]
ACTIVE -right-> PARTIALLY_CONSUMED : Fulfillment / Consume part\n[remaining amount > 0]
ACTIVE -right-> CONSUMED : Fulfillment / Consume all remaining amount
ACTIVE -down-> RELEASED : System / Release all\n[nothing ever consumed]
ACTIVE -down-> CANCELLED : System / Cancel order\n[nothing ever consumed]
PARTIALLY_CONSUMED --> PARTIALLY_CONSUMED : System / Consume or release more\n[remaining amount > 0]
PARTIALLY_CONSUMED -right-> CONSUMED : System / Consume or release remainder\n[remaining amount = 0]
CONSUMED -right-> [*]
RELEASED -right-> [*]
CANCELLED -right-> [*]
""", "CONSUMED means some amount was consumed and no reservation remains; unused amounts may have been released. Credit exposure includes receivables plus active remaining reservations."),
    ("15-farmer-credit-profile", "Farmer Credit Profile", r"""
[*] -right-> ACTIVE : Staff / Create credit profile
ACTIVE -right-> SUSPENDED : Authorized Manager / Suspend credit access
SUSPENDED --> ACTIVE : Authorized Manager / Restore credit access
ACTIVE -right-> BLOCKED : Authorized Manager / Block credit access
SUSPENDED --> BLOCKED : Authorized Manager / Block credit access
BLOCKED --> ACTIVE : Authorized Manager / Unblock credit access
BLOCKED --> SUSPENDED : Authorized Manager / Change to suspended
""", "Available credit = credit limit - outstanding receivables - remaining reserved credit. ACTIVE alone is insufficient: customer, debt account and other eligibility rules also apply."),
    ("16-debt-account", "Debt Account", r"""
[*] -right-> ACTIVE : System / Create debt account
ACTIVE -right-> BLOCKED : Authorized Staff / Block account
BLOCKED --> ACTIVE : Authorized Staff / Unblock account
ACTIVE -right-> CLOSED : Authorized Staff / Close account\n[business conditions satisfied]
BLOCKED --> CLOSED : Authorized Staff / Close account\n[business conditions satisfied]
CLOSED --> ACTIVE : Authorized Staff / Reopen account\n[permitted by application policy]
""", "Administrative status changes are shown. The Domain status setter does not make CLOSED irreversible; authorization and business restrictions belong to the use case."),
    ("17-debt-entry", "Debt Entry", r"""
state "Debt Entry Under Processing" as OPEN_DEBT {
    state OPEN
    state PARTIALLY_PAID
    state ADJUSTED
    state DISPUTED
    OPEN -right-> PARTIALLY_PAID : System / Apply partial payment
    PARTIALLY_PAID --> PARTIALLY_PAID : System / Apply additional payment\n[balance remains]
    OPEN --> DISPUTED : Staff / DISPUTE
    PARTIALLY_PAID --> DISPUTED : Staff / DISPUTE
    ADJUSTED --> DISPUTED : Staff / DISPUTE
    DISPUTED --> DISPUTED : System / Apply payment\n[dispute remains unresolved]
    OPEN --> ADJUSTED : Manager / ADJUST\n[positive balance remains]
    PARTIALLY_PAID --> ADJUSTED : Manager / ADJUST\n[positive balance remains]
    DISPUTED --> ADJUSTED : Manager / ADJUST\n[positive balance remains]
    ADJUSTED --> ADJUSTED : Manager / ADJUST\n[positive balance remains]
    ADJUSTED --> PARTIALLY_PAID : System / Apply payment\n[positive balance remains]
    DISPUTED --> OPEN : Staff / KEEP\n[outstanding = original amount]
    DISPUTED --> PARTIALLY_PAID : Staff / KEEP\n[0 < outstanding < original amount]
}
[*] -right-> OPEN : Fulfillment or adjustment use case / Record receivable
OPEN -right-> PAID : System / Apply payment\n[outstanding becomes zero]
PARTIALLY_PAID -right-> PAID : System / Apply payment\n[outstanding becomes zero]
ADJUSTED -right-> PAID : System / Apply payment\n[outstanding becomes zero]
OPEN_DEBT --> PAID : Manager / ADJUST\n[outstanding becomes zero]
DISPUTED --> PAID : Staff / KEEP\n[outstanding = 0]
OPEN_DEBT --> CANCELLED : Manager / CANCEL\n[post reduction of full outstanding balance]
PAID -right-> [*]
CANCELLED -right-> [*]
""", "OPEN_DEBT is a visual group. A disputed entry may receive payments and stays DISPUTED until resolved. Overdue is derived from due date and balance, not an enum state. Receivable changes require DebtTransactions."),
    ("18-sales-return", "Sales Return", r"""
[*] -right-> REQUESTED : Farmer or Staff / Submit return request
REQUESTED --> REQUESTED : Staff / Update return items
REQUESTED -right-> APPROVED : Staff / Approve request
REQUESTED -down-> REJECTED : Staff / Reject request
REQUESTED -down-> CANCELLED : Staff / Cancel request
APPROVED -right-> RECEIVED : Staff / Receive returned goods
APPROVED --> CANCELLED : Staff / Cancel before goods received
RECEIVED --> RECEIVED : Staff / Inspect individual items
RECEIVED -right-> INSPECTED : Staff / Complete all inspections\nSystem / Determine debt reduction and refund
INSPECTED -right-> PARTIALLY_RESOLVED : System / Record partial resolution
INSPECTED --> COMPLETED : Return use case / Complete all required resolution
PARTIALLY_RESOLVED --> PARTIALLY_RESOLVED : System / Continue restocking or settlement
PARTIALLY_RESOLVED -right-> COMPLETED : Return use case / Complete all required resolution
REJECTED -right-> [*]
CANCELLED -right-> [*]
COMPLETED -right-> [*]
""", "Only resellable goods may be restocked. Reduce attributable unpaid debt first, then refund the remaining eligible value. Only REQUESTED return items are editable."),
    ("19-refund", "Refund", r"""
[*] -right-> PENDING : Staff / Create refund request
PENDING --> PENDING : Staff / Update processing details
PENDING -right-> COMPLETED : Staff / Confirm successful refund
PENDING -down-> FAILED : Staff / Record refund failure
PENDING -down-> CANCELLED : Staff / Cancel refund request
COMPLETED -right-> [*]
FAILED -right-> [*]
CANCELLED -right-> [*]
""", "Applies to sales-return and cancelled-order refunds. A failed refund is retried with a new Refund, never by reopening the failed object."),
    ("20-diagnosis-case", "AI Diagnosis Case", r"""
state "Human Review Not Yet Completed" as BEFORE_REVIEW {
    state SUBMITTED
    state PROCESSING
    state AI_COMPLETED
    state UNDER_REVIEW
    state FAILED
    SUBMITTED -right-> PROCESSING : System / Start AI analysis\n[at least one active image]
    PROCESSING -right-> AI_COMPLETED : System / Complete AI analysis\n[at least one successful inference]
    PROCESSING -down-> FAILED : System / Record AI failure
    FAILED --> PROCESSING : System / Retry AI analysis
    AI_COMPLETED -right-> UNDER_REVIEW : Authorized Reviewer / Start review
}
[*] -right-> SUBMITTED : Farmer / Submit diagnosis request
AI_COMPLETED --> VERIFIED : Authorized Reviewer / CONFIRMED or CORRECTED
UNDER_REVIEW -right-> VERIFIED : Authorized Reviewer / CONFIRMED or CORRECTED
FAILED --> VERIFIED : Authorized Reviewer / Manual diagnosis\n[CORRECTED]
AI_COMPLETED --> INCONCLUSIVE : Authorized Reviewer / INCONCLUSIVE
UNDER_REVIEW --> INCONCLUSIVE : Authorized Reviewer / INCONCLUSIVE
FAILED --> INCONCLUSIVE : Authorized Reviewer / INCONCLUSIVE
VERIFIED --> VERIFIED : Authorized Reviewer / Review again\n[definitive diagnosis]
VERIFIED --> INCONCLUSIVE : Authorized Reviewer / Review again\n[insufficient evidence]
INCONCLUSIVE --> VERIFIED : Authorized Reviewer / Review again\n[definitive diagnosis]
INCONCLUSIVE --> INCONCLUSIVE : Authorized Reviewer / Review again\n[still insufficient evidence]
VERIFIED --> VERIFIED : Authorized Reviewer / Approve recommendations
BEFORE_REVIEW -down-> CANCELLED : Farmer or Staff / Cancel before review completion
CANCELLED -right-> [*]
""", "Review requires can_review_ai. VERIFIED and INCONCLUSIVE allow re-review and are not final states. A new review supersedes the previous review and deactivates its recommendations. Recommendations require VERIFIED; Healthy cases receive treatment guidance only, no product recommendation."),
    ("21-ai-model", "AI Model", r"""
[*] -right-> DRAFT : Authorized Staff / Register model version
DRAFT -right-> ACTIVE : Authorized Staff / Activate deployment
ACTIVE -right-> RETIRED : Authorized Staff / Retire model version
RETIRED -right-> [*]
""", "A retired model version is not reactivated by the current Domain lifecycle."),
    ("22-ai-policy-config", "AI Policy Config", r"""
[*] -right-> DRAFT : Authorized Staff / Create policy
DRAFT -right-> ACTIVE : Authorized Staff / Activate policy
ACTIVE -right-> INACTIVE : Authorized Staff / Deactivate policy
INACTIVE --> ACTIVE : Authorized Staff / Reactivate policy
""", "Inference requires an active policy of the selected active model, effective at inference time. Policies never bypass mandatory Human Review for recommendations."),
    ("23-product", "Product", r"""
[*] -right-> ACTIVE : Staff / Create active product
ACTIVE -right-> INACTIVE : Staff / Temporarily stop selling
INACTIVE --> ACTIVE : Staff / Resume selling
ACTIVE -right-> DISCONTINUED : Staff / Discontinue product
INACTIVE --> DISCONTINUED : Staff / Discontinue product
DISCONTINUED --> ACTIVE : Staff / Restore as active\n[permitted]
DISCONTINUED --> INACTIVE : Staff / Restore as inactive\n[permitted]
""", "Representative administrative transitions. Product, StoreProduct, packaging, prices and eligible inventory jointly determine sellability. DISCONTINUED is not enforced as an irreversible terminal state by the Domain setter."),
    ("24-price-list", "Price List", r"""
[*] -right-> DRAFT : Staff / Create price list
DRAFT -right-> ACTIVE : Staff / Activate price list
ACTIVE -right-> INACTIVE : Staff / Deactivate price list
INACTIVE --> ACTIVE : Staff / Reactivate price list
""", "Actual price applicability also requires valid effective dates and customer-group assignments."),
    ("25-article", "Article", r"""
[*] -right-> DRAFT : Staff / Create article
DRAFT -right-> PUBLISHED : Staff / Publish article
PUBLISHED --> DRAFT : Staff / Move to draft
DRAFT --> ARCHIVED : Staff / Archive article
PUBLISHED -right-> ARCHIVED : Staff / Archive article
ARCHIVED --> DRAFT : Staff / Restore as draft
ARCHIVED --> PUBLISHED : Staff / Publish again
""", "The current design allows moving articles to draft, publishing and archiving without imposing an irreversible final state."),
    ("26-contact-request", "Contact Request", r"""
[*] -right-> OPEN : User / Submit contact request
OPEN -right-> IN_PROGRESS : Staff / Start processing
OPEN --> RESOLVED : Staff / Resolve directly
OPEN --> CLOSED : Staff / Close request
IN_PROGRESS -right-> RESOLVED : Staff / Complete resolution
IN_PROGRESS --> CLOSED : Staff / Close request
RESOLVED -right-> CLOSED : Staff / Close after resolution
CLOSED --> OPEN : Staff / Reopen if permitted
""", "This is the usual handling flow, not an exhaustive transition restriction. The design does not impose a fixed state graph; resolving records resolved_at."),
    ("27-notification", "Notification", r"""
[*] -right-> UNREAD : System / Create notification
UNREAD -right-> READ : User / Read notification
UNREAD --> ARCHIVED : User / Archive notification
READ -right-> ARCHIVED : User / Archive notification
ARCHIVED -right-> [*]
""", "Reading records read_at. Archiving is a semantic lifecycle operation, not physical deletion."),
    ("28-notification-outbox", "Notification Outbox", r"""
[*] -right-> WAITING : System / Write event to outbox
WAITING -right-> PROCESSING : Worker / Pick up event\n[available time reached]
PROCESSING -right-> PROCESSED : Worker / Complete processing
PROCESSING -down-> RETRY_WAIT : Worker / Record failure\nand increment attempt count
RETRY_WAIT --> PROCESSING : Worker / Retry\n[next available time reached]
PROCESSED -right-> [*]
""", "Operational conditions derived from ProcessedAt, AvailableAt and attempt count, not a stored status enum. Retries use increasing delays."),
    ("29-payment-allocation", "Payment Allocation", r"""
[*] -right-> ACTIVE : System / Allocate funds\n[payment is PAID]
ACTIVE --> ACTIVE : Owning use case / Consume or release prepayment\n[operation is valid]
ACTIVE -right-> REVERSED : Owning use case / Reverse allocation\n[reversal conditions satisfied]
REVERSED -right-> [*]
""", "An ORDER allocation cannot be reversed once prepayment has been consumed. ORDER_PAYMENT permits ORDER allocations; DEBT_REPAYMENT permits DEBT allocations."),
    ("30-debt-transaction", "Debt Transaction", r"""
[*] -right-> POSTED : Owning use case / Post receivable transaction
POSTED -right-> REVERSED : Owning use case / Reverse transaction\n[corresponding reversal posting recorded]
REVERSED -right-> [*]
""", "Posted financial history is preserved; reversal is a semantic operation with corresponding ledger effects, never physical deletion."),
]


def main():
    SOURCES.mkdir(parents=True, exist_ok=True)
    documents = []
    manifest = []
    for key, title, body, explanation in DIAGRAMS:
        # ELK places labels on orthogonal edges more reliably than Graphviz.
        # The global layout sets direction; avoid competing per-edge directions.
        body = re.sub(r"-(?:right|down|up)->", "-->", body)
        source = f"@startuml {key}\n" + STYLE + f"\ntitle {title}\n\n" + body.strip() + "\n@enduml\n"
        assert source.count("@startuml") == source.count("@enduml") == 1
        assert source.count("{") == source.count("}")
        assert not re.search(r"(?m)^\[\*\].*\[\*\]", source)
        assert not any(token in source for token in ("<br/>", "stateDiagram-v2", "flowchart LR"))
        path = SOURCES / f"{key}.puml"
        path.write_text(source, encoding="utf-8")
        documents.append(source)
        manifest.append({"id": key, "title": title, "source": f"puml/{path.name}", "notes": explanation})

    combined = ROOT / "AGRISAGE_ALL_STATES.puml"
    combined.write_text("\n".join(documents), encoding="utf-8")
    markdown = [
        "# AgriSage - PlantUML State Diagrams\n",
        "30 standalone diagrams corresponding to the state diagrams discussed in the chat. "
        "Inventory/Credit Reservations and Payment Allocation/Debt Transaction are separate diagrams.\n",
        "Each source includes its own styling: left-to-right layout, rounded white states, thin grey borders, "
        "orthogonal connectors, no shadow and no empty description compartment. "
        "PlantUML controls exact routing; the result is not guaranteed to match a manually placed drawing pixel for pixel.\n",
        "Each terminating lifecycle uses one shared final pseudostate within its diagram. "
        "Reactivatable administrative objects intentionally have no final node. "
        "Composite containers are visual groups, not additional database statuses.\n",
        "The session and outbox diagrams show derived operational conditions, not stored status enums. "
        "Administrative diagrams show representative transitions where the design does not impose a complete graph.\n",
        "Payment correction: current PayOsPaymentService maps EXPIRED/FAILED provider links to FAILED, "
        "and CANCELLED links to CANCELLED. This follows the reference image and current code; "
        "the earlier chat description of expiration as CANCELLED was inaccurate.\n",
    ]
    for entry, source in zip(manifest, documents):
        markdown.extend([
            f"## {entry['id']} - {entry['title']}\n",
            f"Source: [{entry['source']}]({entry['source']})\n",
            entry["notes"] + "\n",
            "```plantuml\n" + source.rstrip() + "\n```\n",
        ])
    (ROOT / "AGRISAGE_ALL_STATES.md").write_text("\n".join(markdown), encoding="utf-8")
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    readme = """# AgriSage PlantUML State Diagrams

- `puml/`: 30 independent PlantUML files; each can be copied directly into a PlantUML editor.
- `AGRISAGE_ALL_STATES.puml`: all 30 complete `@startuml` / `@enduml` blocks in one file.
- `AGRISAGE_ALL_STATES.md`: all code blocks, explanations and modelling notes.
- `manifest.json`: diagram index.
- `svg/`, when present: locally rendered previews.

Use one standalone file in editors supporting one diagram at a time. PlantUML CLI can render
the combined file as multiple diagrams. No external includes are required.

Rendering uses `hide empty description`, rounded states, thin borders, no shadows,
left-to-right layout and `skinparam linetype ortho` for right-angle connectors.
Exact placement and label routing remain layout-engine decisions.

Long-lived or reactivatable objects have no artificial final node. Every terminating
diagram uses one shared final pseudostate. Grouping containers are not persisted states.

Business references: `docs/reference/DATABASE_DESIGN.md` section 35 and the Domain entities.
The operational outbox/session conditions and representative administrative transitions
are distinguished from stored status enums in the full Markdown document.

Payment provider mapping: EXPIRED/FAILED -> FAILED; CANCELLED -> CANCELLED, as implemented
in `Application/Features/Payments/PayOsPaymentService.cs`.

Regenerate sources and package with `python build_diagrams.py`.
"""
    (ROOT / "README.md").write_text(readme, encoding="utf-8")
    archive = ROOT / "AGRISAGE_PLANTUML_STATES.zip"
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(ROOT.rglob("*")):
            if path.is_file() and path.suffix in {".puml", ".md", ".json", ".svg", ".py"}:
                bundle.write(path, path.relative_to(ROOT).as_posix())
    print(json.dumps({"diagrams": len(DIAGRAMS), "sources": str(SOURCES), "combined": str(combined), "archive": str(archive)}))


if __name__ == "__main__":
    main()
