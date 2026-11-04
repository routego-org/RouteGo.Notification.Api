# CAP-NTF-013 — Send the departure reminder

- Capability: CAP-NTF-013
- Canonical spec: `capabilities/notification/features/departure-reminder.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/SendDepartureReminderEmail/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `schedule: 24 hours before each booked departure`; `Infrastructure/Reminders/DepartureReminderScheduler` asks the known bookings for the planned reminders that are due once a minute and hands each booking to `SendDepartureReminderEmailHandler`.
- Payload: the booking ID of a planned reminder; everything else is read from the known booking at that moment.
- Effect: one send log entry per booking, email type `departureReminder`, event type `bookingConfirmed` with the planning event's ID, time and correlation ID, repeat key = the booking; the booking's reminder becomes done.
- Problem codes: `EMAIL_SEND_PARKED` (worker code — error log line plus the alarm on `notification-dlq`).

## Acceptance criteria
Each criterion maps to tests in `SendDepartureReminderEmail.Tests.cs`.

- [x] AC-6: the reminder is sent when due, with the current seats — `HandleAsync_ReminderDue_SendsTheCurrentSeatsAndMarksTheReminderDone`
- [x] AC-7: cancelled before the reminder: nothing is sent, and a reminder that still fires is skipped — `HandleAsync_BookingCancelledAndReminderWithdrawn_SkipsIfItStillFires`; a withdrawn reminder is never due: `RunDueRemindersAsync_OneDueOneNot_SendsOnlyTheDueReminder` finds planned reminders only
- [x] AC-8: a reminder that comes due after departure is skipped — `HandleAsync_ReminderHandledAfterDeparture_SkipsWithDeparturePassed`
- [x] AC-9: repeat safety: the reminder fires twice, one email — `HandleAsync_ReminderFiresTwice_SendsOneEmail`
- [x] AC-10: address on the do-not-send list — `HandleAsync_AddressOnDoNotSendList_SkipsWithSuppressedAddress`
- [x] AC-11: repeated temporary failure parks and alerts — `HandleAsync_TemporaryFailureBeforeTheLastAttempt_LeavesTheReminderPlanned`; `HandleAsync_LastAttemptStillUnavailable_ParksAndRaisesEmailSendParkedNamingTheConfirmedEvent`; `RunDueRemindersAsync_TemporaryFailure_TriesAgainOnTheNextRun`; `RunDueRemindersAsync_FifthFailureInARow_ParksTheReminderAndRaisesTheAlert`

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- `EmailDraft.IsStoppedByCancelledBooking` and `IsStoppedByDeparture` — the recorder skips a reminder for a cancelled booking or a passed departure, then for a suppressed address.
- Notification never scans all bookings: the scheduler's lookup is the indexed query of planned reminders that are due.

## Decisions awaiting the developer's confirmation
- A reminder that fails temporarily is tried once a minute and parked after five failed tries; the count lives in memory, so a restart gives a stuck reminder five fresh tries. The spec gives no number — confirm.
