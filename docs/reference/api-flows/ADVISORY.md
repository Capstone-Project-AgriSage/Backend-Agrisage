# Advisory — from a problem (known or diagnosed) to products, quantity and the cart

Version 1.0 — 2026-11-02. User-requested extension after Review 2 (the lecturers asked the team to go deeper on one
part: input consultation). Conventions, shared shapes and ownership rules: [README.md](README.md). Authorization of
every route below follows [DYNAMIC_PERMISSIONS.md](DYNAMIC_PERMISSIONS.md). The AI photo diagnosis
([AI_DIAGNOSIS.md](AI_DIAGNOSIS.md)) is one of the two ways into this flow; its model is out of scope here.

A Farmer knows the problem of the rice field ("đạo ôn", "vàng lá", "rầy nâu") or comes from an AI diagnosis. The
backend answers with the products the store sells for that problem: crop-protection products with the registration
that allows them on rice, fertilizers with the reason, the dose, the quantity for the Farmer's area and the estimated
amount, whether the stock covers it, and the packagings ready to go into the cart. Ordering, payment and delivery
are the existing flows.

Why: the first version matched products to a disease by **active ingredient** (AI_DIAGNOSIS.md D13). Checked against
Annex I of Thông tư 75/2025/TT-BNNMT, an ingredient match is not a registration: products with the same ingredient
were offered for a disease they are not registered for. The link between a problem and a product is now an explicit,
reviewed row with its source.

