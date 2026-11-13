using F = Foundatio.Mediator;
[assembly: F.MediatorConfiguration(HandlerDiscovery = F.HandlerDiscovery.Explicit,
    HandlerLifetime = F.MediatorLifetime.Transient, MiddlewareLifetime = F.MediatorLifetime.Transient)]
