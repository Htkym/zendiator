using System.Threading.Tasks.Sources;

namespace Zendiator.Tests;

public sealed record ControlledNotification(IValueTaskSource Source, short Version, AsyncLocal<string?> Context) : INotification;