Sources: `DATABASE_DESIGN.md` §14, §55–56 and the new section XXIV-A (Advisory tables); `BUSINESS_RULES.md` rules 37 and 62–66;
Annex I of [Thông tư 75/2025/TT-BNNMT](https://datafiles.chinhphu.vn/cpp/files/vbpq/2025/12/75-bnnmt-pl1.pdf), amended
by Thông tư 28/2026/TT-BNNMT (effective 2026-08-15).

---

## 1. Demo script

| # | Step (screen) | API | Effect |
|---|---|---|---|
| 1 | Owner creates the problems the store advises on (or edits the five seeded ones), with keywords | `POST /api/diseases`, `PUT /api/diseases/{id}` | problems with `problemType` and `searchKeywords` |
| 2 | Owner links products to a problem with dose, registration source, pre-harvest days | `POST /api/diseases/{id}/products` | link in `DRAFT` |
| 3 | A person who knows rice reviews the links and approves them | `POST /api/disease-products/{id}/approve` | link `APPROVED` (visible to Farmers) |
| 4 | Farmer types "dao on" and picks the problem | `GET /api/me/advisory/problems?search=dao on` | ranked problems, accents ignored |
| 5 | Farmer enters the field area and the growth stage | `POST /api/me/advisory/recommendations` | protection and nutrition products with quantity, amount, stock status |
| 6 | Farmer adds a product to the cart (one tap per product) | `POST /api/me/cart/items` per returned packaging line | cart; checkout, payment, delivery as before |
| 7 | AI diagnosis `CONFIDENT` leads to the same screen | `GET /api/me/diagnosis-cases/{id}`, then step 5 with `diagnosisCaseId` | products come from the same links |
| 8 | Store staff follow the advice given and whether it led to a purchase | `GET /api/advisory/sessions` | sessions with `boughtWithin7Days` |

---

## 2. Tasks

| Task | Content | Depends on |
|---|---|---|
| A1 | Migration: `diseases` columns, `disease_products`, `advisory_sessions`, `advisory_session_items`, permission rows | — |
| A2 | Domain: `DiseaseProduct`, `DosePlanner`, `ProblemSearch` (pure, unit-tested) | — |
| A3 | `SellableStock` helper (extracted from `OrderBuilder`) and `AdvisoryEngine` | A1, A2 |
| A4 | Farmer API (`/api/me/advisory`) | A3 |
| A5 | Staff API: problems (create, activate), links, approval | A1, A2 |
| A6 | Staff API: advisory sessions | A4 |
| A7 | AI diagnosis uses the links instead of the ingredient match (D13 replaced) | A3 |
| A8 | Data: links from Annex I, doses with sources, review and approval | A5 |

---

## 3. Decisions

| # | Decision |
|---|---|
| V1 | **A link is explicit and reviewed.** `disease_products` rows are `DRAFT`, `APPROVED` or `INACTIVE`. Only `APPROVED` rows reach a Farmer. Changing the content of an `APPROVED` row (dose, source, group, product) returns it to `DRAFT`. Approval needs its own permission (`DISEASE_PRODUCTS.APPROVE`) and records who and when. |
| V2 | **The model never names a product.** Free text and photos only select a problem; the products come from the links. Text search is plain matching (V3), not an LLM. |
| V3 | **Problem search** normalizes accents (`dao on` = `đạo ôn`, `đ` = `d`), lower case, splits on spaces; every word must appear in the name, the `search_keywords` or the symptoms. Rank: all words in the name, then in the keywords, then in the symptoms; ties by name. The five seeded classes and new problems are searched the same way; the Healthy class is never listed. |
| V4 | **One problem table.** `diseases` stays the catalog of problems (AI classes included). `problem_type` is `DISEASE`, `PEST`, `WEED`, `NUTRITION` or `GROWTH`. The AI looks a class up by `code`, so adding a problem changes nothing for it. The five seeded rows are never deactivated and keep their code and healthy flag. |
| V5 | **Two groups.** `PROTECTION` (pesticides, herbicides: `registration_source` required, e.g. `TT 75/2025/TT-BNNMT, Phụ lục I, tr. 246`) and `NUTRITION` (fertilizers and soil products: `reason` required, they are not in the official list). Both need a `dose_text`. |
| V6 | **Quantity** = `ceil(dose_per_ha_base × applications × area_m² / 10 000)` in **base units** of the product. The packagings are chosen from the ACTIVE sale packagings that have a price: largest first as many whole packagings as fit, the remainder in the next smaller ones, the last remainder rounded up in the smallest. Without an area, a `dose_per_ha_base`, or a priced sale packaging, `plan` is `null` and the product is still shown with its `doseText`. |
| V7 | **Prices and stock come from the existing sources.** Prices: `IPriceResolver` (group of the Farmer, walk-in fallback). Products: `CatalogRules.Sellable`. Stock: the sellable quantity of ACTIVE, unexpired lots (on hand minus reserved), the same formula as the credit-order check, shared in `SellableStock`. The response gives a status only (`ENOUGH`, `NOT_ENOUGH`, `OUT_OF_STOCK`), never a quantity. |
| V8 | **Order of the products** inside a group: stock status (`OUT_OF_STOCK` last), then `priority` (higher first), then estimated amount, then name; at most 5 per group. |
| V9 | **Growth stage** filters links: a link with `growth_stages` is offered only when the request has no stage or the stage is in the list; a link without `growth_stages` fits every stage. Stages: `SEEDLING`, `TILLERING`, `BOOTING`, `HEADING`, `RIPENING`. |
| V10 | **Area is sent in square metres** (`areaSqm`, `> 0`, at most 1 000 000). The clients convert from hectare (10 000), công (1 000) or sào Bắc Bộ (360). |
| V11 | **Sessions are a log, not a cart.** Each recommendation is recorded in `advisory_sessions` with its items for tracing and statistics. A call by the same Farmer for the same problem within 30 minutes updates the latest session (area, stage; items replaced) instead of creating a new one. |
| V12 | **Advice needs a sign-in** (the Farmer's group decides the price). The public catalog stays anonymous. |
| V13 | **AI integration (replaces AI_DIAGNOSIS.md D13).** For a `CONFIDENT`, non-Healthy result the suggested products are the `PROTECTION` links of the predicted problem that pass V7 (at most 5, V8 order); `reason` is the text of the link. `UNCERTAIN` and Healthy show none (unchanged). `disease_treatments.active_ingredient_id` remains as information ("Hoạt chất") and no longer selects products. Cases decided under 1.0 (`VERIFIED`) keep their stored recommendations. |
| V14 | **Safety text** is always returned with a result: read the label, wear protective clothing, respect the pre-harvest interval, ask the store when unsure. |

---

## 4. Schema (A1) — one migration, reviewed, added to `InitialCreateMigrationTests` with its own review test

`diseases` — two columns:

```text
problem_type     varchar(30)   NOT NULL DEFAULT 'DISEASE'
                 CHECK (problem_type IN ('DISEASE','PEST','WEED','NUTRITION','GROWTH'))
search_keywords  varchar(1000) NULL
```

`disease_products` (soft-deletable like the other tables: `created_at`, `updated_at`, `deleted_at`, `deleted_by`; no `version` column, which only the four concurrency tables have):

```text
id                    uuid PK
disease_id            uuid NOT NULL FK diseases.id
product_id            uuid NOT NULL FK products.id
recommendation_group  varchar(20)  NOT NULL  CHECK IN ('PROTECTION','NUTRITION')
registration_source   varchar(200) NULL      CHECK (recommendation_group <> 'PROTECTION' OR registration_source IS NOT NULL)
reason                varchar(300) NULL      CHECK (recommendation_group <> 'NUTRITION'  OR reason IS NOT NULL)
dose_text             varchar(500) NOT NULL
dose_source           varchar(200) NULL
dose_per_ha_base      numeric(12,3) NULL     CHECK (dose_per_ha_base > 0)
applications          smallint     NOT NULL DEFAULT 1  CHECK (applications BETWEEN 1 AND 10)
pre_harvest_days      integer      NULL     CHECK (pre_harvest_days >= 0)
growth_stages         varchar(100) NULL     -- comma separated stage codes
priority              integer      NOT NULL DEFAULT 0
status                varchar(20)  NOT NULL DEFAULT 'DRAFT'  CHECK IN ('DRAFT','APPROVED','INACTIVE')
approved_by           uuid NULL FK users.id
approved_at           timestamptz NULL
CHECK (status <> 'APPROVED' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL))
UNIQUE (disease_id, product_id) WHERE deleted_at IS NULL
INDEX  (disease_id, status)
```

`advisory_sessions`:

```text
id                    uuid PK
store_id              uuid NOT NULL FK stores.id
farmer_profile_id     uuid NOT NULL FK farmer_profiles.id
disease_id            uuid NOT NULL FK diseases.id
source                varchar(20) NOT NULL CHECK IN ('TEXT','DIAGNOSIS_CASE')
diagnosis_case_id     uuid NULL FK diagnosis_cases.id
query_text            varchar(200) NULL
area_sqm              numeric(12,1) NULL CHECK (area_sqm > 0)
growth_stage          varchar(20) NULL
INDEX (store_id, created_at DESC), INDEX (farmer_profile_id, disease_id, created_at DESC)
```

`advisory_session_items`: `id`, `advisory_session_id` FK, `store_product_id` FK, `disease_product_id` FK, `rank_order int`,
`suggested_base_quantity bigint NULL`, `estimated_amount numeric(14,2) NULL`.

Business FKs are `Restrict`. Nothing here changes stock or debt, so no stock movement or debt transaction is involved.
The migration also carries the permission rows (§8) as data (`HasData`), like `AiDiagnosisPermissions`.

---

## 5. A4 — Farmer API (`/api/me/advisory`)

`GET /api/me/advisory/problems?search=&type=&page=&pageSize=` → `PagedResult<AdvisoryProblem>`:

```json
{ "id": "…", "code": "LEAF_BLAST", "name": "Bệnh đạo ôn lá", "problemType": "DISEASE",
  "summary": "Vết hình thoi, tâm xám trắng, viền nâu đỏ", "matchedBy": "NAME" }
```

`matchedBy` is `NAME`, `KEYWORD`, `SYMPTOM` or `null` (no search). Only active, non-Healthy problems.

`POST /api/me/advisory/recommendations`:

```json
{ "diseaseId": "…", "areaSqm": 3000, "growthStage": "TILLERING", "diagnosisCaseId": null, "queryText": "dao on" }
```

`diagnosisCaseId` (optional) must be the Farmer's own case, else `404`. Response `200`:

```json
{
  "sessionId": "…",
  "problem": { "id": "…", "code": "SHEATH_BLIGHT", "name": "Bệnh khô vằn", "problemType": "DISEASE",
               "description": "…", "symptoms": "…", "causes": "…", "prevention": "…" },
  "careGuidance": [ { "id": "…", "type": "CULTURAL", "title": "…", "instructions": "…", "precautions": null, "rankOrder": 1 } ],
  "areaSqm": 3000, "growthStage": "TILLERING",
  "protection": [ AdvisoryProduct ], "nutrition": [ AdvisoryProduct ],
  "emptyReason": null,
  "safetyNote": "Đọc kỹ nhãn trước khi dùng, mang đồ bảo hộ, tuân thủ thời gian cách ly. Gợi ý chỉ mang tính tham khảo; hãy hỏi cửa hàng nếu chưa chắc."
}
```

`AdvisoryProduct`:

```json
{ "storeProductId": "…", "productId": "…", "name": "Anvil 5SC - thuốc đặc trị khô vằn", "sku": "ANVIL-5SC-1L",
  "imageUrl": null, "group": "PROTECTION", "rankOrder": 1,
  "reason": "Đăng ký trị khô vằn trên lúa", "registrationSource": "TT 75/2025/TT-BNNMT, Phụ lục I, tr. 205",
  "doseText": "…", "applications": 2, "preHarvestDays": 14, "growthStages": ["BOOTING", "HEADING"],
  "stockStatus": "ENOUGH",
  "plan": { "baseQuantity": 1, "totalAmount": 135000,
            "lines": [ { "productPackagingId": "…", "packagingName": "Chai 1 lít", "quantity": 1,
                         "unitPrice": 135000, "lineAmount": 135000 } ] } }
```

`plan` is `null` under V6. `careGuidance` holds the active `CULTURAL`, `PREVENTIVE` and `OTHER` treatments; `CHEMICAL`
treatments are not shown here (their products are the links). `emptyReason` is `NO_APPROVED_PRODUCT` when both lists
are empty for lack of an approved link, `NOTHING_IN_STORE` when approved links exist but none is sellable, else `null`.

Errors: `400` (area out of range, unknown stage), `404` (unknown or inactive problem, another Farmer's case),
`422` (the Healthy class: nothing to advise).

Adding to the cart uses the existing `POST /api/me/cart/items` (`storeProductId`, `productPackagingId`, `quantity`)
once per line of `plan.lines`; no new cart route.

---

## 6. A5 — Staff API: problems and links

| Route | Permission | Notes |
|---|---|---|
| `POST /api/diseases` | `DISEASES.CREATE` | `{code, name, problemType, scientificName?, description?, symptoms?, causes?, prevention?, searchKeywords?}`; `code` matches `^[A-Z][A-Z0-9_]{1,48}$`, `409` if taken; never a healthy class |
| `PUT /api/diseases/{id}` | `DISEASES.UPDATE` | existing route; gains `searchKeywords`; `code`, `problemType` and the healthy flag never change |
| `POST /api/diseases/{id}/activate`, `…/deactivate` | `DISEASES.UPDATE` | the five seeded classes cannot be deactivated (`422`) |
| `GET /api/diseases/{id}/products` | `DISEASE_PRODUCTS.READ` | every status |
| `POST /api/diseases/{id}/products` | `DISEASE_PRODUCTS.CREATE` | `{productId, recommendationGroup, registrationSource?, reason?, doseText, doseSource?, dosePerHaBase?, applications?, preHarvestDays?, growthStages?, priority?}` → `201`, status `DRAFT`; `409` if the product is already linked |
| `PUT /api/disease-products/{id}` | `DISEASE_PRODUCTS.UPDATE` | same fields without `productId`; an `APPROVED` link returns to `DRAFT` |
| `POST /api/disease-products/{id}/approve` | `DISEASE_PRODUCTS.APPROVE` | `DRAFT` or `INACTIVE` → `APPROVED`; `422` while a required field is missing |
| `DELETE /api/disease-products/{id}` | `DISEASE_PRODUCTS.DEACTIVATE` | → `INACTIVE` (semantic delete) |

Every write records an audit entry (`DISEASE_PRODUCT_CREATED`, `_UPDATED`, `_APPROVED`, `_DEACTIVATED`) with before and
after values. A product does not need an active ingredient to be linked: the ingredients of a product are information
only (V13).

---

## 7. A6 — Staff API: sessions

`GET /api/advisory/sessions?from=&to=&diseaseId=&page=&pageSize=` (`ADVISORY_SESSIONS.READ`) → `PagedResult` of
`{id, createdAt, farmer{id, name, phone}, problem{id, name}, source, areaSqm, growthStage, items[{name, group}], boughtWithin7Days}`.
`boughtWithin7Days` is true when the same Farmer has an order created from the session time up to 7 days later with
one of the session's store products. Read-only, `AsNoTracking`, SQL-side projection.

---

## 8. Permission codes

Each action needs a code in `PermissionEndpointMap.Codes` and in `PermissionCatalog.Entries` (else `403`).

| Module | Codes | Default roles |
|---|---|---|
| `MY_ADVISORY` | `READ` | FARMER, not delegable |
| `DISEASES` | `CREATE` (new), `READ`, `UPDATE` | `CREATE`, `UPDATE`: ADMIN, STORE_OWNER; `READ`: + SALES_STAFF |
| `DISEASE_PRODUCTS` | `READ`, `CREATE`, `UPDATE`, `APPROVE`, `DEACTIVATE` | `READ`: ADMIN, STORE_OWNER, SALES_STAFF; the rest ADMIN, STORE_OWNER |
| `ADVISORY_SESSIONS` | `READ` | ADMIN, STORE_OWNER, SALES_STAFF |

Controller and action names that key the map: `MeAdvisory.{Problems,Recommend}`, `Diseases.{Create,Activate,Deactivate}`,
`DiseaseProducts.{List,Create,Update,Approve,Delete}`, `AdvisorySessions.List`.

---

## 9. Configuration, migration, operations

- One data and schema migration (A1). Announce it to the lead and the team first; one migration at a time.
- No configuration key. No background job.
- Shipping order: backend with the migration and the approved links together, then the apps. Until a link is
  approved, an AI `CONFIDENT` result shows no product (empty state) and the advisory screen shows `NO_APPROVED_PRODUCT`.
- Existing data: A8 loads the links as `DRAFT`. Nothing reaches a Farmer before a person approves it.

---

## 10. Tests — must prove

| Task | Must prove |
|---|---|
| A2 | `DosePlanner`: 3 000 m² of 250 kg/ha with bag 50 kg and kg → 1 bag + 25 kg; `applications` multiply; rounding up never under-supplies; no priced sale packaging or no dose → no plan. `ProblemSearch`: `dao on`, `dạo ôn`, `Đạo` and `vang la` find the right problems; ranking name > keyword > symptom; Healthy never listed. `DiseaseProduct`: PROTECTION without source and NUTRITION without reason rejected; editing an `APPROVED` link returns it to `DRAFT` |
| A3 | `DRAFT` and `INACTIVE` links, products not sellable or discontinued, other stores: never offered; stage filter; group price used; the three stock statuses; same status formula as the credit-order check (shared helper); at most 5 per group; order V8 |
| A4 | 401/403 (not FARMER); another Farmer's case → 404; Healthy → 422; session written and updated within 30 minutes; area out of range → 400; response never has a stock quantity |
| A5 | create with duplicate code → 409; seeded class cannot be deactivated; link create/update/approve/delete and their audit entries; approval needs the approve permission; sales staff can read but not change |
| A6 | `boughtWithin7Days` true only for the same Farmer, in the window, with a session product |
| A7 | `CONFIDENT` case lists the approved protection links; ingredient-matched products with no link are not offered; `UNCERTAIN` and Healthy unchanged; `VERIFIED` (1.0) unchanged; `DIAGNOSIS_AI_RESULT` audit still records the suggested ids |
| all | every action in `PermissionEndpointMap` (coverage test), Swagger lists the routes, `[RealDbFact]` end to end: link → approve → advice → cart → order; rolled back |

---

## 11. Open points for the lead

1. Who reviews and approves the links and doses (a person who knows rice; the lecturers or a plant-protection
   officer). Nothing is shown to Farmers without this.
2. Doses and pre-harvest intervals are not in the official list; they come from the product label or the maker's
   public page, with the source in `dose_source`. Fertilizer recommendations have no official list either.
3. Annex I changes with each circular; `registration_source` names the text and page, and the links need a re-check
   when a new circular is published.
4. Only rice and about fifteen problems in this phase.
