using Google.Protobuf;

namespace Adam.Shared.Contracts;

public sealed class GetServiceStatusRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class StartServiceRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class StartServiceResponse : IProtoSerializable
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Success);
        size += ProtoHelper.FieldSize(2, Message);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Success);
        ProtoHelper.WriteField(output, 2, Message);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Success = input.ReadBool(); break;
                case 2: Message = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class StopServiceRequest : IProtoSerializable
{
    public int CalculateSize() => 0;
    public void WriteTo(CodedOutputStream output) { }
    public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) > 0) input.SkipLastField(); }
}

public sealed class StopServiceResponse : IProtoSerializable
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, Success);
        size += ProtoHelper.FieldSize(2, Message);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, Success);
        ProtoHelper.WriteField(output, 2, Message);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: Success = input.ReadBool(); break;
                case 2: Message = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}

public sealed class GetServiceStatusResponse : IProtoSerializable
{
    public int ActiveConnections { get; set; }
    public long RejectedConnections { get; set; }
    public int Port { get; set; }
    public long UptimeSeconds { get; set; }
    public string ServiceState { get; set; } = "Unknown";

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(1, ActiveConnections);
        size += ProtoHelper.FieldSize(2, RejectedConnections);
        size += ProtoHelper.FieldSize(3, Port);
        size += ProtoHelper.FieldSize(4, UptimeSeconds);
        size += ProtoHelper.FieldSize(5, ServiceState);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, 1, ActiveConnections);
        ProtoHelper.WriteField(output, 2, RejectedConnections);
        ProtoHelper.WriteField(output, 3, Port);
        ProtoHelper.WriteField(output, 4, UptimeSeconds);
        ProtoHelper.WriteField(output, 5, ServiceState);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case 1: ActiveConnections = input.ReadInt32(); break;
                case 2: RejectedConnections = input.ReadInt64(); break;
                case 3: Port = input.ReadInt32(); break;
                case 4: UptimeSeconds = input.ReadInt64(); break;
                case 5: ServiceState = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}
