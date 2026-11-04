# CAP-NTF-003 — Send operator rejected email

- Capability: CAP-NTF-003
- Canonical spec: `capabilities/notification/features/operator-rejected-email.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SendOperatorRejectedEmail/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `topic: operator-rejected`, received from SQS
  `notification-q`; `NotificationEventDispatcher` hands each `OperatorRejected` event to
  `SendOperatorRejectedEmailHandler` together with the re-application wait.
- Payload: envelope `eventId`, `eventType`, `occurredAt`, `correlationId`, `data`; data `email`,
  `reason` (field names per the system document's event catalog; `operatorId` is carried and not used).
- Re-application wait: setting `OperatorReapplicationWaitDays`, default 7, written as "7 days"
  ("1 day" for 1). Must equal Identity's re-application rule (7 days in Identity's specs).
- Effect: one send log entry per event, email type `operatorRejected`, status sent with the email
  service's receipt number.
- Problem codes: `EMAIL_SEND_PARKED` (worker code — error log line plus the alarm on `notification-dlq`).

## Acceptance criteria
Each criterion maps to one test in `SendOperatorRejectedEmail.Tests.cs`.

- [x] AC-1: rejected operator receives the email with the reason — `HandleAsync_OperatorRejected_SendsReasonAndReapplicationWait`
- [x] AC-2: same event twice sends one email — `HandleAsync_SameEventDeliveredTwice_SendsOneEmail`
- [x] AC-3: missing recipient logged Failed and dropped — `HandleAsync_NoRecipient_LogsFailedMissingRecipientWithoutSending`
- [x] AC-4: missing reason logged Failed and dropped — `HandleAsync_NoReason_LogsFailedInvalidEventDataWithoutSending`
- [x] AC-5: repeated temporary failure parks and alerts — `HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked`

Business rule 4 (the wait shown is the configured setting) is also covered by
`HandleAsync_WaitOfOneDay_SaysOneDay`.

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- Shared rule 11 (v0.2.0): an address on the do-not-send list is not emailed — the entry is Skipped with skip reason suppressed address. The slice sends through `Common/Email/EmailSendRecorder`, which applies it (`HandleAsync_PermanentBounce_PutsTheAddressOnTheListAndTheNextEmailToItIsSkipped` covers the verification email).
- `SendLogRules.BlocksRepeat` — only an email already Sent blocks a repeat delivery (ADR 0006).
- `SendLogRules.IsLastAttempt` — the queue's receive limit decides when a temporary failure parks.
- `EmailMessageBuilder` — two versions with the same words, the sign-off.
