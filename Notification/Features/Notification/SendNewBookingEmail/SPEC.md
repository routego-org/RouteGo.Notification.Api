# CAP-NTF-007 — Send new booking email to the operator

- Capability: CAP-NTF-007
- Canonical spec: `capabilities/notification/features/booking-confirmed-emails.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SendNewBookingEmail/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `topic: booking-confirmed`, received from SQS
  `notification-q`; `NotificationEventDispatcher` hands each `BookingConfirmed` event to `SendNewBookingEmailHandler` as well as to `SendBookingConfirmedEmailHandler`; one failing never stops the other.
- Payload: as `SendBookingConfirmedEmail`, using `operator` (`name`, `email`), the trip and each seat's passenger `name`, `age` and `gender`; the contact and amounts are never read.
- Effect: one send log entry per event, email type `newBooking`, repeat key = event ID, status sent with the email service's receipt number.
- Problem codes: `EMAIL_SEND_PARKED` (worker code — error log line plus the alarm on `notification-dlq`).

## Acceptance criteria
Each criterion maps to tests in `SendNewBookingEmail.Tests.cs`.

- [x] AC-2: operator receives the new booking email without amount or contact — `HandleAsync_BookingConfirmed_SendsOperatorThePassengersWithoutAmountOrContact`
- [x] AC-3: repeat safety (operator half) — `HandleAsync_SameEventDeliveredTwice_SendsOneEmail`
- [x] AC-5: missing operator email fails only the operator email — `HandleAsync_NoOperatorEmail_LogsFailedMissingRecipient`
- [x] AC-6: unusable snapshot data (operator half) — `HandleAsync_NoSeats_LogsFailedInvalidEventData`
- [x] AC-7: a booking known to be cancelled is skipped (operator half) — `HandleAsync_BookingKnownToBeCancelled_SkipsWithBookingCancelled`
- [x] AC-9: repeated temporary failure parks and alerts (operator half) — `HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked`

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- Business rule 3: the email shows what the operator's own passenger list shows and never the contact, fares or total.
- `SendLogRules.BlocksRepeat` and `EmailSendRecorder`, as for the customer email.

## Decisions awaiting the developer's confirmation
- Booking, ticket and payment identifiers are read as UUIDs, as the send log contract's `bookingId` is; an event whose booking ID is not one is unusable event data.
- Passengers are read from each seat's `name`, `age` and `gender`, or from a nested `passenger` object: Booking's event page lists them beside the seat without fixing the shape.
- The send, skip, retry and park behaviour is shared in `Common/Email/EmailSendRecorder` rather than copied into each slice.
