# AI diagnosis — rice leaf disease, Human Review and recommendations

Version 1.0 — 2026-10-10 (user-requested extension, same status as `REPORTS.md` and `DYNAMIC_PERMISSIONS.md`).
Conventions, shared shapes and ownership rules: [README.md](README.md). Authorization of every route below follows
[DYNAMIC_PERMISSIONS.md](DYNAMIC_PERMISSIONS.md).

Sources: `DATABASE_DESIGN.md` §55–63 and §35.12; `BUSINESS_RULES.md` rules 35–37, 41, 59–61;
`docs/AgriSage_Updated_Documentation_v1.1/AgriSage_AI_Disease_Diagnosis_Master_v2.0.md` §38–42, §50, §53–55;
the `AI-Service` repository (`README.md`: HTTP contract of the inference service).

A Farmer photographs a rice leaf. The Python AI Service returns calibrated class probabilities. **This backend** applies
the active policy (minimum confidence / margin) and stores the result as evidence. An authorized reviewer
(`can_review_ai`) then confirms, corrects or marks the case inconclusive. Only a verified case produces treatment
and product recommendations, and only the verified result is shown to the Farmer.

Already on `main`: Domain (`DiagnosisCase` and children, `AiModel`, `AiPolicyConfig`, `Disease`,
`DiseaseTreatment`), EF configurations, `DiseaseSeeder` (the five classes), `IAiDiagnosisClient` and its contracts,
`NotificationWriter`, `AuditTrail`, dynamic permissions. **Nothing above Domain exists yet**: no Application service,
no controller, no `IAiDiagnosisClient` implementation, no DI registration.

---

## 1. Demo script

| # | Step (screen) | API | Effect |
|---|---|---|---|
| 1 | Admin registers the exported model and a policy, activates both | `POST /api/ai-models`, `POST /api/ai-models/{id}/policies`, `…/activate` | one ACTIVE model, one ACTIVE policy |
| 2 | Owner lets a sales staff member review AI cases | `PUT /api/staff/{userId}/ai-review` | `store_members.can_review_ai = true` |
| 3 | Farmer photographs a leaf (mobile) | `POST /api/me/diagnosis-cases` | case `AI_COMPLETED` (or `FAILED`), inference stored |
| 4 | Farmer sees "waiting for a specialist" | `GET /api/me/diagnosis-cases/{id}` | no probabilities, no disease |
| 5 | Reviewer opens the queue and the case | `GET /api/diagnosis-cases`, `GET /api/diagnosis-cases/{id}` | photo (signed URL), top-k, margin, passed policy |
| 6 | Reviewer confirms, corrects or marks inconclusive | `POST /api/diagnosis-cases/{id}/review` | case `VERIFIED` / `INCONCLUSIVE`, Farmer notified |
| 7 | Reviewer attaches treatments and products | `POST /api/diagnosis-cases/{id}/recommendations` | `recommendation_items` |
| 8 | Farmer reads the result and adds a product to the cart | `GET /api/me/diagnosis-cases/{id}`, cart routes | verified disease, guidance, products |
| 9 | AI Service is down: case `FAILED`; reviewer diagnoses by hand or reruns the AI | `POST …/review`, `POST …/rerun-ai` | manual review (CORRECTED / INCONCLUSIVE) or a new inference |

---

## 2. Tasks

| Task | Content | Depends on |
|---|---|---|
| F5.1 | AI client adapter (`FastApiDiagnosisClient`, simulated client, options, exceptions) | — |
| F5.2 | AI models and policies management | — |
| F5.3 | Private diagnosis image storage and signed URLs | — |
| F5.4 | Farmer API: create / list / get / cancel own cases | F5.1, F5.2, F5.3 |
| F5.5 | Reviewer API: queue, detail, start review, review, rerun, recommendations | F5.4 |
| F5.6 | `can_review_ai` management and the review guard | — |
| F5.7 | Disease content and treatments (staff) | — |

Schema: **no new table**. One data-only migration (§12) adds the new permission catalog rows, because the catalog
is seeded with `HasData`. Tell the lead before creating it.

---

## 3. Decisions

