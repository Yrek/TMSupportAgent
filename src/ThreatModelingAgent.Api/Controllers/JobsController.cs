using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThreatModelingAgent.Api.Dtos;
using ThreatModelingAgent.Api.Security;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Controllers;

[ApiController]
[Authorize]
[Route("v1/orgs/{orgId:guid}/jobs")]
[EnableRateLimiting("api")]
public sealed class JobsController(
    IJobRepository jobs,
    IMembershipRepository memberships,
    IArchitectureRepository architectures,
    IBlobStorage blob,
    IAuditLogger audit,
    ILogger<JobsController> logger) : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".puml", ".txt", ".md", ".mmd", ".drawio", ".xml"];

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB (CLAUDE.md §9.7)

    // GET /v1/orgs/{orgId}/jobs
    [HttpGet]
    public async Task<IActionResult> ListJobs(
        Guid orgId,
        [FromQuery] JobStatus? status,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? cursor = null,
        CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        // Cap page size — CLAUDE.md §9.3
        var clampedSize = Math.Clamp(pageSize, 1, 100);
        var (items, hasMore) = await jobs.ListAsync(orgIdValue, status, clampedSize, cursor, ct);

        return Ok(new
        {
            data = items.Select(JobSummaryDto.From),
            pagination = new { hasMore, nextCursor = hasMore ? items.Last().Id.Value : (Guid?)null }
        });
    }

    // POST /v1/orgs/{orgId}/jobs — submit a new analysis job
    [HttpPost]
    [EnableRateLimiting("strict")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> SubmitJob(
        Guid orgId,
        [FromForm] SubmitJobRequest request,
        [FromServices] IJobQueue jobQueue,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var artifact = request.Artifact;

        // Validate file size (CLAUDE.md §9.7)
        if (artifact.Length > MaxFileSizeBytes)
            return StatusCode(StatusCodes.Status413RequestEntityTooLarge,
                new { code = "ARTIFACT_TOO_LARGE", message = "Artifact must not exceed 10 MB." });

        // Validate extension against allowlist (CLAUDE.md §9.6 — do not trust Content-Type alone)
        var ext = Path.GetExtension(artifact.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                new { code = "UNSUPPORTED_ARTIFACT_TYPE", message = "Unsupported file type." });

        // Create job record
        var job = Job.Create(orgIdValue, userId, request.Title);
        await jobs.AddAsync(job, ct);
        await jobs.SaveChangesAsync(ct);

        // Upload artifact to org-scoped blob path — filename randomised on write (CLAUDE.md §9.6)
        var blobPath = $"{orgIdValue}/uploads/{job.Id}/{Guid.NewGuid()}{ext}";
        await using var stream = artifact.OpenReadStream();
        await blob.UploadAsync(blobPath, stream, artifact.ContentType, ct);

        var artifactType = DetectArtifactType(ext);
        job.SetArtifact(blobPath, artifactType);
        await jobs.SaveChangesAsync(ct);

        // Enqueue Phase 1 (DETECT → PARSE → NORMALIZE) on the Service Bus
        await jobQueue.EnqueueParsePhaseAsync(job.Id, orgIdValue, blobPath, artifactType, ct);

        // Update job status to Parsing now that it's enqueued
        job.Transition(JobStatus.Parsing);
        await jobs.SaveChangesAsync(ct);

        await audit.LogAsync("job.submitted",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "job",
            resourceId: job.Id.Value,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        logger.LogInformation("Job submitted and enqueued. JobId={JobId} OrgId={OrgId} ArtifactType={ArtifactType}",
            job.Id, orgIdValue, artifactType);

        return AcceptedAtAction(nameof(GetJob),
            new { orgId, jobId = job.Id.Value },
            JobDetailDto.From(job));
    }

    // POST /v1/orgs/{orgId}/jobs/manual — create a job with an empty architecture (no file upload)
    [HttpPost("manual")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> CreateManualJob(
        Guid orgId,
        [FromBody] CreateManualJobRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        // Create job and skip straight to AwaitingReview — no pipeline phases needed
        var job = Job.Create(orgIdValue, userId, request.Title);
        await jobs.AddAsync(job, ct);
        await jobs.SaveChangesAsync(ct);

        job.Transition(JobStatus.AwaitingReview);
        await jobs.SaveChangesAsync(ct);

        // Create an empty architecture so elements can be added immediately
        var arch = Architecture.Create(
            jobId: job.Id,
            orgId: orgIdValue,
            systemPurpose: request.SystemPurpose,
            classification: [],
            assumptionsJson: "[]",
            gapsJson: "[]",
            clarificationQuestionsJson: "[]");

        await architectures.AddAsync(arch, ct);
        await architectures.SaveChangesAsync(ct);

        await audit.LogAsync("job.created_manual",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "job",
            resourceId: job.Id.Value,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        logger.LogInformation(
            "Manual job created. JobId={JobId} OrgId={OrgId}", job.Id, orgIdValue);

        return CreatedAtAction(nameof(GetJob),
            new { orgId, jobId = job.Id.Value },
            JobDetailDto.From(job));
    }

    // GET /v1/orgs/{orgId}/jobs/{jobId}
    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> GetJob(Guid orgId, Guid jobId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        // org_id scoping is defence-in-depth on top of RLS (CLAUDE.md §8.2 BOLA)
        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        return Ok(JobDetailDto.From(job));
    }

    // DELETE /v1/orgs/{orgId}/jobs/{jobId}
    [HttpDelete("{jobId:guid}")]
    public async Task<IActionResult> DeleteJob(Guid orgId, Guid jobId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        // Delete blob artifacts before removing the DB record
        if (job.ArtifactBlobPath is not null)
            await blob.DeleteByPrefixAsync($"{orgIdValue}/uploads/{job.Id}/", ct);

        await blob.DeleteByPrefixAsync($"{orgIdValue}/intermediate/{job.Id}/", ct);
        await blob.DeleteByPrefixAsync($"{orgIdValue}/outputs/{job.Id}/", ct);

        // Hard-delete the job record; EF cascade removes all child rows
        // (architectures, elements, threats, mitigations, etc.) via FK ON DELETE CASCADE
        jobs.Delete(job);
        await jobs.SaveChangesAsync(ct);

        await audit.LogAsync("job.deleted",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "job",
            resourceId: jobId,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        return NoContent();
    }

    private static string DetectArtifactType(string ext) => ext switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => "image",
        ".puml" => "plantuml",
        ".md" or ".mmd" => "mermaid",
        ".drawio" or ".xml" => "drawio",
        _ => "text"
    };
}
