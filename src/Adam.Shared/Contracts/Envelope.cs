using Google.Protobuf;

namespace Adam.Shared.Contracts;

public sealed class Envelope : IProtoSerializable
{
    private const int AuthTokenField = 1;
    private const int CorrelationIdField = 2;
    private const int MessageTypeField = 3;
    private const int PayloadField = 4;
    private const int StatusCodeField = 5;
    private const int ErrorMessageField = 6;

    public string AuthToken { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public ByteString Payload { get; set; } = ByteString.Empty;
    public int StatusCode { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(AuthTokenField, AuthToken);
        size += ProtoHelper.FieldSize(CorrelationIdField, CorrelationId);
        size += ProtoHelper.FieldSize(MessageTypeField, MessageType);
        size += ProtoHelper.FieldSize(PayloadField, Payload);
        size += ProtoHelper.FieldSize(StatusCodeField, StatusCode);
        size += ProtoHelper.FieldSize(ErrorMessageField, ErrorMessage);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, AuthTokenField, AuthToken);
        ProtoHelper.WriteField(output, CorrelationIdField, CorrelationId);
        ProtoHelper.WriteField(output, MessageTypeField, MessageType);
        ProtoHelper.WriteField(output, PayloadField, Payload);
        ProtoHelper.WriteField(output, StatusCodeField, StatusCode);
        ProtoHelper.WriteField(output, ErrorMessageField, ErrorMessage);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) > 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case AuthTokenField: AuthToken = input.ReadString(); break;
                case CorrelationIdField: CorrelationId = input.ReadString(); break;
                case MessageTypeField: MessageType = input.ReadString(); break;
                case PayloadField: Payload = input.ReadBytes(); break;
                case StatusCodeField: StatusCode = input.ReadInt32(); break;
                case ErrorMessageField: ErrorMessage = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
}
