# AI diagnosis — crop leaf disease, AI result shown to the Farmer, suggestions

Version 2.2 — 2026-11-20: the Farmer **chooses the crop** before the photo, a **general vision model** answers among that crop's diseases, and the admin enters its **API key** (stored encrypted). See §15 and D16–D18; the FastAPI model keeps working unchanged.
Version 2.3 — 2026-11-21: the general model can be **Claude** (framework `ANTHROPIC`) **or Gemini** (framework `GOOGLE`, Gemini API key from Google AI Studio); one provider setting each, routes `/api/admin/ai-providers/{provider}` (§15).
Version 2.1 — 2026-11-02: suggested products now come from reviewed problem-to-product links ([ADVISORY.md](ADVISORY.md), D13 replaced), no longer from an active-ingredient match.
Version 2.0 — 2026-10-21. **Human Review is removed** (lead's decision, 2026-10-21): the Farmer sees the AI result as
soon as the photo is analysed; no staff member confirms it. Version 1.0 (2026-10-10) required a `can_review_ai`
reviewer before any result or recommendation; its routes for reviewing are gone (§8, §9).
Conventions, shared shapes and ownership rules: [README.md](README.md). Authorization of every route below follows
[DYNAMIC_PERMISSIONS.md](DYNAMIC_PERMISSIONS.md).

Sources: `DATABASE_DESIGN.md` §55–63 and §35.12; `BUSINESS_RULES.md` rules 35–37, 59–61;
`docs/AgriSage_Updated_Documentation_v1.1/AgriSage_AI_Disease_Diagnosis_Master_v2.0.md` §38–42, §50, §53–55 (its
Agent Review sections no longer apply); the `AI-Service` repository (`README.md`: HTTP contract of the inference service).

A Farmer photographs a rice leaf. The Python AI Service returns calibrated class probabilities. **This backend** applies
the active policy (minimum confidence / margin) and stores the result. The Farmer then sees the predicted disease, how
sure the AI is (`CONFIDENT` or `UNCERTAIN`, never a probability), the disease's guidance and, only when the AI is
confident, the products of the store that are linked, and approved, for that disease ([ADVISORY.md](ADVISORY.md)). Store staff can read every case to
follow up with the Farmer; they no longer decide anything.

Why: the model is right on about 0.57 of photos from a source it never saw (AI-Service `research/report.md` §4.2b), and
the team will improve it by training rather than by a manual gate. The risk of an unverified pesticide suggestion is
limited by D3 and D13.

---

## 1. Demo script

| # | Step (screen) | API | Effect |
|---|---|---|---|
| 1 | Admin registers the exported model and a policy, activates both | `POST /api/ai-models`, `POST /api/ai-models/{id}/policies`, `…/activate` | one ACTIVE model, one ACTIVE policy |
| 2 | Owner enters disease content and treatments, links products to the disease and a reviewer approves the links ([ADVISORY.md](ADVISORY.md) §6) | `PUT /api/diseases/{id}`, `POST /api/diseases/{id}/treatments`, `POST /api/diseases/{id}/products`, `POST /api/disease-products/{id}/approve` | content and suggestions available |
| 3 | Farmer photographs a leaf (mobile or Farmer web) | `POST /api/me/diagnosis-cases` | case `AI_COMPLETED` with a `result` (or `FAILED`) |
| 4 | Farmer reads the result: disease, certainty, guidance, products (confident only) and adds a product to the cart | `GET /api/me/diagnosis-cases/{id}`, cart routes | — |
| 5 | AI Service is down: case `FAILED`; the Farmer retries once it is back | `POST /api/me/diagnosis-cases/{id}/retry` | a new inference, case `AI_COMPLETED` |
| 6 | Store staff look at the farmers' cases and call a Farmer | `GET /api/diagnosis-cases`, `GET /api/diagnosis-cases/{id}` | photo, AI evidence, what the Farmer saw |

---

## 2. Tasks

| Task | Content | Depends on |
|---|---|---|
| F5.1 | AI client adapter (`FastApiDiagnosisClient`, simulated client, options, exceptions) | — |
| F5.2 | AI models and policies management | — |
| F5.3 | Private diagnosis image storage and signed URLs | — |
| F5.4 | Farmer API: create / list / get / cancel / retry own cases, AI result and suggestions | F5.1, F5.2, F5.3 |
| F5.5 | Store API: read-only list and detail of cases | F5.4 |
| F5.6 | ~~`can_review_ai` management and the review guard~~ — retired in 2.0 | — |
| F5.7 | Disease content and treatments (staff) | — |

Schema: **no new table and no migration** in 2.0. `agent_reviews`, `recommendation_items`,
`store_members.can_review_ai` and `ai_policy_configs.requires_human_review` stay for the cases decided under 1.0; no
code writes them any more. The permission codes of the removed routes stay in the catalog (removing `HasData` rows is
a migration) and are hidden from the permission screens (§11).

---

## 3. Decisions

| # | Decision |
|---|---|
| D1 | **The policy decision lives in the backend.** The AI Service returns calibrated probabilities and never ACCEPTED / INCONCLUSIVE (a general vision model returns its own probability estimate, not calibrated; its policy thresholds come from an evaluation on labelled photos, 2.2). Backend computes `margin = p1 − p2` and `passed_policy = status SUCCESS AND confidence ≥ minimum_confidence AND (minimum_margin IS NULL OR margin ≥ minimum_margin)`. |
| D2 | **A failed policy is an UNCERTAIN result, not INCONCLUSIVE.** The case becomes `AI_COMPLETED` with `passed_policy = false`; the Farmer sees the top guess marked `UNCERTAIN` with the hint `RETAKE_PHOTO_OR_ASK_STORE`. `passed_policy` is the value stored at inference time, under the policy effective then; old cases are never re-judged against today's policy. |
| D3 | **No human review** (2.0). The result of the latest SUCCESS inference is the result. An `UNCERTAIN` result shows no CHEMICAL treatment and no product; a `CONFIDENT` result shows all active treatments and up to five products (D13); the Healthy class never gets products. `requires_human_review` is stored and ignored. |
| D4 | **The AI is called after the case is saved and outside any transaction.** Transaction 1: number, case, image → commit. Then the HTTP call (timeout `AiService:TimeoutSeconds`, default 15). Transaction 2: `StartProcessing` → `RecordInference` → `CompleteAi`/`FailAi`. A crash in between leaves the case `SUBMITTED`, which the Farmer's retry accepts after two minutes. No transaction is held open across an HTTP call. |
| D5 | **At most one ACTIVE model** (Application retires the previous one in the activation transaction) and at most one ACTIVE policy effective at a given moment per model. |
| D6 | **Always ask the service for `top_k = 5`** and store all five in `top_predictions`; the store screens show `policy.top_k` of them. Margin is computed from the stored list. |
| D7 | ~~Reviewer authorization (`can_review_ai`)~~ — retired in 2.0. Reading cases needs only the permission code `DIAGNOSIS.READ`. |
| D8 | **The Farmer sees a disease and a certainty, never a number.** The Farmer response carries no probability, margin, top-k, model or policy. It carries the disease (predicted, or final for a 1.0 VERIFIED case), `certainty` and the guidance. |
| D9 | **Photos are private.** Bucket `diagnosis-images`, objects fetched through short-lived signed URLs (1 hour) created at read time; the stored `image_url` is not directly fetchable. |
| D10 | **Model identity is checked.** An answer whose `model_version` differs from the ACTIVE model's `version`, or that has `stub = true` outside Development, is recorded as a FAILED inference (`raw_output.error = MODEL_VERSION_MISMATCH` / `STUB_REFUSED`), never as a result. |
| D11 | **DELETE is semantic.** Cases are cancelled, treatments deactivated. No route removes an inference. |
| D12 | **Case numbers** are `DG-yyyyMMdd-NNNN` (append `DG` to `DocumentNumbers`). |
| D13 | **Suggested products come from reviewed links and are computed at read time (2.1, replaces the active-ingredient match of 2.0).** Products = the `APPROVED` `PROTECTION` rows of `disease_products` for the predicted disease whose store product passes `CatalogRules.Sellable` and whose stock and price pass [ADVISORY.md](ADVISORY.md) V7; order V8; at most 5; `reason` is the text of the link (e.g. "Đăng ký trị đạo ôn trên lúa"). A product with the same active ingredient but no approved link is **not** offered. `disease_treatments.active_ingredient_id` stays as information. Nothing is stored in `recommendation_items`; each successful AI run still writes a `DIAGNOSIS_AI_RESULT` audit entry with the certainty and the suggested store product ids. |
| D14 | **The Farmer retries a failed analysis.** `retry` is allowed on a `FAILED` case, or a `SUBMITTED` case older than two minutes (D4 crash). It reruns the AI on the primary image with the active model; an unreachable AI Service gives `503` and records nothing. The store has no rerun any more. |
| D16 | **The crop comes first (2.2).** The Farmer picks a crop (`CropCatalog`: RICE, DURIAN, COFFEE, CORN, MANGO) before the photo; the case stores it in `diagnosis_cases.crop_type` (RICE when an older app sends none). A crop is offered only when the ACTIVE model can name at least one of its active `DISEASE` problems; another crop is `422` on create. |
| D17 | **A general model only chooses among the chosen crop's diseases (2.2).** For each case the backend sends the candidates = the active `DISEASE` rows of the case's crop that are in the model's `class_labels`, plus HEALTHY, with their Vietnamese names and symptoms. An answer outside the candidates is a FAILED inference (`CLASS_NOT_FOR_CROP`); a crop without any candidate disease is FAILED without calling the model (`CROP_NOT_SUPPORTED_BY_MODEL`). The model may answer NONE (not this crop, unreadable photo): the result names its likeliest listed disease with confidence 0, so the policy shows UNCERTAIN. A model of framework `ANTHROPIC` or `GOOGLE` registers any active disease codes (HEALTHY required); a model of fixed classes still needs exactly the five rice classes. |
| D18 | **The admin owns the providers and their keys (2.2, 2.3).** One `ai_provider_settings` row per provider (`ANTHROPIC` = Claude, `GOOGLE` = Gemini) holds the model id, timeout, on/off, and the API key encrypted with AES-256-GCM under a master key from configuration (`SecretEncryption:Key`, never in the database). No response and no audit entry carries the key (the audit redacts any field named like a key); the admin sees only that a key is stored. Clearing the key turns the provider off; it cannot be turned on without one. Which provider diagnoses is decided by the ACTIVE model's framework, not by this setting. |
| D15 | **Cases decided under 1.0 stay readable.** `VERIFIED` shows the reviewer's final disease and the stored recommendations of the current review (`certainty = VERIFIED`); `INCONCLUSIVE` keeps its comment and retake hint; an old `UNDER_REVIEW` case reads like `AI_COMPLETED`. No status is added or removed (the status check constraint stays). |

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
  logged. The create-case flow turns it into a `FAILED` case (D4); the Farmer's retry returns it as 503.
- A label that is not one of the five `diseases.code` values is a FAILED inference (`UNKNOWN_CLASS_LABEL`).
- `predicted_disease_id` is looked up from `diseases` by code.

`raw_output` shape (jsonb), written by Application:

```json
{ "rawConfidence": 0.913020, "modelName": "agrisage-rice-classifier", "modelVersion": "0.2.0",
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
{ "name": "agrisage-rice-classifier", "version": "0.2.0", "framework": "pytorch", "architecture": "resnet50",
  "modelStorageUrl": "https://github.com/…/releases/…", "inputWidth": 224, "inputHeight": 224,
  "classLabels": ["LEAF_BLAST","BACTERIAL_LEAF_BLIGHT","BROWN_SPOT","SHEATH_BLIGHT","HEALTHY"],
  "metrics": { "poolTestMacroF1": 0.9842, "temperature": 1.3925 } }

// AiPolicyRequest
{ "version": "policy-2", "minimumConfidence": 0.77, "minimumMargin": 0.20, "topK": 3,
  "effectiveFrom": "2026-11-01T00:00:00Z", "effectiveTo": null, "parameters": { "note": "…" } }
```

- `classLabels` must be **exactly** the five `diseases.code` values (set equality, any order) → else `422`.
- `(name, version)` unique → `409`. Policy `(aiModelId, version)` unique → `409`.
- Activating a policy whose effective period overlaps another ACTIVE policy of the same model → `422` naming it.
  A policy can only be activated while its model is ACTIVE.
- Model status moves DRAFT → ACTIVE → RETIRED only (Domain). Retiring a model does not touch old inferences.
- The policy is now what separates `CONFIDENT` from `UNCERTAIN` for the Farmer (D2, D3): choose it on photos of
  sources the model did not train on (AI-Service `training.shift_calibration`).

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
| POST | `/api/me/diagnosis-cases/{id}/retry` | `MY_DIAGNOSIS.CREATE` | — | `200 MyDiagnosisCaseResponse`; `422` unless FAILED (or SUBMITTED for more than 2 minutes); `503` when the AI Service is unreachable (nothing recorded) |

Rate limit: the existing `upload` policy (30 / minute / IP) on both `POST` that run the AI.

What the Farmer sees (always from the **latest SUCCESS inference**):

| Case status | `result.certainty` | `treatments` | `products` |
|---|---|---|---|
| `AI_COMPLETED` (or a 1.0 `UNDER_REVIEW`), policy passed, not Healthy | `CONFIDENT` | all active treatments of the predicted disease, by priority then title | D13, at most 5 |
| same, predicted Healthy | `CONFIDENT` | active treatments (care tips) | none |
| same, policy not passed | `UNCERTAIN`, `hint = RETAKE_PHOTO_OR_ASK_STORE` | active treatments except `CHEMICAL` | none |
| `VERIFIED` (1.0) | `VERIFIED` | stored recommendations of the current review, as in 1.0 | stored recommendations, filtered with `CatalogRules.Sellable` |
| `INCONCLUSIVE` (1.0) | `result = null`; `inconclusive` = comment + `RETAKE_PHOTO` | — | — |
| `FAILED` | `result = null`, `canRetry = true` | — | — |
| `SUBMITTED` | `result = null`, `canRetry = true` only after 2 minutes | — | — |
| `PROCESSING`, `CANCELLED` | `result = null` | — | — |

```json
// MyDiagnosisCaseResponse
{
  "id": "uuid", "caseNumber": "DG-20261110-0001", "status": "AI_COMPLETED",
  "submittedAt": "2026-11-10T03:12:00Z", "completedAt": "2026-11-10T03:12:04Z",
  "farmerNote": "Lá vàng từ chóp",
  "image": { "id": "uuid", "imageUrl": "https://…signed…", "expiresAt": "2026-11-10T04:12:00Z" },
  "result": {
    "certainty": "CONFIDENT", "hint": null,
    "disease": { "id": "uuid", "code": "LEAF_BLAST", "name": "Đạo ôn", "isHealthy": false,
                 "symptoms": "…", "prevention": "…" },
    "reviewerComment": null,
    "treatments": [ { "id": "uuid", "type": "CHEMICAL", "title": "…", "instructions": "…", "precautions": "…",
                      "rankOrder": 1, "reason": null, "activeIngredientName": "Tricyclazole" } ],
    "products": [ { "storeProductId": "uuid", "productId": "uuid", "name": "…", "sku": "…", "imageUrl": null,
                    "reason": "Chứa hoạt chất Tricyclazole", "rankOrder": 1 } ]
  },
  "inconclusive": null,
  "canRetry": false
}
```

- `MyDiagnosisCaseListItem` gains `certainty`; `diseaseCode` / `diseaseName` are the predicted disease for
  `AI_COMPLETED` (and 1.0 `UNDER_REVIEW`) and the final disease for 1.0 `VERIFIED`.
- **No probability, margin, top-k, model or policy, ever** (D8).
- Prices are the normal catalog prices (no price in this response, the app opens the product).
- `POST` flow (D4): validate the image → `IFileStorageService.UploadAsync` → transaction 1 → AI call → transaction 2.
  The response reflects the state after transaction 2: `AI_COMPLETED` with a `result`, or `FAILED` with `canRetry`.
- Cancel: allowed from SUBMITTED, PROCESSING, AI_COMPLETED, UNDER_REVIEW, FAILED (Domain `Cancel`); the apps offer it
  only while the case is waiting or FAILED.
- Disease content can be empty (it is entered by the store, §10): the apps show a fallback text.

---

## 8. F5.5 — Store API (`/api/diagnosis-cases`, read only)

| Method | Route | Permission | Body / query | Response |
|---|---|---|---|---|
| GET | `/api/diagnosis-cases` | `DIAGNOSIS.READ` | `status`, `aiPassed`, `from`, `to`, `search` (case number, Farmer name or phone), paging; sort `submittedAt` descending | `200 PagedResult<DiagnosisCaseListItem>` |
| GET | `/api/diagnosis-cases/{id}` | `DIAGNOSIS.READ` | — | `200 DiagnosisCaseResponse` |

Removed in 2.0: `start-review`, `review`, `rerun-ai`, `recommendations` (POST and DELETE).

```json
// DiagnosisCaseResponse (store view)
{
  "id": "uuid", "caseNumber": "DG-…", "status": "AI_COMPLETED", "submittedAt": "…", "completedAt": "…",
  "farmer": { "farmerProfileId": "uuid", "fullName": "…", "phoneNumber": "…" },
  "farmerNote": "…", "finalDisease": null,
  "images": [ { "id": "uuid", "imageUrl": "https://…signed…", "isPrimary": true, "fileName": "…", "uploadedAt": "…" } ],
  "inferences": [ {
      "id": "uuid", "diagnosisImageId": "uuid", "status": "SUCCESS", "inferredAt": "…", "durationMs": 83,
      "model": { "id": "uuid", "name": "agrisage-rice-classifier", "version": "0.2.0" },
      "policy": { "id": "uuid", "version": "policy-2", "minimumConfidence": 0.77, "minimumMargin": 0.20, "topK": 3 },
      "predictedClassLabel": "LEAF_BLAST", "predictedDiseaseId": "uuid",
      "confidence": 0.842113, "margin": 0.7406, "passedPolicy": true,
      "topPredictions": [ { "classLabel": "LEAF_BLAST", "diseaseId": "uuid", "confidence": 0.842113 } ]
  } ],
  "farmerResult": { "certainty": "CONFIDENT", "…": "exactly the Farmer's result block" },
  "currentReview": null,
  "reviewHistory": [],
  "recommendations": []
}
```

- `topPredictions` is cut to the policy's `topK`; the full list stays in the database.
- `currentReview`, `reviewHistory` and `recommendations` are filled only for cases decided under 1.0.
- `aiPassed` matches the latest SUCCESS inference.
- Audit actions: `DIAGNOSIS_CASE_CREATED`, `DIAGNOSIS_AI_RETRIED`, `DIAGNOSIS_AI_RESULT` (each successful AI run:
  certainty, disease code, suggested store product ids), `DIAGNOSIS_CASE_CANCELLED`, `AI_MODEL_ACTIVATED`, `AI_MODEL_RETIRED`, `AI_POLICY_ACTIVATED`, `AI_POLICY_DEACTIVATED`. The 1.0 actions
  `DIAGNOSIS_REVIEWED`, `DIAGNOSIS_RECOMMENDATION_ADDED`, `DIAGNOSIS_RECOMMENDATION_REMOVED` and
  `STAFF_AI_REVIEW_CHANGED` are no longer written.
- Notifications to the Farmer come from the committed-notification worker (`CommittedNotificationService.DiagnosisAsync`,
  entity type `DIAGNOSIS_CASE`, key `diagnosis-ai:{caseId}`): one `AI_DIAGNOSIS_COMPLETED` per case once a SUCCESS
  inference exists ("Đã có kết quả chẩn đoán …"; a variant for an `UNCERTAIN` result). `DIAGNOSIS_REVIEWED` and
  `DIAGNOSIS_RECOMMENDATIONS` are no longer sent. The use cases write none themselves.

---

## 9. F5.6 — `can_review_ai` (retired in 2.0)

`PUT /api/staff/{userId}/ai-review` and `canReviewAi` in `GET /api/auth/me` and `StaffResponse` are removed. The column
`store_members.can_review_ai` and the Domain methods `GrantAiReview` / `RevokeAiReview` stay (no migration); nothing
reads them.

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

`treatmentType`: `CULTURAL`, `CHEMICAL`, `PREVENTIVE`, `OTHER`. `activeIngredientId` must be an **active** ingredient.
Reference data is entered through these routes; it is not seeded (`SEED_REFERENCE_DATA.md`). In 2.0 this content is
what every Farmer reads directly, and a treatment's active ingredient is information only (D13 2.1; products come from the reviewed links, ADVISORY.md): enter only
content checked by someone who knows rice diseases. Products still get their ingredients through
`PUT /api/products/{id}/ingredients` (shown to the Farmer as information).

---

## 11. Permission codes

Every action needs a code in `PermissionEndpointMap.Codes` (an action without a code fails closed with `403`, and the
coverage test rejects omissions) and the code must exist in `PermissionCatalog.Entries`.

| Module | Codes in use | Default roles |
|---|---|---|
| `MY_DIAGNOSIS` | `CREATE` (create and retry), `READ`, `CANCEL` | FARMER, not delegable |
| `DIAGNOSIS` | `READ` | ADMIN, STORE_OWNER, SALES_STAFF, delegable |
| `AI_MODELS` | `READ`, `CREATE`, `ACTIVATE`, `RETIRE` | ADMIN, STORE_OWNER |
| `AI_POLICIES` | `READ`, `CREATE`, `ACTIVATE`, `DEACTIVATE` | ADMIN, STORE_OWNER |
| `DISEASES` | `READ` (+ SALES_STAFF), `UPDATE` | ADMIN, STORE_OWNER |
| `DISEASE_TREATMENTS` | `CREATE`, `UPDATE`, `ACTIVATE`, `DEACTIVATE` | ADMIN, STORE_OWNER |

**Retired codes** (kept in the catalog because it is seeded with `HasData`; marked retired in code and hidden from
`GET /api/permissions/catalog` and the member permission screens): `DIAGNOSIS.START_REVIEW`, `DIAGNOSIS.REVIEW`,
`DIAGNOSIS.RERUN_AI`, `DIAGNOSIS.RECOMMEND`, `DIAGNOSIS.UNRECOMMEND`, `STAFF.SET_AI_REVIEW`.

Controller and action names that key the map: `MeDiagnosisCases.{Create,List,Get,Cancel,Retry}`,
`DiagnosisCases.{List,Get}`, `AiModels.*`, `AiPolicies.*`, `Diseases.*`, `DiseaseTreatments.*`.

---

## 12. Configuration, migration, operations

- `appsettings.Development.json`: `AiService:BaseUrl` / `TimeoutSeconds` and `Storage:DiagnosisImageBucket`, no secrets.
  `appsettings.Local.json` (or user secrets): `AiService:ApiKey`. `Mode=Simulated` (Development only) answers with
  deterministic made-up predictions flagged `stub`; register a model whose version is `AiService:SimulatedModelVersion`
  (default `simulated`). Outside Development a `stub` answer is refused (D10).
- Migration `AiDiagnosisPermissions` (1.0, data only) is the last one for this flow; 2.0 needs none.
- Supabase: the private bucket `diagnosis-images`; the service key already configured is reused.
- Retention of diagnosis photos is not defined in the documents: until decided, photos are kept.
- Health: the backend does not call `/health` on a schedule; a failing AI Service shows up as `FAILED` cases.
- Release: the backend, the store web and the Farmer app must go out together (an old web calls removed routes; an
  old app waits forever for a reviewer).

---

## 13. Tests — must prove

| Task | Must prove |
|---|---|
| F5.1 | field mapping for a normal answer; unreachable / timeout / 5xx / bad JSON → `AiServiceUnavailableException`; unknown label, version mismatch and `stub` are FAILED inferences; `Simulated` refused outside Development; the API key is never logged |
| F5.2 | `classLabels` set equality (missing, extra, duplicate → 422); one ACTIVE model after two activations; overlapping policy → 422; activation needs an ACTIVE model; 409 on duplicates |
| F5.3 | bucket mapping for `DiagnosisImages`; signed URL only for a case's own image; oversized / wrong type → 400 |
| F5.4 | 401 / 403 (not FARMER); another Farmer's case → 404; case saved before the AI call; AI down → `FAILED`, photo kept; CONFIDENT result right in the create response with treatments and ingredient-matched products, excluding inactive, unsellable, other-store, non-ACTIVE and deleted products; UNCERTAIN result without CHEMICAL treatments and products; Healthy without products; response never contains a probability or a model name; retry: 503 records nothing, success completes the case, 422 on a completed case; 1.0 VERIFIED / INCONCLUSIVE / UNDER_REVIEW cases still readable; cancel states |
| F5.5 | read without any review right; `farmerResult` equals the Farmer's result; removed routes answer 404/405; `aiPassed` and the newest-first order |
| F5.7 | `code` and healthy flag immutable; DELETE deactivates; an inactive ingredient is refused on a treatment |
| all | every action is in `PermissionEndpointMap` (coverage test), Swagger lists the routes, retired codes hidden from the catalog route, `[RealDbFact]` end-to-end case → inference → result, rolled back; one `AI_DIAGNOSIS_COMPLETED` per case |

---

## 14. Open points for the lead

1. Retention period for diagnosis photos.
2. Disease content and the product links in agrisage-dev must be entered, and the links approved, by someone who
   knows rice diseases before a demo (the approval is the human check, ADVISORY.md V1).
3. Correct the AI master document (`AgriSage_AI_Disease_Diagnosis_Master_v2.0.md`, Agent Review and AC-03/AC-05) and
   the use-case diagrams to the 2.0 flow.

---

## 15. Versions 2.2–2.3 — crop first, general vision model (Claude or Gemini), provider settings (2026-11-20/21)

| Method | Route | Role / permission | Body / query | Response |
|---|---|---|---|---|
| GET | `/api/me/diagnosis/crops` | Farmer / `MY_DIAGNOSIS.CREATE` | — | `200 CropResponse[]`: the crops a photo can be sent for now (D16) |
| GET | `/api/diagnosis/crops` | Operate / `DISEASES.READ` | — | `200 CropResponse[]`: every catalog crop, `photoDiagnosis` tells whether it is offered |
| POST | `/api/me/diagnosis-cases` | Farmer / `MY_DIAGNOSIS.CREATE` | multipart: `image`, `note?`, **`cropType?`** | as before; `400` unknown crop, `422` crop not offered |
| GET | `/api/me/diagnosis-cases`, `/api/me/diagnosis-cases/{id}` | Farmer | as before | list items and the case add **`cropType`, `cropName`** (the crop chosen before the photo; RICE / Lúa for older cases) |
| POST | `/api/diseases` | Manage / `DISEASES.CREATE` | adds **`cropType?`** (default RICE, catalog code) | `400` unknown crop |
| GET | `/api/admin/ai-providers` | **Admin** / `AI_PROVIDER.READ` | — | `200 AiProviderSettingsResponse[]`: ANTHROPIC then GOOGLE, saved or defaults (`version: 0`) |
| GET | `/api/admin/ai-providers/{provider}` | **Admin** / `AI_PROVIDER.READ` | provider `ANTHROPIC` \| `GOOGLE` (any case) | `200 AiProviderSettingsResponse`; `404` other provider |
| PUT | `/api/admin/ai-providers/{provider}` | **Admin** / `AI_PROVIDER.MANAGE` | `{ modelId, isEnabled, timeoutSeconds, version, apiKey?, clearApiKey? }` | `200`; `409` stale version; `422` on without a key or no master key; `400` key and clear together; `404` other provider |
| POST | `/api/admin/ai-providers/{provider}/test` | **Admin** / `AI_PROVIDER.MANAGE` | — | `200 { ok, message }`: the stored key and model are checked with that provider's models API (no content generated, no cost) |

```json
CropResponse { "code": "DURIAN", "name": "Sầu riêng", "diseaseCount": 2, "photoDiagnosis": true,
               "diseases": [ { "code": "DURIAN_PHYTOPHTHORA", "name": "Thối gốc chảy nhựa" } ] }
AiProviderSettingsResponse { "provider": "ANTHROPIC", "modelId": "claude-opus-5-5", "isEnabled": true, "timeoutSeconds": 60,
  "hasApiKey": true, "apiKeyHint": "WXYZ", "encryptionConfigured": true,
  "suggestedModels": ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"], "updatedAt": "…", "version": 1 }
// GOOGLE starts on "gemini-3.8-flash", suggests ["gemini-3.8-flash", "gemini-3.6-flash", "gemini-3.5-flash-lite"]; the admin may type another id.
```

- `AiPredictionRequest` adds `Framework`, `CropType`, `CropName` and `Candidates (code, name, symptoms, isHealthy)`. The
  `RoutingDiagnosisClient` sends a model of framework `ANTHROPIC` to `ClaudeDiagnosisClient`, `GOOGLE` to
  `GeminiDiagnosisClient`, and every other model to the FastAPI client as before.
- Both general clients use the same prompt and answer rules (`GeneralModelPrompt`): the photo and the case text (crop,
  candidate codes with names and symptoms, NONE) go with a fixed system prompt; the answer is constrained by a JSON schema
  (`label` ∈ candidates + NONE, `confidence`, `alternatives`, `reason`). `model_version` = the configured model id, so the
  ACTIVE model's `version` must equal it (D10). Any failure (no key, provider off, network, rate limit, refusal or block,
  answer outside the schema) is `AiServiceUnavailableException`: the case is FAILED and the Farmer retries (D14). The key is
  never logged.
