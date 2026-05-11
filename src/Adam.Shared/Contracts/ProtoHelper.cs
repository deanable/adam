using Google.Protobuf;

namespace Adam.Shared.Contracts;

public interface IProtoSerializable
{
    int CalculateSize();
    void WriteTo(CodedOutputStream output);
    void MergeFrom(CodedInputStream input);
}

public static class ProtoHelper
{
    public static byte[] Serialize(IProtoSerializable message)
    {
        var buffer = new byte[message.CalculateSize()];
        using var output = new CodedOutputStream(buffer);
        message.WriteTo(output);
        return buffer;
    }

    public static T Deserialize<T>(byte[] data) where T : IProtoSerializable, new()
    {
        var message = new T();
        using var input = new CodedInputStream(data);
        message.MergeFrom(input);
        return message;
    }

    public static void WriteField(CodedOutputStream output, int fieldNumber, string value)
    {
        if (value.Length == 0) return;
        output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
        output.WriteString(value);
    }

    public static void WriteField(CodedOutputStream output, int fieldNumber, ByteString value)
    {
        if (value.IsEmpty) return;
        output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(value);
    }

    public static void WriteField(CodedOutputStream output, int fieldNumber, int value)
    {
        if (value == 0) return;
        output.WriteTag(fieldNumber, WireFormat.WireType.Varint);
        output.WriteInt32(value);
    }

    public static void WriteField(CodedOutputStream output, int fieldNumber, long value)
    {
        if (value == 0) return;
        output.WriteTag(fieldNumber, WireFormat.WireType.Varint);
        output.WriteInt64(value);
    }

    public static void WriteField(CodedOutputStream output, int fieldNumber, bool value)
    {
        if (!value) return;
        output.WriteTag(fieldNumber, WireFormat.WireType.Varint);
        output.WriteBool(value);
    }

    public static void WriteField(CodedOutputStream output, int fieldNumber, double value)
    {
        if (value == 0) return;
        output.WriteTag(fieldNumber, WireFormat.WireType.Fixed64);
        output.WriteDouble(value);
    }

    public static void WriteField(CodedOutputStream output, int fieldNumber, IProtoSerializable message)
    {
        output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
        output.WriteLength(message.CalculateSize());
        message.WriteTo(output);
    }

    public static void WriteRepeatedField(CodedOutputStream output, int fieldNumber, IEnumerable<string> values)
    {
        foreach (var v in values)
            WriteField(output, fieldNumber, v);
    }

    public static void WriteRepeatedField(CodedOutputStream output, int fieldNumber, IEnumerable<IProtoSerializable> messages)
    {
        foreach (var m in messages)
            WriteField(output, fieldNumber, m);
    }

    public static int FieldSize(int fieldNumber, string value)
    {
        if (value.Length == 0) return 0;
        var tagSize = CodedOutputStream.ComputeRawVarint32Size(WireFormat.MakeTag(fieldNumber, WireFormat.WireType.LengthDelimited));
        return tagSize + CodedOutputStream.ComputeStringSize(value);
    }

    public static int FieldSize(int fieldNumber, ByteString value)
    {
        if (value.IsEmpty) return 0;
        var tagSize = CodedOutputStream.ComputeRawVarint32Size(WireFormat.MakeTag(fieldNumber, WireFormat.WireType.LengthDelimited));
        return tagSize + CodedOutputStream.ComputeBytesSize(value);
    }

    public static int FieldSize(int fieldNumber, int value)
    {
        if (value == 0) return 0;
        var tagSize = CodedOutputStream.ComputeRawVarint32Size(WireFormat.MakeTag(fieldNumber, WireFormat.WireType.Varint));
        return tagSize + CodedOutputStream.ComputeInt32Size(value);
    }

    public static int FieldSize(int fieldNumber, long value)
    {
        if (value == 0) return 0;
        var tagSize = CodedOutputStream.ComputeRawVarint32Size(WireFormat.MakeTag(fieldNumber, WireFormat.WireType.Varint));
        return tagSize + CodedOutputStream.ComputeInt64Size(value);
    }

    public static int FieldSize(int fieldNumber, bool value)
    {
        if (!value) return 0;
        var tagSize = CodedOutputStream.ComputeRawVarint32Size(WireFormat.MakeTag(fieldNumber, WireFormat.WireType.Varint));
        return tagSize + 1;
    }

    public static int FieldSize(int fieldNumber, double value)
    {
        if (value == 0) return 0;
        var tagSize = CodedOutputStream.ComputeRawVarint32Size(WireFormat.MakeTag(fieldNumber, WireFormat.WireType.Fixed64));
        return tagSize + 8;
    }

    public static int FieldSize(int fieldNumber, IProtoSerializable message)
    {
        var tagSize = CodedOutputStream.ComputeRawVarint32Size(WireFormat.MakeTag(fieldNumber, WireFormat.WireType.LengthDelimited));
        int msgSize = message.CalculateSize();
        return tagSize + CodedOutputStream.ComputeLengthSize(msgSize) + msgSize;
    }

    public static int RepeatedFieldSize(int fieldNumber, IEnumerable<string> values)
    {
        int size = 0;
        foreach (var v in values)
            size += FieldSize(fieldNumber, v);
        return size;
    }

    public static int RepeatedFieldSize(int fieldNumber, IEnumerable<IProtoSerializable> messages)
    {
        int size = 0;
        foreach (var m in messages)
            size += FieldSize(fieldNumber, m);
        return size;
    }

    public static void SkipField(CodedInputStream input, uint tag)
    {
        input.SkipLastField();
    }
}
