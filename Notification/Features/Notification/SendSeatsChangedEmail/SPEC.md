# CAP-NTF-009 — Send seats changed email

- Capability: CAP-NTF-009
- Canonical spec: `capabilities/notification/features/seat-change-email.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SendSeatsChangedEmail/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `topic: booking-seats-changed`, received from SQS
  `notification-q`; `NotificationEventDispatcher` hands each `BookingSeatsChanged` event to `SendSeatsChangedEmailHandler`.
- Payload: envelope as usual; data = the ticket snapshot after the change (`bookingId`, `contact`, `trip`, `seats` with `seatNo` and passenger `name`), `changes` (`fromSeatNo`, `toSeatNo`), `seatChangeCount` (1 to 3).
- Effect: one send log entry per event, email type `seatsChanged`, repeat key = event ID; the known booking holds the seats of the highest seat change count received.
- Problem codes: `EMAIL_SEND_PARKED` (worker code — error log line plus the alarm on `notification-dlq`).

## Acceptance criteria
Each criterion maps to tests in `SendSeatsChangedEmail.Tests.cs`.

- [x] AC-1: customer receives the seats changed email — `HandleAsync_SeatMoved_SendsMoveAndNewSeatsWithTicketLink`
- [x] AC-2: the known booking takes the new seats — `HandleAsync_NewerSeatChange_KnownBookingTakesTheNewSeats`
- [x] AC-3: an older seat change arriving late does not undo a newer one — `HandleAsync_OlderSeatChangeArrivingLate_KeepsNewerSeatsButStillSendsEmail`
- [x] AC-4: repeat safety — `HandleAsync_SameEventDeliveredTwice_SendsOneEmailAndKeepsTheBooking`
- [x] AC-5: a seat change before the confirmation is known — `HandleAsync_BookingNotKnownYet_BecomesKnownAsConfirmedFromTheEvent`
- [x] AC-6: missing recipient or unusable data — `HandleAsync_NoContactEmail_LogsFailedMissingRecipient`; `HandleAsync_NoMoves_LogsFailedInvalidEventData`
- [x] AC-7: booking known to be cancelled, or address on the do-not-send list — `HandleAsync_BookingKnownToBeCancelled_SkipsAndStaysCancelled`; `HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddress`
- [x] AC-8: repeated temporary failure parks and alerts — `HandleAsync_LastAttemptStillUnavailable_ParksAndKnownBookingAlreadyHasNewSeats`

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- `KnownBookingStore.RecordSeatChangeAsync` — seats change only to a higher seat change count; a cancelled booking is never changed; updated before the email, whatever happens to it.
- Shared rule 13 and `EmailSendRecorder`.

## Decisions awaiting the developer's confirmation
- Booking, ticket and payment identifiers are read as UUIDs, as the send log contract's `bookingId` is; an event whose booking ID is not one is unusable event data.
- Passengers are read from each seat's `name`, `age` and `gender`, or from a nested `passenger` object: Booking's event page lists them beside the seat without fixing the shape.
- The send, skip, retry and park behaviour is shared in `Common/Email/EmailSendRecorder` rather than copied into each slice.
