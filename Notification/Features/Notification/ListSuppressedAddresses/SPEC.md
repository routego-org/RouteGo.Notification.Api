# CAP-NTF-015 — List the do-not-send list

- Capability: CAP-NTF-015
- Canonical spec: `capabilities/notification/features/do-not-send-list.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/ListSuppressedAddresses/`

## Contract
Implemented **verbatim** from the issued contract — if it looks wrong, report drift upstream
(`skills-spec-authoring`), never deviate locally.

- Operation: `listSuppressedAddresses` in `contracts/notification/suppressed-address.yaml`
- Route / verb: `GET /internal/suppressedAddresses`
- Request: query `emailAddress` (optional, email, ≤ 254), `page` (≥ 1, default 1), `pageSize` (1–200, default 20)
- Response: 200 `SuppressedAddressListResponse` — `items`, `totalCount`, `page`, `pageSize`
- Credential: `X-Internal-Key` (the `InternalKey` security scheme), checked by
  `Infrastructure/InternalKeyAuthenticationHandler`
- Problem codes: `VALIDATION_FAILED` (400, identified by `errors`), `UNAUTHORIZED` (401)

## Acceptance criteria
Each criterion maps to tests in `ListSuppressedAddresses.Tests.cs`.

- [x] AC-7: list the whole list, newest first — `ListSuppressedAddresses_TwoAddresses_ReturnsBothNewestFirstWithEveryField`; `ListSuppressedAddresses_Page2WithPageSize1_ReturnsTheOlderEntry`
- [x] AC-8: look up one address, case-insensitively — `ListSuppressedAddresses_AddressInDifferentCase_ReturnsExactlyThatEntry`; `ListSuppressedAddresses_AddressNotOnTheList_ReturnsAnEmptyPage`
- [x] AC-9: malformed address filter — `ListSuppressedAddresses_MalformedAddress_ReturnsValidationProblemNamingTheAddress`; `ListSuppressedAddresses_PageSizeOver200_ReturnsValidationProblemNamingPageSize`
- [x] AC-14: internal key required (list half) — `ListSuppressedAddresses_InternalKeyMissingOrWrong_ReturnsUnauthorizedWithNothing`

## Invariants touched

- Addresses are compared case-insensitively; the list's unique index on `lower(email_address)` (migration V0006) serves the lookup.

## Decisions awaiting the developer's confirmation
- The internal key is the plain-text secret `routego/internal-key` (shared with the Portal).
