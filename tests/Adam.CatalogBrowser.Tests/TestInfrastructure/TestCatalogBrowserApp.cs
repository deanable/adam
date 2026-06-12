using Avalonia;

namespace Adam.CatalogBrowser.Tests.TestInfrastructure;

/// <summary>
/// Minimal Avalonia Application that loads the Fluent theme for
/// headless control rendering during tests.
/// </summary>
internal sealed class TestCatalogBrowserApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        base.Initialize();
    }
}
