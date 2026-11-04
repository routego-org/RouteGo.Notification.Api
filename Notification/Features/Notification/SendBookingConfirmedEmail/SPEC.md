# CAP-NTF-006 — Send booking confirmed email

- Capability: CAP-NTF-006
- Canonical spec: `capabilities/notification/features/booking-confirmed-emails.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SendBookingConfirmedEmail/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `topic: booking-confirmed`, received from SQS
  `notification-q`; `NotificationEventDispatcher` hands each `BookingConfirmed` event to `SendBookingConfirmedEmailHandler` (and, separately, to `SendNewBookingEmailHandler`; the two never affect each other).
- Payload: envelope `eventId`, `eventType`, `occurredAt`, `correlationId`, `data`; data = Booking's ticket snapshot: `bookingId`, `contact` (`name`, `email`), `trip` (`source`, `destination`, `departureUtc`, `busName`, `busType`, `isAc`), `seats` (`seatNo` and the passenger `name`), `totalAmount`.
- Effect: one send log entry per event, email type `bookingConfirmed`, repeat key = event ID, `booking_id` and `departs_at` set, status sent with the email service's receipt number.
- Problem codes: `EMAIL_SEND_PARKED` (worker code — error log line plus the alarm on `notification-dlq`).

## Acceptance criteria
Each criterion maps to tests in `SendBookingConfirmedEmail.Tests.cs`.

- [x] AC-1: customer receives the booking confirmed email — `HandleAsync_BookingConfirmed_SendsConfirmationWithSeatsTotalAndTicketLink`
- [x] AC-3: repeat safety (customer half) — `HandleAsync_SameEventDeliveredTwice_SendsOneEmail`
- [x] AC-4: missing contact email fails only the customer email — `HandleAsync_NoContactEmail_LogsFailedMissingRecipientWithoutAlert`; the operator half is covered by `SendNewBookingEmail` and `DispatchAsync_BookingConfirmed_SendsTheCustomersAndTheOperatorsEmail`
- [x] AC-6: unusable snapshot data (customer half) — `HandleAsync_NoSeats_LogsFailedInvalidEventDataNamingTheSeats`
- [x] AC-7: a booking known to be cancelled is skipped (customer half) — `HandleAsync_BookingKnownToBeCancelled_SkipsWithBookingCancelled`
- [x] AC-8: address on the do-not-send list is skipped — `HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddressButKeepsTheEmail`
- [x] AC-9: repeated temporary failure parks and alerts (customer half) — `HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked`
- [x] AC-10: emails wait while Notification is stopped — the waiting itself is the queue's (SQS keeps the messages); the redelivery path is `DispatchAsync_BookingConfirmedWhileEmailServiceIsDown_RetriesTheWholeMessage`

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- `SendLogRules.BlocksRepeat` — only an email already Sent blocks a repeat delivery (ADR 0006); the repeat key is the event ID (ADR 0007).
- Shared rule 13: a booking known to be cancelled gets no booking confirmed email (skip reason booking cancelled).
- `EmailSendRecorder` — the shared send, skip, retry and park behaviour of every email built on it.

## Decisions awaiting the developer's confirmation
- Booking, ticket and payment identifiers are read as UUIDs, as the send log contract's `bookingId` is; an event whose booking ID is not one is unusable event data.
- Passengers are read from each seat's `name`, `age` and `gender`, or from a nested `passenger` object: Booking's event page lists them beside the seat without fixing the shape.
- The send, skip, retry and park behaviour is shared in `Common/Email/EmailSendRecorder` rather than copied into each slice.