| # | Decision |
|---|---|
| D1 | **The policy decision lives in the backend.** The AI Service returns calibrated probabilities and never ACCEPTED / INCONCLUSIVE. Backend computes `margin = p1 − p2` and `passed_policy = status SUCCESS AND confidence ≥ minimum_confidence AND (minimum_margin IS NULL OR margin ≥ minimum_margin)`. |
| D2 | **A failed policy is not INCONCLUSIVE.** Per §35.12 the case still becomes `AI_COMPLETED` with `passed_policy = false` and goes to the queue. `INCONCLUSIVE` is only ever a reviewer's decision. (The AI master document AC-03 reads otherwise; the database design wins, README intro.) |
| D3 | **Every case goes to human review** (rule 36), whether or not the policy passed. `requires_human_review` is stored but never bypasses it. |
| D4 | **The AI is called after the case is saved and outside any transaction.** Transaction 1: number, case, image → commit. Then the HTTP call (timeout `AiService:TimeoutSeconds`, default 15). Transaction 2: `StartProcessing` → `RecordInference` → `CompleteAi`/`FailAi`. A crash in between leaves the case `SUBMITTED`, which `rerun-ai` accepts. No transaction is held open across an HTTP call. |
| D5 | **At most one ACTIVE model** (Application retires the previous one in the activation transaction) and at most one ACTIVE policy effective at a given moment per model. |
| D6 | **Always ask the service for `top_k = 5`** and store all five in `top_predictions`; the screens show `policy.top_k` of them. Margin is computed from the stored list. |
| D7 | **Reviewer authorization has two layers.** (a) The endpoint's permission code (`PermissionFilter`, see §11) is the coarse gate. (b) `AiReviewer.EnsureAsync` additionally requires `store_members.can_review_ai = true` for the active store (business rule 41, rule "AI reviewer = can_review_ai, not a primary role"). The member must belong to the active store and be ACTIVE; ADMIN is not exempt, because `agent_reviews.reviewer_member_id` references `store_members` and an Admin has no membership (an Admin can read cases, not decide them). The flag is the single source of truth for who may decide a case; permission codes only open the routes. |
| D8 | **Farmer sees verified results only.** Before a review the response carries no AI output and no disease. For INCONCLUSIVE it carries the reviewer's comment and a retake hint, never a recommendation. |
| D9 | **Photos are private.** Bucket `diagnosis-images`, objects fetched through short-lived signed URLs (1 hour) created at read time; the stored `image_url` is not directly fetchable. |
| D10 | **Model identity is checked.** An answer whose `model_version` differs from the ACTIVE model's `version`, or that has `stub = true` outside Development, is recorded as a FAILED inference (`raw_output.error = MODEL_VERSION_MISMATCH` / `STUB_REFUSED`), never as a result. |
| D11 | **DELETE is semantic.** Cases are cancelled, recommendations deactivated, treatments deactivated. No route removes an inference or a review. |
| D12 | **Case numbers** are `DG-yyyyMMdd-NNNN` (append `DG` to `DocumentNumbers`). |

---

## 4. F5.1 — AI client adapter

`IAiDiagnosisClient.PredictAsync(AiPredictionRequest, CancellationToken)` stays; `AiPredictionResult` gains three
**trailing optional** members so existing callers and tests keep compiling:

```csharp
public sealed record AiPredictionResult(
    string PredictedClassLabel, decimal Confidence, IReadOnlyList<AiClassScore> TopPredictions,
    string ModelVersion, int? InferenceDurationMs,
    decimal? RawConfidence = null, string? ModelName = null, bool IsStub = false);
```

Options (section `AiService`, bound like `PayOsOptions`, validated at use):

| Key | Default | Meaning |
|---|---|---|
| `Mode` | `Real` | `Real` or `Simulated`. `Simulated` is refused outside Development (same guard as `PayOsMode`). |
| `BaseUrl` | — | e.g. `http://localhost:8001` |
| `ApiKey` | — | sent as header `X-Api-Key`; secret, never logged, never committed |
| `TimeoutSeconds` | 15 | per call |

Mapping of `POST {BaseUrl}/v1/inference` (multipart: `image`, `top_k`, `case_id`):

