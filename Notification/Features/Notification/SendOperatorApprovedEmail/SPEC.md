# CAP-NTF-002 — Send operator approved email

- Capability: CAP-NTF-002
- Canonical spec: `capabilities/notification/features/operator-approved-email.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SendOperatorApprovedEmail/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `topic: operator-approved`, received from SQS
  `notification-q`; `NotificationEventDispatcher` hands each `OperatorApproved` event to
  `SendOperatorApprovedEmailHandler` together with the Portal sign-in link.
- Payload: envelope `eventId`, `eventType`, `occurredAt`, `correlationId`, `data`; data `email`,
  `companyName` (field names per the system document's event catalog; `operatorId` is carried and
  not used).
- Sign-in link: setting `PortalBaseUrl` joined with setting `PortalSignInPath`. The spec's open
  question on the path is answered by the Web Portal specs (`architecture/portal-context.md`:
  `/auth/login`); the path is a setting so it can follow the Portal without a release.
- Effect: one send log entry per event, email type `operatorApproved`, status sent with the email
  service's receipt number.
- Problem codes: `EMAIL_SEND_PARKED` (worker code — error log line plus the alarm on `notification-dlq`).

## Acceptance criteria
Each criterion maps to one test in `SendOperatorApprovedEmail.Tests.cs`.

- [x] AC-1: approved operator receives the email — `HandleAsync_OperatorApproved_SendsApprovedEmailWithSignInLink`
- [x] AC-2: same event twice sends one email — `HandleAsync_SameEventDeliveredTwice_SendsOneEmail`
- [x] AC-3: missing recipient logged Failed and dropped — `HandleAsync_NoRecipient_LogsFailedMissingRecipientWithoutSending`
- [x] AC-4: missing company name logged Failed and dropped — `HandleAsync_NoCompanyName_LogsFailedInvalidEventDataWithoutSending`
- [x] AC-5: repeated temporary failure parks and alerts — `HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked`

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- Shared rule 11 (v0.2.0): an address on the do-not-send list is not emailed — the entry is Skipped with skip reason suppressed address. The slice sends through `Common/Email/EmailSendRecorder`, which applies it (`HandleAsync_PermanentBounce_PutsTheAddressOnTheListAndTheNextEmailToItIsSkipped` covers the verification email).
- `SendLogRules.BlocksRepeat` — only an email already Sent blocks a repeat delivery (ADR 0006).
- `SendLogRules.IsLastAttempt` — the queue's receive limit decides when a temporary failure parks.
- `EmailMessageBuilder` — two versions with the same words, button vs full address, the sign-off.
