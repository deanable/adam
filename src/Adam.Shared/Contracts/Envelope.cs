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
    private const int ClientIpField = 7;

    public string AuthToken { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public MessageTypeCode MessageType { get; set; }
    public ByteString Payload { get; set; } = ByteString.Empty;
    public int StatusCode { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;

    /// <summary>
    /// Server-side connection ID. Not serialized on the wire.
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    public int CalculateSize()
    {
        int size = 0;
        size += ProtoHelper.FieldSize(AuthTokenField, AuthToken);
        size += ProtoHelper.FieldSize(CorrelationIdField, CorrelationId);
        size += ProtoHelper.FieldSize(MessageTypeField, (int)MessageType);
        size += ProtoHelper.FieldSize(PayloadField, Payload);
        size += ProtoHelper.FieldSize(StatusCodeField, StatusCode);
        size += ProtoHelper.FieldSize(ErrorMessageField, ErrorMessage);
        size += ProtoHelper.FieldSize(ClientIpField, ClientIp);
        return size;
    }

    public void WriteTo(CodedOutputStream output)
    {
        ProtoHelper.WriteField(output, AuthTokenField, AuthToken);
        ProtoHelper.WriteField(output, CorrelationIdField, CorrelationId);
        ProtoHelper.WriteField(output, MessageTypeField, (int)MessageType);
        ProtoHelper.WriteField(output, PayloadField, Payload);
        ProtoHelper.WriteField(output, StatusCodeField, StatusCode);
        ProtoHelper.WriteField(output, ErrorMessageField, ErrorMessage);
        ProtoHelper.WriteField(output, ClientIpField, ClientIp);
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
                case MessageTypeField: MessageType = (MessageTypeCode)input.ReadInt32(); break;
                case PayloadField: Payload = input.ReadBytes(); break;
                case StatusCodeField: StatusCode = input.ReadInt32(); break;
                case ErrorMessageField: ErrorMessage = input.ReadString(); break;
                case ClientIpField: ClientIp = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }

}
