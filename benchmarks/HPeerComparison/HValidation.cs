using Microsoft.Extensions.DependencyInjection;

namespace Competitive;

internal static class HPeerValidation
{
    private static readonly string[] Peers = ["Zendiator", "MediatRHistorical", "Mediator", "DispatchR", "Immediate", "Foundatio"];
    private static readonly string[] Endpoints = ["FirstSend", "WarmSend", "ColdOuterFailure"];
    private static readonly string[] SuccessEvents =
    ["before:1", "before:2", "before:3", "handle", "after:3", "finally:3", "after:2", "finally:2", "after:1", "finally:1"];
    private static readonly string[] FailureEvents = ["before:1", "error:1", "finally:1"];

    private static ValueTask<int> Invoke(string peer, object entry, HRequest request, CancellationToken token) => peer switch
    {
        "Zendiator" => ((Generated.HPeerComposition.IZendiator)entry).SendAsync(request, token),
        "MediatRHistorical" => new(((global::MediatR.IMediator)entry).Send(request, token)),
        "Mediator" => ((global::Mediator.Mediator)entry).Send(request, token),
        "DispatchR" => ((global::DispatchR.IMediator)entry).Send(request, token),
        "Immediate" => ((HImmediate.Handler)entry).HandleAsync(request, token),
        "Foundatio" => ((global::Foundatio.Mediator.IMediator)entry).InvokeAsync<int>(request, token),
        _ => throw new ArgumentOutOfRangeException(nameof(peer))
    };

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    internal static Task Run(string output) => RunPeers(output, Peers);

    internal static Task RunPeer(string peer, string output)
    {
        Check(Peers.Contains(peer, StringComparer.Ordinal), "Unknown H comparison peer");
        return RunPeers(output, [peer]);
    }