- `ClaudeDiagnosisClient`: official Anthropic SDK, beta Messages endpoint, effort `medium`; for Opus/Sonnet 5.5 the
  server-side refusal fallback (`fallbacks: "default"`) is on.
- `GeminiDiagnosisClient`: Gemini API REST `POST v1beta/models/{model}:generateContent` on
  `generativelanguage.googleapis.com`, key in the `x-goog-api-key` header only (never in the URL), `systemInstruction`, the
  photo as `inlineData`, `generationConfig.responseMimeType = application/json` + `responseJsonSchema`. No `temperature`
  (ignored by newer Gemini models and announced to become an error). Thought parts are skipped; a `promptFeedback.blockReason`
  fails the case. Logs keep the HTTP status and the error status/reason, never the error message. The connection check is
  `GET v1beta/models/{model}` (must support `generateContent`).
- Gemini free tier (Google AI Studio key without billing): the Gemini API terms let Google use submitted content to improve
  its products, human reviewers may read it, and personal data must not be sent. Use a key of a project with billing
  (paid service) when Farmers' real photos are sent; the admin page says so on the Gemini tab.
- Turning a provider on: the admin saves its key and model (`PUT`), checks them (`POST …/test`), registers a model of
  framework `ANTHROPIC` or `GOOGLE` whose `version` is that provider's model id and whose labels are the active diseases to
  answer (+ HEALTHY), adds a policy with thresholds from the evaluation and activates it (the previous model is retired, D5).
- Configuration: `SecretEncryption:Key` = 32 random bytes in base64 (User Secrets or environment); without it no key can
  be stored and a stored key cannot be read. Changing it makes the stored key unreadable: enter the key again.
- Migration `20261120145730_AiProviderSettings`: table `ai_provider_settings` and the admin-only permissions
  `AI_PROVIDER.READ` / `AI_PROVIDER.MANAGE` (not delegable).
- Tests: `CropAndAiProviderTests` (unit), `ClaudeDiagnosisClientTests` (shared prompt rules) + `AesGcmSecretProtectorTests`,
  `GeminiDiagnosisClientTests` (request shape, key only in the header, every failure unavailable, connection check),
  `AiProviderEndToEndTests` (admin only, one setting per provider, key never returned or audited, version, clear, 404), and
  `DiagnosisToAdvisoryEndToEndTests.A_crop_chosen_before_the_photo_narrows_a_general_model_to_that_crops_diseases`.
