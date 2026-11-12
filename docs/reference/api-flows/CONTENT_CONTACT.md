# Articles and contact requests

User-requested extension, 2026-11-12. Uses the existing `articles` and `contact_requests` schema.
All lists follow README pagination and validation conventions. Article content is plain text, preserving newlines.
There are no article categories, view counters or review states in the schema; clients must not fabricate them.

| Route | Access | Result |
|---|---|---|
| GET /api/articles | Public | Published articles only, search, paged |
| GET /api/articles/{slug} | Public | Published article, otherwise 404 |
| GET /api/admin/articles | ADMIN, STORE_OWNER + ARTICLES.READ | All active articles, search/status, paged |
| GET /api/admin/articles/{id} | Same | Full article |
| POST /api/admin/articles | ADMIN, STORE_OWNER + ARTICLES.MANAGE | Create draft, 201 |
| PUT /api/admin/articles/{id} | Same | Edit content, 200 |
| POST /api/admin/articles/{id}/status | Same | DRAFT/PUBLISHED/ARCHIVED, 200 |
| DELETE /api/admin/articles/{id} | Same | Soft delete, 204 |
| POST /api/files/article-images | ADMIN, STORE_OWNER + ARTICLES.MANAGE | multipart `file`, JPEG/PNG/WebP max 3 MB, 201 {url, storageKey, sizeBytes} |
| POST /api/contact-requests | Public, optionally authenticated | Create OPEN request, 201 |
| GET /api/me/contact-requests | FARMER + MY_CONTACT_REQUESTS.READ | JWT owner's requests only, paged |
| GET /api/me/contact-requests/{id} | Same | Owner's request, otherwise 404 |
| GET /api/contact-requests | ADMIN, STORE_OWNER, SALES_STAFF + CONTACT_REQUESTS.READ | search/status, paged |
| GET /api/contact-requests/{id} | Same | Request detail |
| PUT /api/contact-requests/{id} | Same roles + CONTACT_REQUESTS.MANAGE | status/resolutionNote, 200 |

Article request: title (255), slug (255, lowercase ASCII hyphenated), summary? (1000), content (required, max 100000),
thumbnailUrl (required, 1000, http/https). Article create/edit requires a thumbnail, per user requirement.
Upload it through `/api/files/article-images` first; the existing public image bucket and validated image pipeline are reused.
Author and timestamps come from the server. Duplicate active slug gives 409.
Article response: id, title, slug, summary, content, thumbnailUrl, status, authorId, authorName, publishedAt, createdAt, updatedAt.
Publication sets publishedAt on first publication; returning to draft or archiving hides the article publicly.

Contact create: contactName (150), contactPhone? (20), contactEmail? (255), subject (255), message (10000).
At least one valid phone/email is required. userId comes only from the authenticated identity, nullable for guests.
Number is server-generated CR-yyyyMMdd-NNNN; a concurrent number collision returns 409 for retry.
Contact response: id, requestNumber, contactName, contactPhone, contactEmail, subject, message, status,
resolvedAt, resolutionNote, createdAt, updatedAt. User/assignee IDs are not exposed publicly.
Update: status OPEN/IN_PROGRESS/RESOLVED/CLOSED, resolutionNote? (2000). RESOLVED requires a nonempty reply;
reopening clears the resolution timestamp. Guests receive a receipt but cannot query private history.

Content mutations are owned by ArticleService / ContactRequestService with one SaveChanges per operation.
New permission defaults are delivered by a reviewed migration; existing table shapes are unchanged.

Deployment from the backend repository (with the intended database connection configured):

```powershell
dotnet ef database update --project src/AgriSage.Infrastructure --startup-project src/AgriSage.Api
```

Migration: `20261112151319_ContentContactPermissions`. Existing roles receive additive defaults;
new roles receive their defaults through the existing reference-data seeding flow.
Image storage uses the existing public catalog-image configuration, with no new bucket or schema migration.
