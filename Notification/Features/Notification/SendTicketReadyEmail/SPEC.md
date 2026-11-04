# CAP-NTF-008 — Send ticket ready email

- Capability: CAP-NTF-008
- Canonical spec: `capabilities/notification/features/ticket-ready-email.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SendTicketReadyEmail/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `topic: ticket-ready`, received from SQS
  `notification-q`; `NotificationEventDispatcher` hands each `TicketReady` event to `SendTicketReadyEmailHandler`.
- Payload: envelope as usual; data `bookingId`, `issueKind` (`FirstIssue`/`Reissue`), `ticketChangeAt`, `contact` (`name`, `email`), `trip` (`source`, `destination`, `departureUtc`).
- Effect: one send log entry per ticket, email type `ticketReady` or `ticketReissued`, repeat key = booking and ticket change time (not the event ID), status sent with the receipt number.
- Problem codes: `EMAIL_SEND_PARKED` (worker code — error log line plus the alarm on `notification-dlq`).

## Acceptance criteria
Each criterion maps to tests in `SendTicketReadyEmail.Tests.cs`.

- [x] AC-1: first ticket sends the ticket ready email — `HandleAsync_FirstIssue_SendsTicketReadyWithTicketLink`
- [x] AC-2: a re-issued ticket sends the updated ticket email — `HandleAsync_Reissue_SendsUpdatedTicketThatReplacesTheEarlierOne`
- [x] AC-3: the same ticket made twice sends one email — `HandleAsync_SameTicketUnderNewEventId_SendsOneEmail`
- [x] AC-4: two seat changes send two updated tickets — `HandleAsync_SecondSeatChange_SendsSecondUpdatedTicket`
- [x] AC-5: missing recipient — `HandleAsync_NoContactEmail_LogsFailedMissingRecipient`
- [x] AC-6: unusable issue kind or missing booking — `HandleAsync_UnknownIssueKind_LogsFailedInvalidEventDataAsTicketReady`; `HandleAsync_NoBookingId_LogsFailedInvalidEventData`
- [x] AC-7: booking known to be cancelled — `HandleAsync_BookingKnownToBeCancelled_SkipsWithBookingCancelled`
- [x] AC-8: address on the do-not-send list — `HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddress`
- [x] AC-9: repeated temporary failure parks and alerts — `HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParked`

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- ADR 0007: the repeat key is the booking together with the ticket change time, because Ticket Generator may publish the same ticket again under a new event ID.
- Shared rule 13 and `EmailSendRecorder`, as for the booking emails.

## Decisions awaiting the developer's confirmation
- Booking, ticket and payment identifiers are read as UUIDs, as the send log contract's `bookingId` is; an event whose booking ID is not one is unusable event data.
- Passengers are read from each seat's `name`, `age` and `gender`, or from a nested `passenger` object: Booking's event page lists them beside the seat without fixing the shape.
- The send, skip, retry and park behaviour is shared in `Common/Email/EmailSendRecorder` rather than copied into each slice.