| AI Service field | Becomes |
|---|---|
| `predicted_class_label` | `PredictedClassLabel` (a `diseases.code`) |
| `confidence` (calibrated) | `Confidence` and `ai_inferences.confidence` (numeric(7,6)) |
| `raw_confidence` | `RawConfidence` → `raw_output.rawConfidence` |
| `top_predictions[]` | `TopPredictions` → `ai_inferences.top_predictions` |
| `model_name`, `model_version` | `ModelName`, `ModelVersion` → `raw_output` |
| `inference_duration_ms` | `InferenceDurationMs` → `ai_inferences.inference_duration_ms` |
| `stub` | `IsStub` → `raw_output.stub` |

- Not configured, unreachable, timeout, non-2xx or an unparseable body → `AiServiceUnavailableException`
  (Application, no provider details), exactly like `PaymentGatewayUnavailableException`. Only the HTTP status is
  logged. The create-case flow turns it into a `FAILED` case (D4); `rerun-ai` returns it as 503.
- A label that is not one of the five `diseases.code` values is a FAILED inference (`UNKNOWN_CLASS_LABEL`).
- `predicted_disease_id` is looked up from `diseases` by code.

`raw_output` shape (jsonb), written by Application:

```json
{ "rawConfidence": 0.913020, "modelName": "agrisage-rice-classifier", "modelVersion": "0.1.0",
  "stub": false, "margin": 0.7321, "error": null }
```

`top_predictions` shape: `[ { "classLabel": "LEAF_BLAST", "confidence": 0.842113 }, … ]`, most probable first.

---

## 5. F5.2 — Models and policies

| Method | Route | Permission | Body / query | Response |
|---|---|---|---|---|
| GET | `/api/ai-models` | `AI_MODELS.READ` | `status`, paging | `200 PagedResult<AiModelResponse>` |
| GET | `/api/ai-models/{id}` | `AI_MODELS.READ` | — | `200 AiModelResponse` (with policies) |
| POST | `/api/ai-models` | `AI_MODELS.CREATE` | `AiModelRequest` | `201 AiModelResponse`, status DRAFT |
| POST | `/api/ai-models/{id}/activate` | `AI_MODELS.ACTIVATE` | — | `200`, the previously ACTIVE model becomes RETIRED (D5) |
| POST | `/api/ai-models/{id}/retire` | `AI_MODELS.RETIRE` | — | `200` |
| GET | `/api/ai-models/{id}/policies` | `AI_POLICIES.READ` | — | `200 AiPolicyResponse[]` |
| POST | `/api/ai-models/{id}/policies` | `AI_POLICIES.CREATE` | `AiPolicyRequest` | `201`, status DRAFT |
| POST | `/api/ai-policies/{id}/activate` | `AI_POLICIES.ACTIVATE` | — | `200` |
| POST | `/api/ai-policies/{id}/deactivate` | `AI_POLICIES.DEACTIVATE` | — | `200` |

```json
// AiModelRequest — copy of the exported manifest.json of the AI Service
{ "name": "agrisage-rice-classifier", "version": "0.1.0", "framework": "pytorch", "architecture": "mobilenet_v2",
  "modelStorageUrl": "https://github.com/…/releases/…", "inputWidth": 224, "inputHeight": 224,
  "classLabels": ["LEAF_BLAST","BACTERIAL_LEAF_BLIGHT","BROWN_SPOT","SHEATH_BLIGHT","HEALTHY"],
  "metrics": { "testMacroF1": 0.92, "temperature": 1.3962 } }

// AiPolicyRequest
{ "version": "policy-1", "minimumConfidence": 0.80, "minimumMargin": 0.15, "topK": 3,
  "effectiveFrom": "2026-11-01T00:00:00Z", "effectiveTo": null, "parameters": { "note": "…" } }
```

- `classLabels` must be **exactly** the five `diseases.code` values (set equality, any order) → else `422`.
- `(name, version)` unique → `409`. Policy `(aiModelId, version)` unique → `409`.
- Activating a policy whose effective period overlaps another ACTIVE policy of the same model → `422` naming it.
  A policy can only be activated while its model is ACTIVE.
- Model status moves DRAFT → ACTIVE → RETIRED only (Domain). Retiring a model does not touch old inferences.

---

## 6. F5.3 — Diagnosis images

- `StorageArea.DiagnosisImages`; `StorageOptions.DiagnosisImageBucket` (default `diagnosis-images`, **private**).
  `BucketFor` must map it explicitly (today every unknown area falls back to the product-image bucket).
