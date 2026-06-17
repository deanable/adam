using Google.Protobuf;

namespace Adam.Shared.Contracts;

// Empty request/response - keep manual
public sealed class GetServiceStatusRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

// Empty request - keep manual
public sealed class StartServiceRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class StartServiceResponse : IProtoSerializable
{
    [ProtoField(1)] public bool Success { get; set; }
    [ProtoField(2)] public string Message { get; set; } = string.Empty;
}

// Empty request - keep manual
public sealed class StopServiceRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed partial class StopServiceResponse : IProtoSerializable
{
    [ProtoField(1)] public bool Success { get; set; }
    [ProtoField(2)] public string Message { get; set; } = string.Empty;
}

public sealed partial class GetServiceStatusResponse : IProtoSerializable
{
    [ProtoField(1)] public int ActiveConnections { get; set; }
    [ProtoField(2)] public long RejectedConnections { get; set; }
    [ProtoField(3)] public int Port { get; set; }
    [ProtoField(4)] public long UptimeSeconds { get; set; }
    [ProtoField(5)] public string ServiceState { get; set; } = "Unknown";
}
