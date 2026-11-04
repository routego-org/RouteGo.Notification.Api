# CAP-NTF-004 — Search the send log

- Capability: CAP-NTF-004
- Canonical spec: `capabilities/notification/features/email-send-log.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SearchSendLog/`

## Contract
Implemented **verbatim** from the issued contract — if it looks wrong, report drift upstream
(`skills-spec-authoring`), never deviate locally.

- Operation: `searchSendLog` in `contracts/notification/send-log-entry.yaml`
- Route / verb: `GET /internal/sendLogEntries` (the contract's route, not the default action-scoped one)
- Request: query `toAddress` (required, email, ≤ 254), `page` (≥ 1, default 1), `pageSize` (1–200, default 20)
- Response: 200 `SendLogEntryListResponse` — `items`, `totalCount`, `page`, `pageSize`; no message body. Since v0.2.0 each entry also carries `bookingId`, `departsAt` and `skipReason`, and status may be `skipped`
- Credential: `X-Internal-Key` (the `InternalKey` security scheme), checked by
  `Infrastructure/InternalKeyAuthenticationHandler`
- Problem codes: `VALIDATION_FAILED` (400, identified by `errors` as the shared contract specifies),
  `UNAUTHORIZED` (401)

## Acceptance criteria
Each criterion maps to one test in `SearchSendLog.Tests.cs`.

- [x] AC-1: every attempt to the address, newest first — `SearchSendLog_TwoAttemptsToAddress_ReturnsOnlyThoseNewestFirst`, `SearchSendLog_TwoAttemptsToAddress_ShowsEveryRecordedFieldButNotTheMessage`
- [x] AC-2: address matched case-insensitively — `SearchSendLog_AddressInDifferentCase_ReturnsTheEntry`
- [x] AC-3: no emails for the address — `SearchSendLog_NoEmailEverToAddress_ReturnsEmptyPage`
- [x] AC-4: paging — `SearchSendLog_Page3Of45Entries_ReturnsTheOldestFive`
- [x] AC-5: address missing or malformed — `SearchSendLog_AddressMissingOrMalformed_ReturnsValidationProblemNamingAddress`
- [x] AC-6: internal key required (search half) — `SearchSendLog_InternalKeyMissingOrWrong_ReturnsUnauthorizedWithNoEntries`
- Business rule 1 (at most 200 per page) — `SearchSendLog_PageSizeOver200_ReturnsValidationProblemNamingPageSize`

## Invariants touched
- Emails are compared case-insensitively: the search compares `lower(to_address)`, served by the
  index `ix_send_log_entries_lower_to_address_attempted_at` (migration V0004).
- Entries with no recipient cannot be found by address (they have none to match).
