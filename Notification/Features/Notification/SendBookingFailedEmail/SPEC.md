# CAP-NTF-017 — Send booking failed email

- Capability: CAP-NTF-017
- Canonical spec: `capabilities/notification/features/booking-not-completed-emails.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SendBookingFailedEmail/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `topic: booking-failed`, received from SQS
  `notification-q`; `NotificationEventDispatcher` hands each `BookingFailed` event to `SendBookingFailedEmailHandler`.
- Payload: envelope as usual; data `bookingId`, `reason`, and, when Booking had them, `contact` and `trip`.
- Effect: for reason `TripCancelled` or `SeatsLost`, one send log entry, email type `bookingFailed`, repeat key = event ID; any other reason is consumed with no email and no entry.
- Problem codes: `EMAIL_SEND_PARKED` (worker code — error log line plus the alarm on `notification-dlq`).

## Acceptance criteria
Each criterion maps to tests in `SendBookingFailedEmail.Tests.cs`.

- [x] AC-1: trip cancelled before payment — `HandleAsync_TripCancelled_SaysNothingWasCharged`
- [x] AC-2: paid, but the seats were lost — `HandleAsync_SeatsLost_SaysThePaymentIsBeingReturned`
- [x] AC-3: other failure reasons send nothing — `HandleAsync_ReasonTheCustomerSawOnScreen_SendsNothingAndRecordsNothing`
- [x] AC-5: no contact on the event — `HandleAsync_NoContact_LogsFailedMissingRecipient`
- [x] AC-6: unusable event data — `HandleAsync_ContactButNoTrip_LogsFailedInvalidEventData`
- [x] AC-7: repeat safety — `HandleAsync_SameEventDeliveredTwice_SendsOneEmail`
- [x] AC-8: never to a do-not-send address — `HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddress`
- [x] AC-9: repeated temporary failure parks and alerts — `HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked`

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- Business rule 5: the booking is never made known (`HandleAsync_TripCancelled_DoesNotMakeTheBookingKnown`).
- Business rule 2: with no contact name the email greets "Hello" (`HandleAsync_NoContactName_GreetsHello`).

## Decisions awaiting the developer's confirmation
- Booking, ticket and payment identifiers are read as UUIDs, as the send log contract's `bookingId` is; an event whose booking ID is not one is unusable event data.
- Passengers are read from each seat's `name`, `age` and `gender`, or from a nested `passenger` object: Booking's event page lists them beside the seat without fixing the shape.
- The send, skip, retry and park behaviour is shared in `Common/Email/EmailSendRecorder` rather than copied into each slice.
