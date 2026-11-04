# CAP-NTF-016 — Remove an address from the do-not-send list

- Capability: CAP-NTF-016
- Canonical spec: `capabilities/notification/features/do-not-send-list.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/RemoveSuppressedAddress/`

## Contract
Implemented **verbatim** from the issued contract — if it looks wrong, report drift upstream
(`skills-spec-authoring`), never deviate locally.

- Operation: `removeSuppressedAddress` in `contracts/notification/suppressed-address.yaml`
- Route / verb: `DELETE /internal/suppressedAddresses/{suppressedAddressId}`; the identifier is bound as text, not with a `:guid` route constraint, so a malformed one is a 400 rather than a 404
- Request: path `suppressedAddressId` (uuid); no body
- Response: 204, no body
- Credential: `X-Internal-Key` (the `InternalKey` security scheme), checked by
  `Infrastructure/InternalKeyAuthenticationHandler`
- Problem codes: `VALIDATION_FAILED` (400), `UNAUTHORIZED` (401), `NOT_FOUND` (404)

## Acceptance criteria
Each criterion maps to tests in `RemoveSuppressedAddress.Tests.cs`.

- [x] AC-10: remove an address; emails flow again — `RemoveSuppressedAddress_ExistingEntry_ReturnsNoContentAndTheAddressIsEmailedAgain`; `RemoveSuppressedAddress_OnlyThatEntry_LeavesTheOthers`; the resend of a skipped email afterwards is `ResendEmail_SkippedEntryAfterTheAddressWasRemoved_IsSentAndKeepsTheBookingDetails`
- [x] AC-11: unknown or already removed entry — `RemoveSuppressedAddress_UnknownOrAlreadyRemovedEntry_ReturnsNotFound`
- [x] AC-12: malformed entry identifier — `RemoveSuppressedAddress_MalformedIdentifier_ReturnsValidationProblem`
- [x] AC-14: internal key required (remove half) — `RemoveSuppressedAddress_InternalKeyMissingOrWrong_ReturnsUnauthorizedAndChangesNothing`

## Invariants touched

- Removing deletes the entry at once and resends nothing; a later report adds the address again as a new entry.

## Decisions awaiting the developer's confirmation
- The internal key is the plain-text secret `routego/internal-key` (shared with the Portal).