- `IFileStorageService.CreateSignedUrlAsync(storageKey, area, TimeSpan ttl, ct)` → Supabase
  `POST /storage/v1/object/sign/{bucket}/{key}`; `StorageUnavailableException` on failure.
- Rules: JPEG / PNG / WebP by magic bytes (`ImageRules.Detect`), `MaxDiagnosisBytes = 5 MB`, server-generated key
  `yyyy/MM/{guid}.{ext}`, the client's file name is stored only in `file_name` after trimming.
- `diagnosis_images.storage_key` holds the key; `image_url` holds the non-public object path and is never returned.
  Responses carry `imageUrl` = a signed URL created at read time (D9).
- A photo uploaded for a case that then fails to save is deleted again (best effort, logged).

---

## 7. F5.4 — Farmer API (`/api/me/diagnosis-cases`)

Identity from the JWT through `CurrentFarmer.GetAsync`; another Farmer's case is `404`.

| Method | Route | Permission | Body / query | Response |
|---|---|---|---|---|
| POST | `/api/me/diagnosis-cases` | `MY_DIAGNOSIS.CREATE` | multipart: `image` (file, required), `note` (≤ 1000) | `201 MyDiagnosisCaseResponse` + `Location` |
| GET | `/api/me/diagnosis-cases` | `MY_DIAGNOSIS.READ` | `status`, paging | `200 PagedResult<MyDiagnosisCaseListItem>` |
| GET | `/api/me/diagnosis-cases/{id}` | `MY_DIAGNOSIS.READ` | — | `200 MyDiagnosisCaseResponse` |
| POST | `/api/me/diagnosis-cases/{id}/cancel` | `MY_DIAGNOSIS.CANCEL` | `{ "reason": "…" }` optional | `200` |

Rate limit: the existing `upload` policy (30 / minute / IP) on `POST`.

```json
// MyDiagnosisCaseResponse
{
  "id": "uuid", "caseNumber": "DG-20261110-0001", "status": "VERIFIED",
  "submittedAt": "2026-11-10T03:12:00Z", "completedAt": "2026-11-10T04:02:00Z",
  "farmerNote": "Lá vàng từ chóp",
  "image": { "id": "uuid", "imageUrl": "https://…signed…", "expiresAt": "2026-11-10T05:12:00Z" },
  "result": {                       // only when status = VERIFIED
    "disease": { "id": "uuid", "code": "LEAF_BLAST", "name": "Đạo ôn", "isHealthy": false,
                 "symptoms": "…", "prevention": "…" },
    "reviewerComment": "Vết bệnh hình thoi điển hình.",
    "treatments": [ { "id": "uuid", "type": "CHEMICAL", "title": "…", "instructions": "…", "precautions": "…" } ],
    "products": [ { "storeProductId": "uuid", "name": "…", "sku": "…", "reason": "…", "rankOrder": 1 } ]
  },
  "inconclusive": { "comment": "Ảnh bị mờ, vui lòng chụp lại.", "hint": "RETAKE_PHOTO" }   // only INCONCLUSIVE
}
```

- Statuses reach the Farmer unchanged (`SUBMITTED`, `PROCESSING`, `AI_COMPLETED`, `UNDER_REVIEW`, `FAILED` all mean
  "waiting"); the apps show one waiting state. **No AI field, no probability, no model name, ever** (D8).
- `products` come from `recommendation_items` of the **current** review only, active and sellable at read time;
  prices are the normal catalog prices (no price in this response, the app fetches the product).
- `POST` flow (D4): validate the image → `IFileStorageService.UploadAsync` → transaction 1 → AI call → transaction 2.
  The response reflects the state after transaction 2: `AI_COMPLETED` (waiting) or `FAILED` (waiting, manual).
- Cancel: allowed from SUBMITTED, PROCESSING, AI_COMPLETED, UNDER_REVIEW, FAILED (Domain `Cancel`).

---

## 8. F5.5 — Reviewer API (`/api/diagnosis-cases`)

Listing and reading a case need only the permission code. Every route that decides or changes a case (`start-review`,
`review`, `rerun-ai`, recommendations) also needs D7 layer (b).

