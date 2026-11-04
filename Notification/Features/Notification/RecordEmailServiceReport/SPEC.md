# CAP-NTF-014 — Record bounces and complaints on the do-not-send list

- Capability: CAP-NTF-014
- Canonical spec: `capabilities/notification/features/do-not-send-list.md` (spec-RouteGo.Notification.Api, v0.3.2)
- Area: Notification
- Slice: `Features/Notification/RecordEmailServiceReport/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation. Trigger `queue: email-service-reports`; `Infrastructure/Messaging/EmailServiceReportListener` reads the queue named by setting `EmailServiceReportsQueueUrl` and hands each report to `RecordEmailServiceReportHandler`.
- Payload: the email service's bounce or complaint notification, read by `EmailServiceReportReader` whether delivered as it is or wrapped in a topic message: kind, the addresses, the time, the receipt number of the reported email and the service's description.
- Effect: every address of a permanent bounce or complaint is on `suppressed_addresses` (migration V0006) with its reason; a temporary bounce changes nothing.
- Problem codes: `EMAIL_REPORT_PARKED` (worker code — error log line plus the alarm on the reports queue's dead-letter queue).

## Acceptance criteria
Each criterion maps to tests in `RecordEmailServiceReport.Tests.cs`.

- [x] AC-1: a permanent bounce puts the address on the list — `HandleAsync_PermanentBounce_PutsTheAddressOnTheListAndTheNextEmailToItIsSkipped`; `Read_PermanentBounce_GivesTheAddressTimeReceiptAndDetail`
- [x] AC-2: a complaint puts the address on the list — `HandleAsync_Complaint_PutsTheAddressOnTheListWithReasonComplaint`; `Read_ComplaintWrappedInATopicMessage_GivesTheComplaint`
- [x] AC-3: a temporary bounce changes nothing — `HandleAsync_TemporaryBounce_ChangesNothing`; `Read_TransientBounce_IsATemporaryBounce`
- [x] AC-4: repeat safety: a repeated report changes nothing — `HandleAsync_RepeatedReportInAnotherCase_LeavesOneUnchangedEntry`; `HandleAsync_ReportNamingSeveralAddresses_PutsEveryOneOnTheList`
- [x] AC-5: an unusable report is dropped — `Read_BounceNamingNoAddress_IsNotUsable`; `Read_DeliveryNotice_IsNotUsable`; `Read_NotJson_IsNotUsable`; `Read_BounceWithoutATime_IsNotUsable`
- [x] AC-6: repeated failure parks the report and alerts, and a replay records it — `HandleAsync_RecordingFailsBeforeTheLastAttempt_RetriesWithoutAnAlert`; `HandleAsync_RecordingFailsOnTheLastAttempt_RaisesEmailReportParkedAndReplayRecordsIt`
- [x] AC-13: a new bounce after removal adds the address again — `HandleAsync_AddressRemovedThenBouncesAgain_IsOnTheListAsANewEntry`

## Invariants touched
Business rules live in `Domain/` and `Common/`, never in this slice.

- Every email checks its recipient against the list, case-insensitively, just before sending (`EmailSendRecorder`); the v1 emails go through it too.
- One statement records every address of a report, so no I/O runs inside a loop.

## Decisions awaiting the developer's confirmation
- Which queue and how the email service's reports reach it (SES event publishing to a topic to the queue) is deployment, not code: the reader accepts the notification directly or wrapped in a topic message. The setting `EmailServiceReportsQueueUrl` is required, and the queue needs a dead-letter redrive policy like `notification-q`.
- The parked-report alert names the report by kind, address count, time and the receipt number of the reported email, not by the addresses: they are personal data and the coding standard keeps personal data out of log lines. The problem catalogue lists `addresses` — confirm which wins.
