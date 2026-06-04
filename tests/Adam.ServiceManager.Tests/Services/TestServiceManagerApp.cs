using Avalonia;
using Avalonia.Themes.Fluent;

namespace Adam.ServiceManager.Tests.Services;

/// <summary>
/// Minimal Avalonia Application that loads the Fluent theme for
/// headless control rendering during tests.
/// </summary>
internal sealed class TestServiceManagerApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        base.Initialize();
    }
}