| Method | Route | Permission | Body / query | Response |
|---|---|---|---|---|
| GET | `/api/diagnosis-cases` | `DIAGNOSIS.READ` | `status`, `aiPassed`, `from`, `to`, `search` (case number, Farmer name or phone), paging; default sort `submittedAt` ascending | `200 PagedResult<DiagnosisCaseListItem>` |
| GET | `/api/diagnosis-cases/{id}` | `DIAGNOSIS.READ` | — | `200 DiagnosisCaseResponse` |
| POST | `/api/diagnosis-cases/{id}/start-review` | `DIAGNOSIS.START_REVIEW` | — | `200`, `AI_COMPLETED → UNDER_REVIEW` |
| POST | `/api/diagnosis-cases/{id}/review` | `DIAGNOSIS.REVIEW` | `ReviewRequest` | `200 DiagnosisCaseResponse` |
| POST | `/api/diagnosis-cases/{id}/rerun-ai` | `DIAGNOSIS.RERUN_AI` | — | `200`, or `503` when the AI Service is unreachable |
| POST | `/api/diagnosis-cases/{id}/recommendations` | `DIAGNOSIS.RECOMMEND` | `RecommendationRequest` | `201` |
| DELETE | `/api/diagnosis-cases/{id}/recommendations/{recommendationId}` | `DIAGNOSIS.UNRECOMMEND` | — | `204` (deactivates) |

```json
// ReviewRequest
{ "decision": "CORRECTED", "finalDiseaseId": "uuid", "primaryAiInferenceId": "uuid", "comment": "…" }
// RecommendationRequest — exactly one target
{ "type": "TREATMENT", "diseaseTreatmentId": "uuid", "rankOrder": 1, "reason": "…" }
{ "type": "PRODUCT",   "storeProductId": "uuid",      "rankOrder": 2, "reason": "…" }
```

```json
// DiagnosisCaseResponse (reviewer view)
{
  "id": "uuid", "caseNumber": "DG-…", "status": "AI_COMPLETED", "submittedAt": "…", "completedAt": null,
  "farmer": { "farmerProfileId": "uuid", "fullName": "…", "phoneNumber": "…" },
  "farmerNote": "…", "finalDisease": null,
  "images": [ { "id": "uuid", "imageUrl": "https://…signed…", "isPrimary": true, "fileName": "…", "uploadedAt": "…" } ],
  "inferences": [ {
      "id": "uuid", "diagnosisImageId": "uuid", "status": "SUCCESS", "inferredAt": "…", "durationMs": 83,
      "model": { "id": "uuid", "name": "agrisage-rice-classifier", "version": "0.1.0" },
      "policy": { "id": "uuid", "version": "policy-1", "minimumConfidence": 0.80, "minimumMargin": 0.15, "topK": 3 },
      "predictedClassLabel": "LEAF_BLAST", "predictedDiseaseId": "uuid",
      "confidence": 0.842113, "margin": 0.7406, "passedPolicy": true,
      "topPredictions": [ { "classLabel": "LEAF_BLAST", "diseaseId": "uuid", "confidence": 0.842113 } ]
  } ],
  "currentReview": null,
  "reviewHistory": [],
  "recommendations": []
}
```

- `topPredictions` is cut to the policy's `topK`; the full list stays in the database.
- `review` rules (Domain `Review`, §35.12): CONFIRMED needs a SUCCESS inference and the final disease equal to the
  inference's predicted disease; CORRECTED needs `finalDiseaseId`; INCONCLUSIVE has none. A case with no SUCCESS
  inference (FAILED) can only be CORRECTED or INCONCLUSIVE. A new review supersedes the current one and deactivates
  its recommendations; history is kept. `primaryAiInferenceId` defaults to the latest SUCCESS inference.
- A re-review is two saves in one transaction (`ReleaseCurrentReview`, then `Review`): the database allows one current
  review per case (`ux_agent_reviews_current`) while the old review points at its successor
  (`superseded_by_review_id`), so a single INSERT/UPDATE batch cannot satisfy both.
- `recommendations`: only on a VERIFIED case; PRODUCT must be active and sellable and is refused for the Healthy class
  (`422`). Duplicate target on the same review → `409`.
- `rerun-ai`: allowed from SUBMITTED or FAILED (Domain). Runs D4 again for the primary image and appends a new
  immutable inference; earlier ones stay.
