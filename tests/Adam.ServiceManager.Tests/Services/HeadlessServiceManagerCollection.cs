namespace Adam.ServiceManager.Tests.Services;

/// <summary>
/// xUnit collection definition that serializes all Avalonia headless tests in the
/// ServiceManager test project. Only one Avalonia application can exist per process,
/// so these test classes must NOT run in parallel.
/// </summary>
[CollectionDefinition(nameof(HeadlessServiceManagerCollection), DisableParallelization = true)]
public sealed class HeadlessServiceManagerCollection
{
}
