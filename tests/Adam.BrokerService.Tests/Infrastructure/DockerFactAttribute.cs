namespace Adam.BrokerService.Tests.Infrastructure;

/// <summary>
/// A <see cref="FactAttribute"/> that skips the test when Docker is not available
/// in the current test environment. Dynamically sets the <see cref="FactAttribute.Skip"/>
/// property so that tests are marked as "Skipped" (not "Passed") when Docker is absent.
/// </summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerAvailability.IsAvailable)
            Skip = "Requires Docker";
    }
}
