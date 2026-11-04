# CAP-NTF-012 — Plan the departure reminder

- Capability: CAP-NTF-012
- Canonical spec: `capabilities/notification/features/departure-reminder.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/PlanDepartureReminder/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `topic: booking-confirmed`; `NotificationEventDispatcher` hands each `BookingConfirmed` event to `PlanDepartureReminderHandler` (as well as to the two booking confirmed email slices; none affects the others).
- Payload: envelope as usual; data = Booking's ticket snapshot: `bookingId`, `contact` (`name`, `email`), `trip` (`source`, `destination`, `departureUtc`, `busName`), `seats` (`seatNo`, passenger `name`).
- Effect: the booking is known (`known_bookings`, migration V0006) and its reminder is planned for the departure time minus the setting `ReminderLeadHours` (default 24), or marked not planned when that time has passed.
- Problem codes: `REMINDER_NOT_PLANNED` (worker code — error log line naming the event, the booking and the reason `invalidEventData` or `repeatedFailure`).

## Acceptance criteria
Each criterion maps to tests in `PlanDepartureReminder.Tests.cs`.

- [x] AC-1: a reminder is planned 24 hours before departure — `HandleAsync_BookedDaysAhead_KnowsTheBookingAndPlansTheReminder24HoursBefore`
- [x] AC-2: booked less than 24 hours before departure: no reminder — `HandleAsync_BookedLessThan24HoursBeforeDeparture_PlansNoReminder`
- [x] AC-3: repeat safety: one reminder per booking — `HandleAsync_SameEventDeliveredTwice_KeepsOneReminderAtTheSameTime`
- [x] AC-4: a booking already known to be cancelled gets no reminder — `HandleAsync_BookingKnownToBeCancelled_PlansNoReminderAndStaysCancelled`
- [x] AC-5: a reminder that cannot be planned is retried, then alerts, and a replay plans it — `HandleAsync_PlanningFailsBeforeTheLastAttempt_RetriesWithoutAnAlert`; `HandleAsync_PlanningFailsOnTheLastAttempt_RaisesReminderNotPlannedAndReplayPlansIt`; the booking emails of the same event are sent regardless: `DispatchAsync_BookingConfirmedWhileEmailServiceIsDown_RetriesTheWholeMessage` shows the three effects are independent
- [x] Business rule 1: unusable event data records nothing and is not retried — `HandleAsync_UnusableEventData_RecordsNothingAndRaisesReminderNotPlannedWithoutRetry`
- [x] Known booking rules: a seat change heard first keeps its newer seats — `HandleAsync_BookingKnownFromASeatChange_KeepsTheNewerSeatsAndPlansTheReminder`

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- `KnownBookingStore.PlanReminderAsync` — one transaction: lock the booking, record or fill it in, decide the reminder only while it is still pending.
- A cancelled booking never becomes confirmed again and plans no reminder; the seats change only to a higher seat change count.

## Decisions awaiting the developer's confirmation
- How the planned reminder is kept is this codebase's decision (the spec leaves it open): it lives on the known booking (`reminder_state`, `reminder_due_at`, partial index V0006) and `Infrastructure/Reminders/DepartureReminderScheduler` fires it.
