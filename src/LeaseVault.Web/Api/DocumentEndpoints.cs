using LeaseVault.Core.Abstractions;
using LeaseVault.Core.Domain;
using LeaseVault.Core.Security;
using LeaseVault.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeaseVault.Web.Api;

/// <summary>
/// Minimal API for content upload/download and the check-out lock. Uploads accept the raw
/// request body as a stream so clients can send large files (chunked transfer encoding) and the
/// storage provider stages them as blocks without buffering the whole file.
/// </summary>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents").WithTags("Documents");

        group.MapPost("/{id:int}/versions", UploadVersionAsync)
            .RequireAuthorization(Policies.Contributors)
            .DisableAntiforgery()
            .WithName("UploadDocumentVersion");

        group.MapGet("/{id:int}/versions/{version:int}", DownloadVersionAsync)
            .RequireAuthorization()
            .WithName("DownloadDocumentVersion");

        group.MapPost("/{id:int}/checkout", CheckOutAsync)
            .RequireAuthorization(Policies.Contributors)
            .DisableAntiforgery()
            .WithName("CheckOutDocument");

        group.MapPost("/{id:int}/checkin", CheckInAsync)
            .RequireAuthorization(Policies.Contributors)
            .DisableAntiforgery()
            .WithName("CheckInDocument");

        return app;
    }

    private static async Task<IResult> UploadVersionAsync(
        int id,
        HttpRequest request,
        DocumentService documents,
        [FromQuery] string? fileName,
        [FromQuery] string? comment,
        CancellationToken ct)
    {
        try
        {
            Stream content;
            string name;
            string contentType;

            if (request.HasFormContentType)
            {
                // Browser upload (multipart/form-data) from the Razor Pages UI.
                var form = await request.ReadFormAsync(ct);
                var file = form.Files.FirstOrDefault();
                if (file is null || file.Length == 0)
                {
                    return Results.BadRequest(new { error = "No file supplied." });
                }

                content = file.OpenReadStream();
                name = file.FileName;
                contentType = file.ContentType;
                comment ??= form["comment"].FirstOrDefault();
            }
            else
            {
                // Raw body upload: curl --data-binary @file "…/versions?fileName=lease.pdf"
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return Results.BadRequest(new { error = "fileName query parameter is required for raw uploads." });
                }

                content = request.Body;
                name = fileName;
                contentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType;
            }

            await using (content)
            {
                var version = await documents.AddVersionAsync(id, content, name, contentType, comment, ct);
                return Results.Created($"/api/documents/{id}/versions/{version.VersionNumber}", new
                {
                    documentId = id,
                    version = version.VersionNumber,
                    version.FileName,
                    version.SizeBytes,
                    version.Sha256,
                    version.UploadedUtc
                });
            }
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (DocumentLockedException ex)
        {
            return Results.Conflict(new { error = ex.Message, lockOwner = ex.LockOwner });
        }
        catch (DomainException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> DownloadVersionAsync(int id, int version, DocumentService documents, CancellationToken ct)
    {
        try
        {
            var (meta, stream) = await documents.OpenVersionAsync(id, version, ct);
            return Results.File(stream, meta.ContentType, meta.FileName, enableRangeProcessing: true);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound(new { error = "Stored content is missing." });
        }
    }

    private static async Task<IResult> CheckOutAsync(int id, DocumentService documents, CancellationToken ct)
    {
        try
        {
            var doc = await documents.CheckOutAsync(id, ct);
            return Results.Ok(new { documentId = id, doc.CheckedOutBy, doc.CheckedOutUtc });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (DocumentLockedException ex)
        {
            return Results.Conflict(new { error = ex.Message, lockOwner = ex.LockOwner });
        }
        catch (DomainException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CheckInAsync(int id, DocumentService documents, ICurrentUser user, CancellationToken ct, [FromQuery] bool force = false)
    {
        try
        {
            if (force && !user.IsInGroup(GroupNames.Admins))
            {
                return Results.Forbid();
            }

            await documents.CheckInAsync(id, force, ct);
            return Results.Ok(new { documentId = id, checkedOutBy = (string?)null });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (DocumentLockedException ex)
        {
            return Results.Conflict(new { error = ex.Message, lockOwner = ex.LockOwner });
        }
        catch (DomainException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
