using System.ComponentModel;
using System.Reflection;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Configuration;
using Adam.Shared.Extractors;
using Adam.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adam.CatalogBrowser.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="PluginManagerViewModel"/> — plugin list display,
/// refresh, and folder-opening behavior.
/// </summary>
public sealed class PluginManagerViewModelTests
{
    [Fact]
    public void Constructor_LoadsPlugins()
    {
        var sut = CreateSut();

        sut.Plugins.Should().NotBeEmpty();
        sut.HasPlugins.Should().BeTrue();
    }

    [Fact]
    public void Plugins_ContainsBothBuiltInExtractors()
    {
        var sut = CreateSut();

        sut.Plugins.Should().Contain(p => p.Name.Contains("Image"));
        sut.Plugins.Should().Contain(p => p.Name.Contains("Office"));
    }

    [Fact]
    public void BuiltInPlugins_HaveIsBuiltInTrue()
    {
        var sut = CreateSut();

        sut.Plugins.Should().AllSatisfy(p => p.IsBuiltIn.Should().BeTrue());
    }

    [Fact]
    public void BuiltInPlugins_HaveStatusLoaded()
    {
        var sut = CreateSut();

        sut.Plugins.Should().AllSatisfy(p => p.Status.Should().Be("Loaded"));
    }

    [Fact]
    public void PluginPriorities_AreCorrect()
    {
        var sut = CreateSut();

        var imagePlugin = sut.Plugins.Should().ContainSingle(p => p.Name.Contains("Image"))
            .Which;
        imagePlugin.Priority.Should().Be(100);

        var officePlugin = sut.Plugins.Should().ContainSingle(p => p.Name.Contains("Office"))
            .Which;
        officePlugin.Priority.Should().Be(200);
    }

    [Fact]
    public void RefreshCommand_ReloadsPlugins()
    {
        var sut = CreateSut();
        var initialCount = sut.Plugins.Count;

        sut.RefreshCommand.Execute(null);

        sut.Plugins.Should().HaveCount(initialCount);
    }

    [Fact]
    public void CloseCommand_RaisesRequestClose()
    {
        var sut = CreateSut();
        var invoked = false;
        sut.RequestClose += () => invoked = true;

        sut.CloseCommand.Execute(null);

        invoked.Should().BeTrue();
    }

    [Fact]
    public void OpenPluginFolderCommand_DoesNotThrow()
    {
        var sut = CreateSut();

        var act = () => sut.OpenPluginFolderCommand.Execute(null);
        act.Should().NotThrow();
    }

    private static PluginManagerViewModel CreateSut()
    {
        var pluginLoader = new PluginLoaderService(
            Options.Create(new PluginConfig()),
            new NullLogger<PluginLoaderService>());
        return new PluginManagerViewModel(pluginLoader);
    }
}
