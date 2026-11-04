# CAP-NTF-018 — Send booking expired email

- Capability: CAP-NTF-018
- Canonical spec: `capabilities/notification/features/booking-not-completed-emails.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SendBookingExpiredEmail/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `topic: booking-expired`, received from SQS
  `notification-q`; `NotificationEventDispatcher` hands each `BookingExpired` event to `SendBookingExpiredEmailHandler`.
- Payload: envelope as usual; data `bookingId`, and, when Booking had them, `contact` and `trip`.
- Effect: one send log entry per event, email type `bookingExpired`, repeat key = event ID, status sent with the receipt number.
- Problem codes: `EMAIL_SEND_PARKED` (worker code — error log line plus the alarm on `notification-dlq`).

## Acceptance criteria
Each criterion maps to tests in `SendBookingExpiredEmail.Tests.cs`.

- [x] AC-4: unpaid seats released — `HandleAsync_UnpaidSeatsReleased_SaysSeatsWereReleasedAndNothingCharged`
- [x] AC-5: no contact on the event — `HandleAsync_NoContact_LogsFailedMissingRecipient`
- [x] AC-6: unusable event data — `HandleAsync_NoBookingId_LogsFailedInvalidEventData`
- [x] AC-7: repeat safety — `HandleAsync_SameEventDeliveredTwice_SendsOneEmail`
- [x] AC-8: never to a do-not-send address — `HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddress`
- [x] AC-9: repeated temporary failure parks and alerts — `HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked`

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- Business rule 5 of the feature spec: the booking is never made known.

## Decisions awaiting the developer's confirmation
- Booking, ticket and payment identifiers are read as UUIDs, as the send log contract's `bookingId` is; an event whose booking ID is not one is unusable event data.
- Passengers are read from each seat's `name`, `age` and `gender`, or from a nested `passenger` object: Booking's event page lists them beside the seat without fixing the shape.
- The send, skip, retry and park behaviour is shared in `Common/Email/EmailSendRecorder` rather than copied into each slice.
