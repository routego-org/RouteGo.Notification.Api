# CAP-NTF-010 — Send booking cancelled email

- Capability: CAP-NTF-010
- Canonical spec: `capabilities/notification/features/cancellation-email.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SendCancellationEmail/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `topic: booking-cancelled`, received from SQS
  `notification-q`; `NotificationEventDispatcher` hands each `BookingCancelled` event to `SendCancellationEmailHandler`.
- Payload: envelope as usual; data `bookingId`, `contact`, `trip`, `kind` (`CustomerCancelled`/`AdminCancelled`/`OperatorCancelled`), `reason`, `refundPercent`, `refundAmount`.
- Effect: one send log entry per event, email type `bookingCancelled`, repeat key = event ID; the known booking becomes cancelled and a planned reminder is withdrawn.
- Problem codes: `EMAIL_SEND_PARKED` (worker code — error log line plus the alarm on `notification-dlq`).

## Acceptance criteria
Each criterion maps to tests in `SendCancellationEmail.Tests.cs`.

- [x] AC-1: customer cancellation with a refund — `HandleAsync_CustomerCancelledWithRefund_SaysAsYouAskedAndShowsRefundWithoutReason`
- [x] AC-2: operator cancellation shows the operator's reason — `HandleAsync_OperatorCancelled_ShowsTheOperatorsReasonAndFullRefund`
- [x] AC-3: admin cancellation shows the admin's reason — `HandleAsync_AdminCancelled_ShowsTheAdminsReason`; `HandleAsync_AdminCancelledWithoutReason_LeavesTheReasonLineOut`
- [x] AC-4: no refund due — `HandleAsync_NoRefundDue_SaysNoRefundAndPromisesNoFurtherEmail`
- [x] AC-5: the reminder is withdrawn and the booking is known as cancelled — `HandleAsync_BookingKnownWithPlannedReminder_MarksItCancelledAndWithdrawsTheReminder`
- [x] AC-6: cancellation arrives before the confirmation — `HandleAsync_CancellationBeforeConfirmation_BecomesKnownAsCancelledWithNoReminder`
- [x] AC-7: repeat safety — `HandleAsync_SameEventDeliveredTwice_SendsOneEmailAndKeepsTheBooking`
- [x] AC-8: missing recipient or unusable kind — `HandleAsync_NoContactEmail_LogsFailedButStillWithdrawsTheKnownBooking`; `HandleAsync_UnknownKind_LogsFailedInvalidEventDataButStillRecordsTheCancellation`
- [x] AC-9: sent although the booking is cancelled, but not to a do-not-send address — `HandleAsync_BookingAlreadyKnownCancelled_StillSendsTheCancellationEmail`; `HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddress`
- [x] AC-10: repeated temporary failure parks and alerts — `HandleAsync_LastAttemptStillUnavailable_ParksAndTheReminderIsAlreadyWithdrawn`

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- `KnownBookingStore.RecordCancellationAsync` — cancelled for good, a planned reminder becomes withdrawn, a second delivery changes nothing.
- Shared rule 13 never stops a cancellation email; the do-not-send list still does.

## Decisions awaiting the developer's confirmation
- Booking, ticket and payment identifiers are read as UUIDs, as the send log contract's `bookingId` is; an event whose booking ID is not one is unusable event data.
- Passengers are read from each seat's `name`, `age` and `gender`, or from a nested `passenger` object: Booking's event page lists them beside the seat without fixing the shape.
- The send, skip, retry and park behaviour is shared in `Common/Email/EmailSendRecorder` rather than copied into each slice.
- A booking Notification has never known, cancelled by an event with no contact or trip, cannot be remembered (the known booking requires them); nothing is recorded for it.
