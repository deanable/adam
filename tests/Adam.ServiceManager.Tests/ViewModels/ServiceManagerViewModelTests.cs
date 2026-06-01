using System.Reflection;
using Adam.ServiceManager.ViewModels;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.ServiceManager.Tests.ViewModels;

/// <summary>
/// Tests for the <see cref="ServiceManagerViewModel"/> properties.
/// </summary>
public sealed class ServiceManagerViewModelHealthTests
{
    [Fact]
    public void AdministratorAccount_IsNotEmpty()
    {
        var vm = new ServiceManagerViewModel(Enumerable.Empty<IServiceInstaller>());

        vm.AdministratorAccount.Should().NotBeNullOrWhiteSpace();
        vm.AdministratorAccount.Should().Contain("\\");
    }

    [Fact]
    public void IsElevated_DoesNotThrow()
    {
        var vm = new ServiceManagerViewModel(Enumerable.Empty<IServiceInstaller>());

        var act = () => { var x = vm.IsElevated; };
        act.Should().NotThrow();
    }

    [Fact]
    public void IsElevationRequired_InvertsIsElevated()
    {
        var vm = new ServiceManagerViewModel(Enumerable.Empty<IServiceInstaller>());

        vm.IsElevationRequired.Should().Be(!vm.IsElevated);
    }

    [Fact]
    public void Health_WhenNotInstalled_ReturnsRed()
    {
        var vm = new ServiceManagerViewModel(Enumerable.Empty<IServiceInstaller>());

        vm.Health.Should().Be(ServiceHealth.Red);
        vm.HealthLabel.Should().Be("Not Installed");
    }

    [Fact]
    public void Health_WhenInstalledButNotRunning_ReturnsAmber()
    {
        var vm = new ServiceManagerViewModel(Enumerable.Empty<IServiceInstaller>());

        vm.IsServiceInstalled = true;
        vm.IsServiceRunning = false;

        vm.Health.Should().Be(ServiceHealth.Amber);
        vm.HealthLabel.Should().Be("Starting Up");
    }

    [Fact]
    public void Health_WhenInstalledAndRunning_ReturnsGreen()
    {
        var vm = new ServiceManagerViewModel(Enumerable.Empty<IServiceInstaller>());

        vm.IsServiceInstalled = true;
        vm.IsServiceRunning = true;

        vm.Health.Should().Be(ServiceHealth.Green);
        vm.HealthLabel.Should().Be("Running");
    }

    [Fact]
    public void StatusText_ReflectsServiceStatus()
    {
        var vm = new ServiceManagerViewModel(Enumerable.Empty<IServiceInstaller>());

        vm.ServiceStatusText = "Running";
        vm.StatusText.Should().Be("Service: Running");
    }

    [Fact]
    public void RelaunchAsAdminCommand_DoesNotThrow()
    {
        var vm = new ServiceManagerViewModel(Enumerable.Empty<IServiceInstaller>());

        var act = () => vm.RelaunchAsAdminCommand.CanExecute(null);
        act.Should().NotThrow();
    }
}

/// <summary>
/// Tests for the private static <c>FormatUptime</c> method on
/// <see cref="ServiceManagerViewModel"/>, accessed via reflection.
/// </summary>
public sealed class ServiceManagerViewModelFormatUptimeTests
{
    private static readonly MethodInfo FormatUptimeMethod = typeof(ServiceManagerViewModel)
        .GetMethod("FormatUptime", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static string InvokeFormatUptime(long seconds)
    {
        return (string)FormatUptimeMethod.Invoke(null, [seconds])!;
    }

    [Fact]
    public void FormatUptime_ZeroSeconds_ReturnsZeroTime()
    {
        var result = InvokeFormatUptime(0);
        result.Should().Be("0h 0m 0s");
    }

    [Fact]
    public void FormatUptime_59Seconds_ReturnsSecondsOnly()
    {
        var result = InvokeFormatUptime(59);
        result.Should().Be("0h 0m 59s");
    }

    [Fact]
    public void FormatUptime_60Seconds_ReturnsOneMinute()
    {
        var result = InvokeFormatUptime(60);
        result.Should().Be("0h 1m 0s");
    }

    [Fact]
    public void FormatUptime_3600Seconds_ReturnsOneHour()
    {
        var result = InvokeFormatUptime(3600);
        result.Should().Be("1h 0m 0s");
    }

    [Fact]
    public void FormatUptime_3661Seconds_ReturnsOneHourOneMinuteOneSecond()
    {
        var result = InvokeFormatUptime(3661);
        result.Should().Be("1h 1m 1s");
    }

    [Fact]
    public void FormatUptime_86399Seconds_ReturnsHoursMinutesSeconds()
    {
        var result = InvokeFormatUptime(86399);
        result.Should().Be("23h 59m 59s");
    }

    [Fact]
    public void FormatUptime_86400Seconds_ReturnsOneDay()
    {
        var result = InvokeFormatUptime(86400);
        result.Should().Be("1d 0h 0m");
    }

    [Fact]
    public void FormatUptime_90061Seconds_ReturnsOneDayOneHourOneMinute()
    {
        var result = InvokeFormatUptime(90061);
        result.Should().Be("1d 1h 1m");
    }

    [Fact]
    public void FormatUptime_LargeValue_ReturnsDaysHoursMinutes()
    {
        var result = InvokeFormatUptime(1000000);
        result.Should().Be("11d 13h 46m");
    }

    [Theory]
    [InlineData(0, "0h 0m 0s")]
    [InlineData(30, "0h 0m 30s")]
    [InlineData(120, "0h 2m 0s")]
    [InlineData(7200, "2h 0m 0s")]
    [InlineData(86399, "23h 59m 59s")]
    [InlineData(86400, "1d 0h 0m")]
    [InlineData(172800, "2d 0h 0m")]
    [InlineData(31536000, "365d 0h 0m")]
    public void FormatUptime_Theory_MatchesExpected(long seconds, string expected)
    {
        var result = InvokeFormatUptime(seconds);
        result.Should().Be(expected);
    }
}