- Audit actions: `DIAGNOSIS_CASE_CREATED`, `DIAGNOSIS_REVIEWED`, `DIAGNOSIS_RECOMMENDATION_ADDED`,
  `DIAGNOSIS_RECOMMENDATION_REMOVED`, `AI_MODEL_ACTIVATED`, `AI_MODEL_RETIRED`, `AI_POLICY_ACTIVATED`,
  `AI_POLICY_DEACTIVATED`, `STAFF_AI_REVIEW_CHANGED`.
- Notifications to the Farmer come from the existing committed-notification worker
  (`CommittedNotificationService.DiagnosisAsync`, entity type `DIAGNOSIS_CASE`, keys `diagnosis-ai:`, `diagnosis-review:`,
  `diagnosis-recommendations:`): `AI_DIAGNOSIS_COMPLETED`, `DIAGNOSIS_REVIEWED` (VERIFIED and INCONCLUSIVE alike) and
  `DIAGNOSIS_RECOMMENDATIONS`. The use cases write none themselves: an in-transaction notification as well gave the
  Farmer two alerts per decision (found in the first end-to-end run on 2026-10-10). The worker only reports a review
  made by a member who still has `can_review_ai`.

---

## 9. F5.6 — `can_review_ai`

| Method | Route | Permission | Body | Response |
|---|---|---|---|---|
| PUT | `/api/staff/{userId}/ai-review` | `STAFF.SET_AI_REVIEW` | `{ "enabled": true, "reason": "…" }` | `200 StaffResponse` |

- Uses `StoreMember.GrantAiReview()` / `RevokeAiReview()`; audit `STAFF_AI_REVIEW_CHANGED` with old and new value
  and the reason. Only for ACTIVE SALES_STAFF or STORE_OWNER members of the active store; Admin may target any.
- `GET /api/auth/me` (`CurrentUserResponse`) gains `canReviewAi` so the web can show or hide the review screens.
  `GET /api/me/permissions` is unchanged and lists permission codes.
- `AiReviewer.EnsureAsync(ct)` (Application, shared by F5.5): resolves the caller's `StoreMember` for the active
  store, `403` when missing, not ACTIVE or `CanReviewAi = false` (ADMIN included, see D7). The reviewer's
  `store_members.id` becomes `agent_reviews.reviewer_member_id`.

---

## 10. F5.7 — Disease content and treatments

| Method | Route | Permission | Body | Response |
|---|---|---|---|---|
| GET | `/api/diseases` | `DISEASES.READ` | `isActive` | `200 DiseaseResponse[]` with treatments |
| PUT | `/api/diseases/{id}` | `DISEASES.UPDATE` | `name`, `scientificName`, `description`, `symptoms`, `causes`, `prevention` | `200` (calls `UpdateContent`; `code` and the healthy flag never change) |
| POST | `/api/diseases/{id}/treatments` | `DISEASE_TREATMENTS.CREATE` | `treatmentType`, `title`, `instructions`, `activeIngredientId?`, `precautions?`, `priority` | `201` |
| PUT | `/api/disease-treatments/{id}` | `DISEASE_TREATMENTS.UPDATE` | same | `200` |
| POST | `/api/disease-treatments/{id}/activate` | `DISEASE_TREATMENTS.ACTIVATE` | — | `200` |
| DELETE | `/api/disease-treatments/{id}` | `DISEASE_TREATMENTS.DEACTIVATE` | — | `204` (deactivate, D11) |

`treatmentType`: `CULTURAL`, `CHEMICAL`, `PREVENTIVE`, `OTHER`. Reference data is entered through these routes; it is
not seeded (`SEED_REFERENCE_DATA.md`).

---

## 11. Permission catalog additions

Added to `PermissionCatalog.Entries` (stable ids) **and** to `PermissionEndpointMap.Codes` for every action — an
action without a code fails closed with `403`, and the coverage test rejects omissions.

