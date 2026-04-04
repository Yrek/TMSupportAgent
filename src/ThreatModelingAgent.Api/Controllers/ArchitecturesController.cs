using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThreatModelingAgent.Api.Dtos;
using ThreatModelingAgent.Api.Security;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Controllers;

/// <summary>
/// Manages the canonical architecture model for a job.
///
/// Endpoints:
///   GET    /v1/orgs/{orgId}/jobs/{jobId}/architecture          — read the canonical model
///   POST   /v1/orgs/{orgId}/jobs/{jobId}/architecture/confirm  — confirm and trigger Phase 2
///   PATCH  /v1/orgs/{orgId}/jobs/{jobId}/elements/{elementId}  — correct an element
///
/// Authorization: org membership check on every request (defence-in-depth, RLS also active).
/// </summary>
[ApiController]
[Authorize]
[Route("v1/orgs/{orgId:guid}/jobs/{jobId:guid}")]
[EnableRateLimiting("api")]
public sealed class ArchitecturesController(
    IJobRepository jobs,
    IMembershipRepository memberships,
    IArchitectureRepository architectures,
    IJobQueue jobQueue,
    IAuditLogger audit,
    ILogger<ArchitecturesController> logger) : ControllerBase
{
    // GET /v1/orgs/{orgId}/jobs/{jobId}/architecture
    [HttpGet("architecture")]
    public async Task<IActionResult> GetArchitecture(Guid orgId, Guid jobId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        var arch = await architectures.GetByJobIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (arch is null) return NotFound();

        var elements = await architectures.ListElementsAsync(arch.Id, orgIdValue, ct);
        return Ok(ArchitectureDto.From(arch, elements));
    }

    // POST /v1/orgs/{orgId}/jobs/{jobId}/architecture/confirm
    [HttpPost("architecture/confirm")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> ConfirmArchitecture(
        Guid orgId,
        Guid jobId,
        [FromBody] ConfirmArchitectureRequest? request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        // Architecture can only be confirmed from AWAITING_REVIEW state
        if (job.Status != JobStatus.AwaitingReview)
            return Conflict(new
            {
                code = "INVALID_JOB_STATUS",
                message = $"Job must be in AwaitingReview status to confirm. Current: {job.Status}"
            });

        var arch = await architectures.GetByJobIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (arch is null) return NotFound();

        if (arch.IsConfirmed)
            return Conflict(new { code = "ALREADY_CONFIRMED", message = "Architecture is already confirmed." });

        // Mark as confirmed and load the artifact details needed to enqueue Phase 2
        arch.Confirm(userId);
        await architectures.SaveChangesAsync(ct);

        // Enqueue Phase 2 (CLASSIFY → ANALYZE → SYNTHESIZE) on the Service Bus
        var blobPath = job.ArtifactBlobPath
            ?? throw new InvalidOperationException("Job has no artifact blob path.");
        var artifactType = job.ArtifactType
            ?? throw new InvalidOperationException("Job has no artifact type.");

        await jobQueue.EnqueueAnalyzePhaseAsync(
            JobId.From(jobId), orgIdValue, blobPath, artifactType, ct);

        // Transition job to Classifying so the status reflects Phase 2 starting
        job.Transition(JobStatus.Classifying);
        await jobs.SaveChangesAsync(ct);

        await audit.LogAsync("architecture.confirmed",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "architecture",
            resourceId: arch.Id,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        logger.LogInformation(
            "Architecture confirmed, Phase 2 enqueued. JobId={JobId} ArchId={ArchId}",
            jobId, arch.Id);

        var elements = await architectures.ListElementsAsync(arch.Id, orgIdValue, ct);
        return Ok(ArchitectureDto.From(arch, elements));
    }

    // GET /v1/orgs/{orgId}/jobs/{jobId}/elements/{elementId}
    [HttpGet("elements/{elementId:guid}")]
    public async Task<IActionResult> GetElement(
        Guid orgId,
        Guid jobId,
        Guid elementId,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var element = await architectures.GetElementByIdAsync(elementId, orgIdValue, ct);
        if (element is null) return NotFound();

        // Verify element belongs to this job's architecture (CLAUDE.md §8.2 BOLA)
        var arch = await architectures.GetByJobIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (arch is null || element.ArchitectureId != arch.Id)
            return NotFound();

        return Ok(ArchitectureElementDto.From(element));
    }

    // PATCH /v1/orgs/{orgId}/jobs/{jobId}/elements/{elementId}
    [HttpPatch("elements/{elementId:guid}")]
    public async Task<IActionResult> PatchElement(
        Guid orgId,
        Guid jobId,
        Guid elementId,
        [FromBody] PatchElementRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        // Elements can only be corrected while the architecture is pending review
        if (job.Status != JobStatus.AwaitingReview)
            return Conflict(new
            {
                code = "INVALID_JOB_STATUS",
                message = "Elements can only be corrected while the job is in AwaitingReview status."
            });

        // org_id check on the element itself — defence-in-depth alongside RLS (CLAUDE.md §8.2 BOLA)
        var element = await architectures.GetElementByIdAsync(elementId, orgIdValue, ct);
        if (element is null) return NotFound();

        // Verify the element belongs to this job's architecture
        var arch = await architectures.GetByJobIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (arch is null || element.ArchitectureId != arch.Id)
            return NotFound();

        element.Update(request.Name, request.Description, propertiesJson: null);
        await architectures.SaveChangesAsync(ct);

        await audit.LogAsync("element.corrected",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "architecture_element",
            resourceId: elementId,
            ct: ct);

        return Ok(ArchitectureElementDto.From(element));
    }
}
