namespace Adam.Shared.Models;

public class ModeConfiguration
{
    public Guid Id { get; set; }
    public string Mode { get; set; } = "Standalone";
    public string DbProvider { get; set; } = "sqlite";
    public string ConnectionString { get; set; } = string.Empty;
    public string? ServiceEndpoint { get; set; }
    public bool ServiceInstalled { get; set; }
    public DateTimeOffset LastModified { get; set; }
}