| Module | Codes | Delegable | Default roles |
|---|---|---|---|
| `MY_DIAGNOSIS` | `CREATE`, `READ`, `CANCEL` | no | FARMER |
| `DIAGNOSIS` | `READ`, `START_REVIEW`, `REVIEW`, `RERUN_AI`, `RECOMMEND`, `UNRECOMMEND` | yes | ADMIN, STORE_OWNER, SALES_STAFF (D7: the flag decides who may act) |
| `AI_MODELS` | `READ`, `CREATE`, `ACTIVATE`, `RETIRE` | no | ADMIN, STORE_OWNER |
| `AI_POLICIES` | `READ`, `CREATE`, `ACTIVATE`, `DEACTIVATE` | no | ADMIN, STORE_OWNER |
| `DISEASES` | `READ` (+ SALES_STAFF), `UPDATE` | no | ADMIN, STORE_OWNER |
| `DISEASE_TREATMENTS` | `CREATE`, `UPDATE`, `ACTIVATE`, `DEACTIVATE` | no | ADMIN, STORE_OWNER |
| `STAFF` | `SET_AI_REVIEW` | no | ADMIN, STORE_OWNER |

Controller and action names that key the map: `MeDiagnosisCases.{Create,List,Get,Cancel}`,
`DiagnosisCases.{List,Get,StartReview,Review,RerunAi,Recommend,Unrecommend}`, `AiModels.*`, `AiPolicies.*`,
`Diseases.*`, `DiseaseTreatments.*`, `Staff.SetAiReview`.

---

## 12. Configuration, migration, operations

- `appsettings.Development.json`: `AiService:BaseUrl` / `TimeoutSeconds` and `Storage:DiagnosisImageBucket`, no secrets.
  `appsettings.Local.json` (or user secrets): `AiService:ApiKey`. `Mode=Simulated` (Development only) answers with
  deterministic made-up predictions flagged `stub`; register a model whose version is `AiService:SimulatedModelVersion`
  (default `simulated`). Outside Development a `stub` answer is refused (D10).
- `/api/auth/me` `canReviewAi` is true for an ACTIVE store member with the flag.
- Migration `AiDiagnosisPermissions` (data only: the new `permissions` rows, plus `role_permissions` defaults
  through `PermissionSeeder`). Review it as a data migration: no schema operation is expected.
- Supabase: create the private bucket `diagnosis-images` and set `Storage:DiagnosisImageBucket`. The service key
  already configured is reused.
- Retention of diagnosis photos is not defined in the documents: until decided, photos are kept.
- Health: the backend does not call `/health` on a schedule; a failing AI Service shows up as `FAILED` cases.

---

## 13. Tests — must prove

| Task | Must prove |
|---|---|
| F5.1 | field mapping for a normal answer; unreachable / timeout / 5xx / bad JSON → `AiServiceUnavailableException`; unknown label, version mismatch and `stub` are FAILED inferences; `Simulated` refused outside Development; the API key is never logged |
| F5.2 | `classLabels` set equality (missing, extra, duplicate → 422); one ACTIVE model after two activations; overlapping policy → 422; activation needs an ACTIVE model; 409 on duplicates |
| F5.3 | bucket mapping for `DiagnosisImages`; signed URL only for a case's own image; oversized / wrong type → 400 |
| F5.4 | 401 / 403 (not FARMER); another Farmer's case → 404; case saved before the AI call; AI down → `FAILED`, photo kept; two transactions, none open across the HTTP call; response never contains probabilities; cancel states |
| F5.5 | role + permission + `can_review_ai` matrix (Farmer, Delivery, Sales without flag → 403, Sales with flag, Owner, Admin); passed-policy computed with margin; CONFIRMED vs predicted disease; FAILED → manual CORRECTED; re-review supersedes and deactivates recommendations; PRODUCT refused for Healthy; Farmer notified once (dedup); `rerun-ai` appends an inference |
| F5.6 | grant / revoke audited; `canReviewAi` in `/api/auth/me`; revoked reviewer is refused on the next call without re-login |
| F5.7 | `code` and healthy flag immutable; DELETE deactivates; deactivated treatment not recommendable |
| all | every new action is in `PermissionEndpointMap` (coverage test), Swagger lists the routes, `[RealDbFact]` end-to-end case → inference → review → recommendation, rolled back |

---

## 14. Open points for the lead

1. Create the data-only migration `AiDiagnosisPermissions` (§12) — one migration at a time, announce first.
2. Create the private Supabase bucket `diagnosis-images`.
3. Confirm D7 (two layers) and that D2 (a failed policy still goes to the queue) is accepted over AC-03 of the AI
   master document, which should then be corrected.
4. Retention period for diagnosis photos.
