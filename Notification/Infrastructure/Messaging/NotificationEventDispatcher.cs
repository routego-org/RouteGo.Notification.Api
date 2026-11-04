using Notification.Features.Notification.PlanDepartureReminder;
using Notification.Features.Notification.SendBookingConfirmedEmail;
using Notification.Features.Notification.SendBookingExpiredEmail;
using Notification.Features.Notification.SendBookingFailedEmail;
using Notification.Features.Notification.SendCancellationEmail;
using Notification.Features.Notification.SendNewBookingEmail;
using Notification.Features.Notification.SendOperatorApprovedEmail;
using Notification.Features.Notification.SendOperatorRejectedEmail;
using Notification.Features.Notification.SendRefundCompletedEmail;
using Notification.Features.Notification.SendSeatsChangedEmail;
using Notification.Features.Notification.SendTicketReadyEmail;
using Notification.Features.Notification.SendVerificationEmail;

namespace Notification.Infrastructure.Messaging;

/// <summary>
/// Hands each event to the slice that owns its event type, constructing that slice's handler from
/// the shared pieces. One case per event type; an event type with no case has no email.
/// </summary>
public sealed class NotificationEventDispatcher
{
    private readonly EmailSendRecorder _recorder;
    private readonly KnownBookingStore _knownBookings;
    private readonly NotificationSettings _settings;
    private readonly PlanDepartureReminderHandler _reminderPlanner;

    public NotificationEventDispatcher(
        EmailSendRecorder recorder,
        KnownBookingStore knownBookings,
        NotificationSettings settings,
        PlanDepartureReminderHandler reminderPlanner)
    {
        _recorder = recorder;
        _knownBookings = knownBookings;
        _settings = settings;
        _reminderPlanner = reminderPlanner;
    }

    /// <summary>Handles the event, or returns null when no slice owns its event type.</summary>
    public async Task<EventHandlingOutcome?> DispatchAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        switch (receivedEvent.EventType)
        {
            case SendVerificationEmailHandler.TriggerEventType:
                return await new SendVerificationEmailHandler(_recorder)
                    .HandleAsync(receivedEvent, cancellationToken);
            case SendOperatorApprovedEmailHandler.TriggerEventType:
                return await new SendOperatorApprovedEmailHandler(_recorder)
                    .HandleAsync(receivedEvent, _settings.Links.SignInPage, cancellationToken);
            case SendOperatorRejectedEmailHandler.TriggerEventType:
                return await new SendOperatorRejectedEmailHandler(_recorder)
                    .HandleAsync(receivedEvent, _settings.ReapplicationWaitDays, cancellationToken);
            default:
                return await DispatchBookingEventAsync(receivedEvent, cancellationToken);
        }
    }

    // The booking, ticket and payment events.
    private async Task<EventHandlingOutcome?> DispatchBookingEventAsync(
        ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        PortalLinks links = _settings.Links;
        switch (receivedEvent.EventType)
        {
            case SendBookingConfirmedEmailHandler.TriggerEventType:
                return await HandleBookingConfirmedAsync(receivedEvent, cancellationToken);
            case SendTicketReadyEmailHandler.TriggerEventType:
                return await new SendTicketReadyEmailHandler(_recorder, links)
                    .HandleAsync(receivedEvent, cancellationToken);
            case SendSeatsChangedEmailHandler.TriggerEventType:
                return await new SendSeatsChangedEmailHandler(_recorder, _knownBookings, links)
                    .HandleAsync(receivedEvent, cancellationToken);
            case SendCancellationEmailHandler.TriggerEventType:
                return await new SendCancellationEmailHandler(_recorder, _knownBookings)
                    .HandleAsync(receivedEvent, cancellationToken);
            case SendRefundCompletedEmailHandler.TriggerEventType:
                return await new SendRefundCompletedEmailHandler(_recorder, _knownBookings)
                    .HandleAsync(receivedEvent, cancellationToken);
            case SendBookingFailedEmailHandler.TriggerEventType:
                return await new SendBookingFailedEmailHandler(_recorder)
                    .HandleAsync(receivedEvent, cancellationToken);
            case SendBookingExpiredEmailHandler.TriggerEventType:
                return await new SendBookingExpiredEmailHandler(_recorder)
                    .HandleAsync(receivedEvent, cancellationToken);
            default:
                return null;
        }
    }

    // One event, three independent effects — the customer's email, the operator's email and the
    // departure reminder's planning — so the message is done with only when all three are, and one
    // failing never stops the others.
    private async Task<EventHandlingOutcome> HandleBookingConfirmedAsync(
        ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        var customerEmail = new SendBookingConfirmedEmailHandler(_recorder, _settings.Links);
        var operatorEmail = new SendNewBookingEmailHandler(_recorder, _settings.Links);
        EventHandlingOutcome customerOutcome = await customerEmail.HandleAsync(receivedEvent, cancellationToken);
        EventHandlingOutcome operatorOutcome = await operatorEmail.HandleAsync(receivedEvent, cancellationToken);
        EventHandlingOutcome planningOutcome = await _reminderPlanner.HandleAsync(
            receivedEvent, _settings.ReminderLeadTime, cancellationToken);
        bool needsRetry = customerOutcome == EventHandlingOutcome.RetryLater
            || operatorOutcome == EventHandlingOutcome.RetryLater
            || planningOutcome == EventHandlingOutcome.RetryLater;
        if (needsRetry)
        {
            return EventHandlingOutcome.RetryLater;
        }

        return EventHandlingOutcome.Consumed;
    }
}
