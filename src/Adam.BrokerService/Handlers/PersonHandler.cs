using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Google.Protobuf;

namespace Adam.BrokerService.Handlers;

/// <summary>
/// Broker handler for person management operations.
/// Handles ListPersons, NamePerson, MergePersons, DeletePerson requests.
/// </summary>
public sealed class PersonHandler : HandlerBase
{
    public PersonHandler(
        IServiceProvider serviceProvider,
        ILogger<PersonHandler> logger,
        AuthorizationMiddleware authz)
        : base(serviceProvider, logger, authz)
    {
    }

    /// <summary>
    /// Lists all known persons with face counts.
    /// </summary>
    public async Task<Envelope> ListPersonsAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var persons = await db.Persons
            .Select(p => new
            {
                p.Id,
                p.Name,
                FaceCount = p.Faces.Count,
                p.CreatedAt,
                p.ModifiedAt,
                AvgConfidence = p.Faces.Any() ? p.Faces.Average(f => (double?)f.MatchingConfidence) ?? 0 : 0
            })
            .OrderBy(p => p.Name)
            .AsNoTracking()
            .ToListAsync(ct);

        var response = new ListPersonsResponse();
        foreach (var p in persons)
        {
            response.Persons.Add(new PersonWire
            {
                Id = p.Id.ToString(),
                Name = p.Name,
                FaceCount = p.FaceCount,
                CreatedAt = p.CreatedAt.ToUnixTimeMilliseconds(),
                ModifiedAt = p.ModifiedAt.ToUnixTimeMilliseconds(),
                AvgConfidence = (float)p.AvgConfidence
            });
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ListPersonsResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    /// <summary>
    /// Names a face / creates a new person or links to existing.
    /// </summary>
    public async Task<Envelope> NamePersonAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<NamePersonRequest>(request, out var req);
        if (error != null) return error;

        if (!Guid.TryParse(req!.AssetFaceId, out var faceId))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid face ID");

        if (string.IsNullOrWhiteSpace(req.PersonName))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Person name is required");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Find or create person
        var person = await db.Persons
            .FirstOrDefaultAsync(p => p.Name == req.PersonName, ct);

        if (person == null)
        {
            person = new Person
            {
                Id = Guid.NewGuid(),
                Name = req.PersonName.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            };
            db.Persons.Add(person);
        }

        // Link face to person
        var face = await db.AssetFaces.FirstOrDefaultAsync(f => f.Id == faceId, ct);
        if (face != null)
        {
            face.PersonId = person.Id;
            face.IsAutoAssigned = false; // Explicitly named, not auto-assigned
        }

        await db.SaveChangesAsync(ct);

        var response = new NamePersonResponse
        {
            PersonId = person.Id.ToString()
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.NamePersonResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    /// <summary>
    /// Merges two persons by moving all faces from source to target and deleting source.
    /// </summary>
    public async Task<Envelope> MergePersonsAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<MergePersonsRequest>(request, out var req);
        if (error != null) return error;

        if (!Guid.TryParse(req!.SourcePersonId, out var sourceId) ||
            !Guid.TryParse(req.TargetPersonId, out var targetId))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid person ID");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var source = await db.Persons
            .Include(p => p.Faces)
            .FirstOrDefaultAsync(p => p.Id == sourceId, ct);

        var target = await db.Persons
            .FirstOrDefaultAsync(p => p.Id == targetId, ct);

        if (source == null || target == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Person not found");

        // Move all faces from source to target
        foreach (var face in source.Faces.ToList())
        {
            face.PersonId = targetId;
        }

        // Recompute target centroid
        var matcher = scope.ServiceProvider.GetRequiredService<FaceMatcherService>();
        var centroid = await matcher.ComputeCentroidAsync(targetId, ct);
        target.CentroidEmbedding = centroid;

        // Delete source person
        db.Persons.Remove(source);
        await db.SaveChangesAsync(ct);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.MergePersonsResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new MergePersonsResponse())),
            StatusCode = ErrorCode.Success
        };
    }

    /// <summary>
    /// Deletes a person and unlinks all associated face records.
    /// </summary>
    public async Task<Envelope> DeletePersonAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<DeletePersonRequest>(request, out var req);
        if (error != null) return error;

        if (!Guid.TryParse(req!.PersonId, out var personId))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid person ID");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var person = await db.Persons
            .Include(p => p.Faces)
            .FirstOrDefaultAsync(p => p.Id == personId, ct);

        if (person == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Person not found");

        // Unlink all faces (SetNull behavior handled by EF)
        foreach (var face in person.Faces.ToList())
        {
            face.PersonId = null;
            face.IsAutoAssigned = false;
        }

        db.Persons.Remove(person);
        await db.SaveChangesAsync(ct);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.DeletePersonResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new DeletePersonResponse())),
            StatusCode = ErrorCode.Success
        };
    }
}
