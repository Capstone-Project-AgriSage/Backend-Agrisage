# Dynamic permissions — user-requested implementation, 2026-10-09

Supersedes the earlier deferral of dynamic permissions. Primary roles remain ADMIN, STORE_OWNER, SALES_STAFF,
FARMER and the existing DELIVERY_STAFF role. Identity/role promotion, store membership, own-Farmer and assigned-driver
checks remain business invariants and cannot be bypassed by granting a permission.

## Persistence and effective permissions

Add `permissions`, `role_permissions`, `store_member_permissions`; see DATABASE_DESIGN.md §36. Existing roles and
store_members gain optimistic-concurrency `version`. Catalog codes identify a module and operation and are seeded
from the reviewed API inventory; arbitrary client-defined codes cannot confer access to an API. Role defaults are
stored in the database; member rows are explicit grant/deny overrides (absence inherits defaults). Revoking is an
explicit deny, never physical deletion. Re-saving the same settings is a no-op.

ADMIN retains all active catalog permissions and cannot change its own role permissions. Only Admin changes other
role defaults, within that role's allowed catalog (Farmer: own/customer APIs only; no staff/administrative APIs).
Owner can change permissions of ACTIVE SALES_STAFF members in the active store only. Targeting self, another
Owner, Admin, Farmer, Delivery Staff or another store is refused. Delegable permissions exclude role/permission
management, staff identity administration and audit access. Each grant must be held by the Owner. Effective Sale
permissions are also capped by current Owner-role defaults; withdrawing a permission from Owner therefore removes
the corresponding Sale authority immediately, even when there is an old explicit grant.

Live checks verify active User, Role and store membership on every request. No permission claims in JWT and no
cross-request permission cache. Role changes and overrides do not require re-login. One transaction owner handles
each change: transaction → permission write lock → fresh authorization/ceiling checks → expected version check →
upsert grant/deny rows → bump parent version → audit → single SaveChanges/commit. The PostgreSQL transaction advisory
lock serializes permission configuration changes and prevents concurrent revocation/delegation races. Stale drafts
return 409 rather than overwrite a newer configuration.

## APIs

| Route | Authorization | Shape |
|---|---|---|
| GET `/api/me/permissions` | authenticated active user | `{role, storeId?, roleVersion, memberVersion?, permissions:[code]}` |
| GET `/api/permissions` | Admin or Owner with PERMISSIONS.DELEGATE | catalog `{id, code, module, name, delegable, defaultRoles, allowedRoles}`[] |
| GET `/api/roles` | Admin | `{id, code, name, description, version, editable, permissionCodes}`[] |
| PUT `/api/roles/{id}/permissions` | Admin | `{permissionCodes:[code], version, reason}` → role configuration |
| GET `/api/staff/{userId}/permissions` | Admin or authorized Owner, active Sale in own store | `{userId, fullName, role, storeId, version, roleVersion, defaultPermissions, effectivePermissions, overrides:[{code, granted}], grantablePermissions}` |
| PUT `/api/staff/{userId}/permissions` | same scope | `{overrides:[{code, granted}], version, roleVersion, reason}` → configuration |

PUT member request is a patch: only listed overrides change. Preserve unlisted overrides. This avoids granting other
permissions indirectly when an Owner cannot edit them. The UI submits only changed checkboxes. Reasons required,
≤500 characters; at most catalog-size entries, no duplicates/unknown codes; all entries (including deny) must be
delegable and within the actor's authority. `roleVersion` guards defaults changed while an Owner edited a draft.

## Enforcement and UI

Every existing business controller action has a reviewed permission mapping. Authorization filter checks permission
before validation/action execution; unknown protected actions fail closed and a coverage test rejects omissions.
An authorization handler allows a staff member with an explicit delegable grant past legacy role gates for that
business action; the filter still denies a role holder whose effective permission was withdrawn. Farmer/driver
ownership and identity-management role gates remain intact. Public catalog, anonymous auth/webhook, authenticated
session/security recovery, own notifications and the current-permission discovery endpoint are explicitly classified
exceptions: their existing authentication, ownership and signature checks remain mandatory.

Web reads permissions from the API, refreshes on navigation/window focus and periodically while visible, plus
immediately after configuration writes. Loading/errors fail closed. Route guards, menus and action controls use the
same codes. Backend is always authoritative, including the interval before a browser refresh. Checkbox management
replaces mock role configuration and mock staff IDs; Owner selects real Sale members from `/api/staff`.

Audit actions: ROLE_PERMISSIONS_CHANGED (global role; Admin only), STAFF_PERMISSIONS_CHANGED (store-scoped member).
Record old/new grants, actor, target, reason and request metadata atomically; do not record credentials/tokens.
API errors: 400 invalid input, 401 unauthenticated, 403 outside authority/self-elevation, 404 inaccessible target,
409 stale version. No API mutates the audit history.
