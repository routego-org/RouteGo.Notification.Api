# CAP-NTF-001 — Send verification email

- Capability: CAP-NTF-001
- Canonical spec: `capabilities/notification/features/verification-email.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SendVerificationEmail/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `topic: user-registered`, received from SQS
  `notification-q` as the raw RouteGo envelope; `Infrastructure/Messaging/NotificationQueueListener`
  hands each `UserRegistered` event to `SendVerificationEmailHandler` through
  `NotificationEventDispatcher`.
- Payload: envelope `eventId`, `eventType`, `occurredAt`, `correlationId`, `data`; data `email`,
  `name`, `role`, `verificationLink` (field names per the system document's event catalog;
  `userId` is carried and not used).
- Effect: one send log entry per (event ID, email type); status sent with the email service's
  receipt number.
- Problem codes: `EMAIL_SEND_PARKED` (worker code — an error log line naming event ID, event type,
  email type and the last error, plus the alarm on `notification-dlq`).

## Acceptance criteria
Each criterion maps to one test in `SendVerificationEmail.Tests.cs`.

- [x] AC-1: customer wording — `HandleAsync_CustomerRegistered_SendsCustomerWordingAndLogsSent`
- [x] AC-2: operator wording — `HandleAsync_OperatorRegistered_SendsOperatorWording`
- [x] AC-3: role matched case-insensitively — `HandleAsync_RoleInLowerCase_SendsCustomerWording`
- [x] AC-4: same event twice sends one email — `HandleAsync_SameEventDeliveredTwice_SendsOneEmail`
- [x] AC-5: a resend from Identity is a new email — `HandleAsync_NewEventForSameAddress_SendsSecondEmailWithNewLink`
- [x] AC-6: missing recipient logged Failed and dropped — `HandleAsync_NoRecipient_LogsFailedMissingRecipientWithoutSending`
- [x] AC-7: unusable role or missing link logged Failed and dropped — `HandleAsync_AdminRole_LogsFailedInvalidEventDataWithoutSending`
- [x] AC-8: email service refuses the address — `HandleAsync_EmailServiceRefusesAddress_LogsFailedRefusedWithReason`
- [x] AC-9: temporary failure retries then succeeds — `HandleAsync_TemporaryFailureThenRecovery_SendsOnceOnSameEntry`
- [x] AC-10: repeated temporary failure parks and alerts — `HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked`
- [x] AC-11: a replayed parked email is sent — `HandleAsync_ReplayOfParkedEmail_SendsOnSameEntry`
- [x] AC-12: emails wait while Notification is stopped — `HandleAsync_EventsWaitingAfterRestart_SendsEveryOne`.
  The waiting itself is the queue's (SQS keeps the messages); the test proves a backlog is all
  sent, and the system document's lab (§8.9, "stop notification, make bookings, restart") proves
  the rest end to end.

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- Shared rule 11 (v0.2.0): an address on the do-not-send list is not emailed — the entry is Skipped with skip reason suppressed address. The slice sends through `Common/Email/EmailSendRecorder`, which applies it (`HandleAsync_PermanentBounce_PutsTheAddressOnTheListAndTheNextEmailToItIsSkipped` covers the verification email).
- `SendLogRules.BlocksRepeat` — only an email already Sent blocks a repeat delivery (ADR 0006).
- `SendLogRules.IsLastAttempt` — the queue's receive limit decides when a temporary failure parks.
- `EmailMessageBuilder` — two versions with the same words, button vs full address, the sign-off.

## Decisions taken with the developer
- A message that cannot be used at all (not JSON, incomplete envelope, or an event type with no
  email) is logged as an error and left for the dead-letter queue.
- Only SES `MessageRejected` is a permanent refusal; every other SES error is temporary.
- The receive limit is read from `notification-q`'s redrive policy at startup.
- Messages are received 10 at a time and handled one at a time.
