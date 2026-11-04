# CAP-NTF-005 — Resend an email

- Capability: CAP-NTF-005
- Canonical spec: `capabilities/notification/features/email-send-log.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/ResendEmail/`

## Contract
Implemented **verbatim** from the issued contract — if it looks wrong, report drift upstream
(`skills-spec-authoring`), never deviate locally.

- Operation: `resendEmail` in `contracts/notification/send-log-entry.yaml`
- Route / verb: `POST /internal/sendLogEntries/{sendLogEntryId}/resend` (the contract's route). The
  identifier is bound as text, not with a `:guid` route constraint, so a malformed one is a 400
  rather than a 404.
- Request: path `sendLogEntryId` (uuid); no body
- Response: 200 `SendLogEntry` — the new entry, in status sent
- Credential: `X-Internal-Key` (the `InternalKey` security scheme)
- Problem codes: `VALIDATION_FAILED` (400), `UNAUTHORIZED` (401), `NOT_FOUND` (404),
  `RESEND_NOT_POSSIBLE` (422, `failureReason`), `RESEND_LINK_EXPIRED` (422, `linkExpiredAt`),
  `RESEND_BOOKING_CANCELLED` (422, `bookingId`), `RESEND_TRIP_DEPARTED` (422, `departsAt`),
  `ADDRESS_SUPPRESSED` (422, `toAddress`, `suppressionReason`),
  `EMAIL_REFUSED` (422, `sendLogEntryId`, `providerReason`), `UPSTREAM_UNAVAILABLE` (502,
  `retryable`, `sendLogEntryId`)

## Acceptance criteria
Each criterion maps to one test in `ResendEmail.Tests.cs`.

- [x] AC-6: internal key required (resend half) — `ResendEmail_InternalKeyMissingOrWrong_ReturnsUnauthorizedAndSendsNothing`
- [x] AC-7: resend a Failed email — `ResendEmail_FailedEntry_SendsSameEmailToSameRecipient`, `ResendEmail_FailedEntry_ReturnsNewSentEntryAndLeavesOriginalUnchanged`
- [x] AC-8: resend a Sent email — `ResendEmail_SentEntry_SendsAgainAndRecordsNewSentEntry`
- [x] AC-9: verification email inside the link lifetime — `ResendEmail_VerificationEmailInsideLinkLifetime_SendsSameLink`
- [x] AC-10: verification link expired — `ResendEmail_VerificationLinkExpired_ReturnsLinkExpiredAndSendsNothing`
- [x] AC-11: nothing to send — `ResendEmail_EntryWithNoRecipient_ReturnsResendNotPossibleAndSendsNothing`
- [x] AC-12: unknown entry — `ResendEmail_UnknownEntry_ReturnsNotFound`
- [x] AC-13: malformed entry identifier — `ResendEmail_MalformedEntryIdentifier_ReturnsValidationProblem`
- [x] AC-14: email service unavailable — `ResendEmail_EmailServiceUnavailable_RecordsFailedEntryAndReturnsUpstreamUnavailable`
- [x] AC-15: email service refuses — `ResendEmail_EmailServiceRefuses_RecordsFailedEntryAndReturnsEmailRefused`
- [x] Rule 9: a booking email of a known cancelled booking is refused; the cancellation and refund emails are not — `ResendEmail_BookingEmailOfAKnownCancelledBooking_ReturnsBookingCancelledAndSendsNothing`, `ResendEmail_CancellationOrRefundEmailOfACancelledBooking_IsResent`
- [x] Rule 10: a trip email after its departure is refused — `ResendEmail_TripEmailAfterDeparture_ReturnsTripDeparted`, `ResendEmail_CancellationEmailAfterDeparture_IsStillResent`
- [x] Rule 11: an address on the do-not-send list is refused, and a skipped entry is resent once it is removed — `ResendEmail_AddressOnDoNotSendList_ReturnsAddressSuppressedWithTheReason`, `ResendEmail_SkippedEntryAfterTheAddressWasRemoved_IsSentAndKeepsTheBookingDetails`
- [x] Rule 12: the first refusal in the spec's order is the one returned — `ResendEmail_CancelledAndDepartedAndSuppressed_ReportsTheCancellationFirst`
- [x] AC-16: resending a resend — `ResendEmail_EntryThatIsItselfAResend_RecordsNewEntryPointingAtIt`

## Invariants touched
- The new entry is copied from the resent one inside the database (recipient, subject, both
  message versions, event ID and type, email type, event time, correlation ID); the resent entry
  is never updated. `resend_of` being set keeps it outside the email workers' one-entry-per-event
  index.
- Verification link lifetime: setting `VerificationLinkLifetimeHours`, default 24, must match
  Identity's. A customer or operator verification email is refused once occurred time + lifetime
  is not after now.
- Sent once, immediately; a failure is recorded as Failed and never retried automatically.

## Decisions awaiting the developer's confirmation
- The internal key is the plain-text secret `routego/internal-key` (shared with the Portal).
- The resend's own log lines carry the original event's correlation ID; a caller-supplied
  correlation ID on the request is not read yet.
