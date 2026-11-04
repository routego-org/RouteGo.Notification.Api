# CAP-NTF-011 — Send refund completed email

- Capability: CAP-NTF-011
- Canonical spec: `capabilities/notification/features/refund-completed-email.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SendRefundCompletedEmail/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `topic: refund-completed`, received from SQS
  `notification-q`; `NotificationEventDispatcher` hands each `RefundCompleted` event to `SendRefundCompletedEmailHandler`.
- Payload: envelope as usual; data `bookingId`, `amount`, `customerEmail`.
- Effect: one send log entry per booking, email type `refundCompleted`, repeat key = the booking (not the event ID), status sent with the receipt number.
- Problem codes: `EMAIL_SEND_PARKED` (worker code — error log line plus the alarm on `notification-dlq`).

## Acceptance criteria
Each criterion maps to tests in `SendRefundCompletedEmail.Tests.cs`.

- [x] AC-1: email from the event, name from the known booking — `HandleAsync_KnownBooking_SendsToTheEventEmailGreetingTheKnownName`
- [x] AC-2: a booking Notification never knew is still emailed — `HandleAsync_BookingNeverKnown_StillEmailsTheCustomerGreetingHello`
- [x] AC-3: no email in the event: the known booking's contact, else missing recipient — `HandleAsync_NoEmailInEventButBookingKnown_SendsToTheKnownContact`; `HandleAsync_NoEmailAndBookingNotKnown_LogsFailedMissingRecipientWithoutAlert`
- [x] AC-4: one refund email per booking, whatever the event ID — `HandleAsync_SameBookingUnderNewEventId_SendsOneEmail`
- [x] AC-5: unusable event data — `HandleAsync_NoBookingId_LogsFailedInvalidEventData`; `HandleAsync_NoAmount_LogsFailedInvalidEventData`
- [x] AC-6: sent even for a cancelled booking, but not to a do-not-send address — `HandleAsync_CancelledBooking_StillSendsButNotToADoNotSendAddress`
- [x] AC-7: repeated temporary failure parks and alerts — `HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked`

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- Business rule 1: the customer email is personal data and never reaches a log line (`HandleAsync_CustomerEmail_NeverReachesALogLine`).
- ADR 0007: the repeat key is the booking.

## Decisions awaiting the developer's confirmation
- Booking, ticket and payment identifiers are read as UUIDs, as the send log contract's `bookingId` is; an event whose booking ID is not one is unusable event data.
- Passengers are read from each seat's `name`, `age` and `gender`, or from a nested `passenger` object: Booking's event page lists them beside the seat without fixing the shape.
- The send, skip, retry and park behaviour is shared in `Common/Email/EmailSendRecorder` rather than copied into each slice.
- The known booking this email falls back to is written by the booking confirmed (CAP-NTF-012), seat change and cancellation slices, so a refund for any booking Notification heard about greets by name.
