using System.Text.Json;
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

/// <summary>
/// Manages the canonical architecture model for a job.
///
/// Endpoints:
///   GET    /v1/orgs/{orgId}/jobs/{jobId}/architecture          — read the canonical model
///   POST   /v1/orgs/{orgId}/jobs/{jobId}/architecture/confirm  — confirm and trigger Phase 2
///   PATCH  /v1/orgs/{orgId}/jobs/{jobId}/elements/{elementId}  — correct an element
///
/// Authorization: org membership check on every request (defence-in-depth, RLS also active).
///
/// Manual job flow (no file upload):
///   POST  /v1/orgs/{orgId}/jobs/manual                   — creates job + empty arch
///   POST  /v1/orgs/{orgId}/jobs/{jobId}/elements         — add element
///   PATCH /v1/orgs/{orgId}/jobs/{jobId}/elements/{id}   — update element
///   DELETE /v1/orgs/{orgId}/jobs/{jobId}/elements/{id}  — remove element
///   POST  /v1/orgs/{orgId}/jobs/{jobId}/architecture/confirm — trigger analysis
/// </summary>
[ApiController]
[Authorize]
[Route("v1/orgs/{orgId:guid}/jobs/{jobId:guid}")]
[EnableRateLimiting("api")]
public sealed class ArchitecturesController(
    IJobRepository jobs,
    IMembershipRepository memberships,
    IArchitectureRepository architectures,
    IThreatRepository threats,
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
        var corrections = await architectures.ListCorrectionsAsync(arch.Id, orgIdValue, ct);
        return Ok(ArchitectureDto.From(arch, elements, corrections));
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

        // Enqueue Phase 2 (CLASSIFY → ANALYZE → SYNTHESIZE) on the Service Bus.
        // Manual jobs have no artifact blob — the orchestrator detects "manual" artifact type
        // and builds the canonical model from the user-defined elements in DB instead.
        var blobPath = job.ArtifactBlobPath ?? string.Empty;
        var artifactType = job.ArtifactType ?? "manual";

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
        var corrections = await architectures.ListCorrectionsAsync(arch.Id, orgIdValue, ct);
        return Ok(ArchitectureDto.From(arch, elements, corrections));
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

        // Serialize Properties if provided; null leaves the stored value unchanged
        string? propertiesJson = null;
        if (request.Properties.HasValue &&
            request.Properties.Value.ValueKind != JsonValueKind.Null)
            propertiesJson = request.Properties.Value.GetRawText();

        element.Update(request.Name, request.Description, propertiesJson);
        await architectures.SaveChangesAsync(ct);

        await audit.LogAsync("element.corrected",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "architecture_element",
            resourceId: elementId,
            ct: ct);

        var elementCorrections = await architectures.ListCorrectionsAsync(arch.Id, orgIdValue, ct);
        var thisCorrsList = elementCorrections.Where(c => c.ElementId == elementId).ToList();
        return Ok(ArchitectureElementDto.From(element, thisCorrsList));
    }

    // POST /v1/orgs/{orgId}/jobs/{jobId}/elements/:elementId — record a correction/annotation
    [HttpPost("elements/{elementId:guid}")]
    public async Task<IActionResult> CorrectElement(
        Guid orgId,
        Guid jobId,
        Guid elementId,
        [FromBody] CorrectElementRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        if (job.Status != JobStatus.AwaitingReview)
            return Conflict(new
            {
                code = "INVALID_JOB_STATUS",
                message = "Corrections can only be recorded while the job is in AwaitingReview status."
            });

        // Validate CorrectionType allow-list (CLAUDE.md §6.3)
        if (!Enum.TryParse<CorrectionType>(request.CorrectionType, ignoreCase: true, out var correctionType))
            return UnprocessableEntity(new
            {
                code = "INVALID_CORRECTION_TYPE",
                message = $"Unknown correction type: {request.CorrectionType}. Valid values: {string.Join(", ", Enum.GetNames<CorrectionType>())}"
            });

        // Validate required fields per correction type
        if (correctionType == CorrectionType.Update &&
            string.IsNullOrWhiteSpace(request.FieldName))
            return UnprocessableEntity(new
            {
                code = "FIELD_NAME_REQUIRED",
                message = "FieldName is required for Update corrections."
            });

        if (correctionType == CorrectionType.AddNote &&
            string.IsNullOrWhiteSpace(request.Note))
            return UnprocessableEntity(new
            {
                code = "NOTE_REQUIRED",
                message = "Note is required for AddNote corrections."
            });

        // Field name length guard
        if (request.FieldName?.Length > 100)
            return UnprocessableEntity(new { code = "FIELD_NAME_TOO_LONG", message = "FieldName must not exceed 100 characters." });

        // Value/note length guards
        if (request.CorrectedValue?.Length > 5000)
            return UnprocessableEntity(new { code = "VALUE_TOO_LONG", message = "CorrectedValue must not exceed 5000 characters." });

        if (request.Note?.Length > 5000)
            return UnprocessableEntity(new { code = "NOTE_TOO_LONG", message = "Note must not exceed 5000 characters." });

        // org_id scoping — defence-in-depth alongside RLS (CLAUDE.md §8.2 BOLA)
        var element = await architectures.GetElementByIdAsync(elementId, orgIdValue, ct);
        if (element is null) return NotFound();

        var arch = await architectures.GetByJobIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (arch is null || element.ArchitectureId != arch.Id) return NotFound();

        var correction = ArchitectureCorrection.Create(
            elementId: elementId,
            architectureId: arch.Id,
            orgId: orgIdValue,
            correctedBy: userId,
            correctionType: correctionType,
            fieldName: request.FieldName,
            originalValue: request.OriginalValue,
            correctedValue: request.CorrectedValue,
            note: request.Note);

        await architectures.AddCorrectionAsync(correction, ct);
        await architectures.SaveChangesAsync(ct);

        await audit.LogAsync("element.correction_recorded",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "architecture_correction",
            resourceId: correction.Id,
            ct: ct);

        logger.LogInformation(
            "Element correction recorded. ElementId={ElementId} CorrectionType={Type} JobId={JobId}",
            elementId, correctionType, jobId);

        // Return the updated element with all its corrections
        var allCorrections = await architectures.ListCorrectionsAsync(arch.Id, orgIdValue, ct);
        var elementCorrs = allCorrections.Where(c => c.ElementId == elementId).ToList();
        return Ok(ArchitectureElementDto.From(element, elementCorrs));
    }

    // POST /v1/orgs/{orgId}/jobs/{jobId}/elements
    [HttpPost("elements")]
    public async Task<IActionResult> AddElement(
        Guid orgId,
        Guid jobId,
        [FromBody] AddElementRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        if (job.Status != JobStatus.AwaitingReview)
            return Conflict(new
            {
                code = "INVALID_JOB_STATUS",
                message = "Elements can only be added while the job is in AwaitingReview status."
            });

        var arch = await architectures.GetByJobIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (arch is null) return NotFound();

        // Validate ElementType allow-list — reject unknown values (CLAUDE.md §6.3)
        if (!Enum.TryParse<ElementType>(request.ElementType, ignoreCase: true, out var elementType))
            return UnprocessableEntity(new
            {
                code = "INVALID_ELEMENT_TYPE",
                message = $"Unknown element type: {request.ElementType}. Valid values: {string.Join(", ", Enum.GetNames<ElementType>())}"
            });

        var propertiesJson = (request.Properties.HasValue &&
                              request.Properties.Value.ValueKind != JsonValueKind.Null)
            ? request.Properties.Value.GetRawText()
            : "{}";

        var element = ArchitectureElement.CreateUserAdded(
            arch.Id, orgIdValue, elementType, request.Name, request.Description, propertiesJson);

        await architectures.AddElementAsync(element, ct);
        await architectures.SaveChangesAsync(ct);

        await audit.LogAsync("element.added",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "architecture_element",
            resourceId: element.Id,
            ct: ct);

        logger.LogInformation(
            "Element added manually. ElementId={ElementId} JobId={JobId} Type={Type}",
            element.Id, jobId, elementType);

        return CreatedAtAction(nameof(GetElement),
            new { orgId, jobId, elementId = element.Id },
            ArchitectureElementDto.From(element));
    }

    // DELETE /v1/orgs/{orgId}/jobs/{jobId}/elements/{elementId}
    [HttpDelete("elements/{elementId:guid}")]
    public async Task<IActionResult> DeleteElement(
        Guid orgId,
        Guid jobId,
        Guid elementId,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        if (job.Status != JobStatus.AwaitingReview)
            return Conflict(new
            {
                code = "INVALID_JOB_STATUS",
                message = "Elements can only be removed while the job is in AwaitingReview status."
            });

        // org_id scoping on element — defence-in-depth alongside RLS (CLAUDE.md §8.2 BOLA)
        var element = await architectures.GetElementByIdAsync(elementId, orgIdValue, ct);
        if (element is null) return NotFound();

        // Verify element belongs to this job's architecture
        var arch = await architectures.GetByJobIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (arch is null || element.ArchitectureId != arch.Id)
            return NotFound();

        architectures.RemoveElement(element);
        await architectures.SaveChangesAsync(ct);

        await audit.LogAsync("element.deleted",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "architecture_element",
            resourceId: elementId,
            ct: ct);

        return NoContent();
    }

    // POST /v1/orgs/{orgId}/jobs/{jobId}/architecture/reanalyze
    [HttpPost("architecture/reanalyze")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> ReanalyzeJob(
        Guid orgId,
        Guid jobId,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        // Re-analysis only allowed from terminal states
        if (job.Status is not (JobStatus.Complete or JobStatus.Partial))
            return Conflict(new
            {
                code = "INVALID_JOB_STATUS",
                message = $"Re-analysis requires a Complete or Partial job. Current: {job.Status}"
            });

        var arch = await architectures.GetByJobIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (arch is null) return NotFound();

        // Reset architecture: clear confirmation, bump version
        arch.ResetForReanalysis();
        await architectures.SaveChangesAsync(ct);

        // Remove system-generated threats so re-analysis starts from a clean slate.
        // User-added threats (source == user) are preserved.
        await threats.DeleteSystemGeneratedAsync(JobId.From(jobId), orgIdValue, ct);
        await threats.SaveChangesAsync(ct);

        // Transition job back to AwaitingReview so the user can correct the architecture
        // before triggering Phase 2 again via POST /architecture/confirm.
        job.Transition(JobStatus.AwaitingReview);
        await jobs.SaveChangesAsync(ct);

        await audit.LogAsync("job.reanalyze_requested",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "job",
            resourceId: job.Id.Value,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        logger.LogInformation(
            "Re-analysis requested. JobId={JobId} ArchVersion={Version}",
            jobId, arch.Version);

        return Ok(JobDetailDto.From(job));
    }
}