    private static async Task RunPeers(string output, string[] selectedPeers)
    {
        var rows = new List<object>();
        using var token = new CancellationTokenSource();
        foreach (var peer in selectedPeers)
            foreach (var endpoint in Endpoints)
            {
                var work = new HWork();
                ServiceDescriptor[] descriptors = [];
                var ids = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
                int Id(object receiver)
                {
                    if (!ids.TryGetValue(receiver, out var id))
                        ids.Add(receiver, id = ids.Count + 1);
                    return id;
                }
                object[] Activations(IEnumerable<HActivation> values) => values.Select(value => (object)new
                {
                    value.Role,
                    instanceId = Id(value.Receiver),
                    type = value.Receiver.GetType().FullName,
                    assembly = value.Receiver.GetType().Assembly.GetName().Name
                }).ToArray();

                object? operation = null;
                using (var provider = HPeerRegistration.Create(peer, work, services => descriptors = services.ToArray()))
                {
                    var entryType = HPeerRegistration.EntryType(peer);
                    var entryDescriptor = descriptors.LastOrDefault(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == entryType);
                    Check(entryDescriptor?.Lifetime == ServiceLifetime.Scoped, peer + " public entry must be explicitly Scoped");
                    var receiverServiceTypes = HPeerRegistration.ReceiverServiceTypes(peer);
                    Check(receiverServiceTypes.Length == 4 && receiverServiceTypes.Distinct().Count() == 4,
                        peer + " must declare the three logical Hook receivers and one handler receiver");
                    foreach (var type in receiverServiceTypes)
                    {
                        var registrations = descriptors.Where(descriptor => descriptor.ServiceType == type ||
                            (descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType) == type).ToArray();
                        Check(registrations.Length != 0 && registrations.All(descriptor => descriptor.Lifetime == ServiceLifetime.Transient),
                            peer + " receiver must have explicit Transient registration: " + type.FullName);
                    }

                    // Match benchmark setup: compile and warm the provider graph, not the fresh target scope.
                    using (var startupScope = provider.CreateScope())
                    {
                        var startupEntry = startupScope.ServiceProvider.GetRequiredService(entryType);
                        Check(await Invoke(peer, startupEntry, new HRequest(7, false, token.Token), token.Token).ConfigureAwait(false)
                            == 7 + HWork.Expected, peer + " provider warmup result differs");
                    }
                    Check(!work.Record, "Provider startup must not enter the recorded target operation");
                    work.Record = true;

                    using (var scope = provider.CreateScope())
                    {
                        var beforeEntryConstruction = work.Created.Count;
                        var entry = scope.ServiceProvider.GetRequiredService(entryType);
                        var createdByEntryResolution = work.Created.Count - beforeEntryConstruction;
                        Check(work.Events.Count == 0, peer + " resolved entry executed user callbacks");
                        var request = new HRequest(7, endpoint == "ColdOuterFailure", token.Token);
                        object? warmPreparation = null;
                        if (endpoint == "WarmSend")
                        {
                            Check(await Invoke(peer, entry, request, token.Token).ConfigureAwait(false) == 7 + HWork.Expected,
                                peer + " warm preparation result differs");
                            Check(work.Events.SequenceEqual(SuccessEvents), peer + " warm preparation callback order differs");
                            warmPreparation = new
                            {
                                constructed = Activations(work.Created),
                                beforeReceivers = Activations(work.BeforeReceivers),
                                handled = Activations(work.Handled),
                                events = work.Events.ToArray()
                            };
                            work.Events.Clear();
                            work.Trace.Clear();
                            work.BeforeReceivers.Clear();
                            work.Handled.Clear();
                        }
                        var createdBeforeOperation = work.Created.Count;
                        int? result = null;
                        string? error = null;
                        if (request.FailOuter)
                        {
                            try
                            {
                                await Invoke(peer, entry, request, token.Token).ConfigureAwait(false);
                                throw new InvalidOperationException(peer + " did not fail in the outer Hook");
                            }
                            catch (HOuterFailure failure)
                            {
                                error = failure.GetType().FullName;
                            }
                            Check(work.Events.SequenceEqual(FailureEvents), peer + " outer failure callback work/order differs");
                            Check(work.Handled.Count == 0, peer + " invoked the handler after outer Hook failure");
                            Check(request.Phase == 1 && request.Accumulator == 17 * 31 + 1,
                                peer + " outer failure did not stop the ordered work after the first Hook");
                        }
                        else
                        {
                            result = await Invoke(peer, entry, request, token.Token).ConfigureAwait(false);
                            Check(result == 7 + HWork.Expected, peer + " successful result differs");
                            Check(work.Events.SequenceEqual(SuccessEvents), peer + " successful callback work/order differs");
                            Check(request.Phase == 3 && request.Accumulator == HWork.Expected,
                                peer + " successful ordered work differs");
                            Check(work.Handled.Count == 1, peer + " handler invocation count differs");
                        }
                        Check(work.Released.Count == 0, peer + " disposed a borrowed receiver before target scope disposal");
                        Check(work.Created.Any(value => value.Role == "Hook1"), peer + " did not record the outer receiver activation");
                        Check(work.Created.All(value => receiverServiceTypes.Contains(value.Receiver.GetType())),
                            peer + " activated a receiver from a different native binding");
                        if (!request.FailOuter)
                            Check(new[] { "Hook1", "Hook2", "Hook3", "Handler" }.All(role => work.Created.Any(value => value.Role == role)),
                                peer + " did not activate all successful-path logical receivers");
                        Check(work.BeforeReceivers.Concat(work.Handled).All(call =>
                            work.Created.Any(created => created.Role == call.Role && ReferenceEquals(created.Receiver, call.Receiver))),
                            peer + " callback receiver was not an observed native activation");
                        operation = new
                        {
                            peer,
                            endpoint,
                            requestValue = request.Value,
                            request.FailOuter,
                            request.Phase,
                            request.Accumulator,
                            result,
                            error,
                            entryType = entryType.FullName,
                            entryLifetime = entryDescriptor!.Lifetime.ToString(),
                            declaredTransientReceiverTypes = receiverServiceTypes.Select(type => type.FullName).ToArray(),
                            providerGraphWarmupCompleted = true,
                            scopeCreatedBeforeMediatorAndReceiverResolution = true,
                            createdByEntryResolution,
                            createdBeforeOperation,
                            createdByOperation = work.Created.Count - createdBeforeOperation,
                            warmPreparation,
                            created = Activations(work.Created),
                            beforeReceivers = Activations(work.BeforeReceivers),
                            handled = Activations(work.Handled),
                            events = work.Events.ToArray(),
                            trace = work.Trace.ToArray(),
                            releasedBeforeScopeDisposal = work.Released.Count,
                            constructionCounts = work.Created.GroupBy(value => value.Role)
                                .ToDictionary(group => group.Key, group => group.Count()),
                            serviceDescriptors = descriptors.Select(descriptor => new
                            {
                                serviceType = descriptor.ServiceType.FullName,
                                keyed = descriptor.IsKeyedService,
                                implementationType = (descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType)?.FullName,
                                lifetime = descriptor.Lifetime.ToString(),
                                factory = descriptor.IsKeyedService ? descriptor.KeyedImplementationFactory != null : descriptor.ImplementationFactory != null,
                                instanceType = (descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance : descriptor.ImplementationInstance)?.GetType().FullName
                            }).ToArray()
                        };
                    }
                    var createdIds = work.Created.Select(value => Id(value.Receiver)).ToArray();
                    var releasedIds = work.Released.Select(value => Id(value.Receiver)).ToArray();
                    var nativeExactlyOnce = createdIds.Order().SequenceEqual(releasedIds.Order()) && releasedIds.Distinct().Count() == releasedIds.Length;
                    var membershipMatches = work.Released.All(released => work.Created.Any(created => created.Role == released.Role &&
                        ReferenceEquals(created.Receiver, released.Receiver)));
                    var knownAliasDisposalDifference = peer == "DispatchR" && createdIds.Distinct().Count() == createdIds.Length &&
                        work.Created.Count == 4 && new[] { "Hook1", "Hook2", "Hook3", "Handler" }.All(role => work.Created.Count(value => value.Role == role) == 1) &&
                        membershipMatches && work.Released.Count == work.Created.Count + 1 &&
                        work.Created.All(created => work.Released.Count(released => ReferenceEquals(created.Receiver, released.Receiver)) ==
                            (created.Role == "Hook1" ? 2 : 1));
                    var disposalMatchesProposedPolicy = createdIds.Distinct().Count() == createdIds.Length && membershipMatches &&
                        (peer == "DispatchR" ? knownAliasDisposalDifference : nativeExactlyOnce);
                    if (!disposalMatchesProposedPolicy)
                    {
                        try
                        {
                            var directory = Path.GetDirectoryName(Path.GetFullPath(output))!;
                            var failurePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(output) +
                                ".disposal-failure." + peer + "." + endpoint + "." + Guid.NewGuid().ToString("N") + ".json");
                            Check(!File.Exists(failurePath), "Refusing to overwrite an earlier H disposal failure");
                            ArtifactEvidence.Write(failurePath, new
                            {
                                timing = false,
                                diagnostic = "Raw native-DI disposal failure outside the proposed observational policy; validation still throws after this snapshot.",
                                peer,
                                endpoint,
                                selectedPeers,
                                nativeExactlyOnce,
                                knownAliasDisposalDifference,
                                parentReviewRequired = peer == "DispatchR",
                                disposalMatchesProposedPolicy,
                                operation,
                                completedPriorRowsInThisInvocation = rows.ToArray(),
                                createdRaw = Activations(work.Created),
                                releasedRaw = Activations(work.Released),
                                createdIds,
                                releasedIds,
                                perReceiverCounts = work.Created.Concat(work.Released).GroupBy(value => Id(value.Receiver))
                                    .Select(group => new
                                    {
                                        instanceId = group.Key,
                                        type = group.First().Receiver.GetType().FullName,
                                        created = work.Created.Count(value => ReferenceEquals(value.Receiver, group.First().Receiver)),
                                        disposeCalls = work.Released.Count(value => ReferenceEquals(value.Receiver, group.First().Receiver)),
                                        creationRoles = work.Created.Where(value => ReferenceEquals(value.Receiver, group.First().Receiver))
                                            .Select(value => value.Role).ToArray(),
                                        disposalRoles = work.Released.Where(value => ReferenceEquals(value.Receiver, group.First().Receiver))
                                            .Select(value => value.Role).ToArray()
                                    }).ToArray(),
                                nativeDescriptors = descriptors.Select((descriptor, ordinal) =>
                                {
                                    Delegate? factory = descriptor.IsKeyedService ? descriptor.KeyedImplementationFactory : descriptor.ImplementationFactory;
                                    var key = descriptor.IsKeyedService ? descriptor.ServiceKey : null;
                                    return new
                                    {
                                        ordinal,
                                        serviceType = descriptor.ServiceType.AssemblyQualifiedName,
                                        keyed = descriptor.IsKeyedService,
                                        keyType = key?.GetType().AssemblyQualifiedName,
                                        keyValue = key is Type type ? type.AssemblyQualifiedName : key?.ToString(),
                                        implementationType = (descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType)?.AssemblyQualifiedName,
                                        lifetime = descriptor.Lifetime.ToString(),
                                        factory = factory != null,
                                        factoryMethod = factory?.Method.ToString(),
                                        factoryDeclaringType = factory?.Method.DeclaringType?.AssemblyQualifiedName,
                                        factoryTargetType = factory?.Target?.GetType().AssemblyQualifiedName,
                                        instanceType = (descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance : descriptor.ImplementationInstance)?.GetType().AssemblyQualifiedName
                                    };
                                }).ToArray()
                            });
                            Console.Error.WriteLine("H disposal diagnostic saved: " + failurePath);
                        }
                        catch (Exception diagnosticFailure)
                        {
                            Console.Error.WriteLine("H disposal diagnostic could not be saved; disposal assertion still follows: " + diagnosticFailure);
                        }
                    }
                    Check(createdIds.Distinct().Count() == createdIds.Length, peer + " recorded the same activation twice");
                    if (peer == "DispatchR")
                        Check(knownAliasDisposalDifference, peer + " native disposal differs from the exact outer Hook1 alias observation policy");
                    else
                        Check(createdIds.Order().SequenceEqual(releasedIds.Order()) && releasedIds.Distinct().Count() == releasedIds.Length,
                            peer + " native DI did not dispose every constructed receiver exactly once with the target scope");
                    Check(work.Released.All(released => work.Created.Any(created => created.Role == released.Role &&
                        ReferenceEquals(created.Receiver, released.Receiver))), peer + " disposal receiver/role differs from native activation");
                    rows.Add(new
                    {
                        operation,
                        releasedAfterScopeDisposal = Activations(work.Released),
                        createdRaw = Activations(work.Created),
                        createdIds,
                        releasedIds,
                        disposalCounts = work.Released.GroupBy(value => value.Role).ToDictionary(group => group.Key, group => group.Count()),
                        perReceiverDisposeCallCounts = work.Created.Select(created => new
                        {
                            created.Role,
                            instanceId = Id(created.Receiver),
                            disposeCalls = work.Released.Count(released => ReferenceEquals(created.Receiver, released.Receiver))
                        }).ToArray(),
                        nativeExactlyOnce,
                        nativeScopeOwns = true,
                        nativeScopeOwnsRecordedReceivers = true,
                        knownAliasDisposalDifference,
                        parentReviewRequired = peer == "DispatchR"
                    });
                    work.Record = false;
                }
            }
        ArtifactEvidence.Write(output, new
        {
            timing = false,
            peers = selectedPeers,
            endpoints = Endpoints,
            rows,
            disposalPolicy = "Proposed observation only: non-DispatchR exactly once; DispatchR requires exactly one activation of each logical receiver, the same outer Hook1 disposed twice and every other receiver once. No comparison admission is implied.",
            parentReviewRequired = selectedPeers.Contains("DispatchR", StringComparer.Ordinal),
            comparisonAdmissionApproved = false,
            meaning = "Common ordered work and callbacks through each native Scoped public entry; receiver activation timing and Transient reuse are observed rather than normalized.",
            firstSendBoundary = "Provider graph startup, target scope creation and disposal are excluded; target mediator and receiver resolution are included.",
            failureBoundary = "Cold legal outer Hook failure: before:1, error:1, finally:1, no handler execution. Native activation counts may differ.",
            warmBoundary = "Each native entry is resolved once in the target scope; first and followup receiver identities/counts are reported, not forced equal between libraries."
        });
    }
}
