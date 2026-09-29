---
name: review-backend-change
description: Review an AgriSage backend diff or pull request for architecture, business invariants, EF/database safety, security, and test coverage.
---

# Review Backend Change

Use for PR/diff/code review.

## Review Order

1. Understand task intent and affected feature.
2. Inspect the diff and relevant existing code.
3. Architecture:
   - layer dependency;
   - Controller thinness;
   - Application orchestration;
   - Domain independence;
   - external abstraction boundaries.
4. Business invariants:
   - pricing/credit;
   - inventory movement;
   - debt ledger;
   - fulfillment;
   - returns;
   - AI Human Review.
5. Persistence:
   - FK/delete behavior;
   - concurrency;
   - soft delete;
   - indexes/constraints;
   - migration safety.
6. Security:
   - identity/ownership;
   - secrets;
   - webhook verification/idempotency.
7. Quality:
   - async/CancellationToken;
   - projection/AsNoTracking;
   - exception handling;
   - logging.
8. Tests:
   - Domain unit tests;
   - integration tests for transactional/persistence risks.
9. Report findings by severity and cite files/lines when possible.

Do not approve based only on compilation if core business invariants are untested or violated.
