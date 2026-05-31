namespace Adam.CatalogBrowser.Tests.Controls;

/// <summary>
/// xUnit collection definition that serializes all Avalonia headless tests.
/// Both <see cref="SearchableTreeViewHeadlessIntegrationTests"/> and
/// <see cref="DragGhostWindowSmokeTests"/> use <see cref="Lazy&lt;HeadlessUnitTestSession&gt;"/>
/// to initialize an Avalonia application. Since only one Avalonia application can exist
/// per process, these test classes must NOT run in parallel.
///
/// Apply <c>[Collection(nameof(HeadlessAvaloniaCollection))]</c> to any test class
/// that creates a <c>HeadlessUnitTestSession</c>.
/// </summary>
[CollectionDefinition(nameof(HeadlessAvaloniaCollection), DisableParallelization = true)]
public sealed class HeadlessAvaloniaCollection
{
}
