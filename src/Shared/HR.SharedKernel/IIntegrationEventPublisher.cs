namespace HR.SharedKernel;
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;

    // Same best-effort dispatch to every registered IIntegrationEventHandler<TEvent> as
    // PublishAsync (a failing handler still never blocks another handler or the caller), but
    // additionally reports whether every handler that opted in as "required" (by implementing
    // IRequiredIntegrationEventHandler<TEvent> rather than plain IIntegrationEventHandler<TEvent>)
    // actually succeeded. Callers that need to durably know "did delivery to my required
    // consumer(s) actually happen" (as opposed to "did the publish call merely return") should use
    // this instead of PublishAsync, and only mark their own delivery state complete when this
    // returns true. See IntegrationEventPublisher for the swallow-and-log behaviour this builds on.
    Task<bool> PublishAndConfirmAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;
}

// Opt-in marker for an integration event handler whose successful completion a publisher-side
// caller needs to durably confirm via PublishAndConfirmAsync, rather than merely knowing the
// publish call returned (which HR.SharedKernel.IntegrationEventPublisher deliberately guarantees
// even when a handler throws, so other handlers/the caller are never blocked by one failure).
// Register the implementing class under IIntegrationEventHandler<TEvent> as normal — this marker
// interface is only used by the publisher to decide whether a caught exception from this specific
// handler should flip PublishAndConfirmAsync's return value to false.
public interface IRequiredIntegrationEventHandler<TEvent> : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
{
}
