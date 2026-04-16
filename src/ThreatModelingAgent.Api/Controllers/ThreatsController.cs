using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ThreatModelingAgent.Api.Dtos;
using ThreatModelingAgent.Api.Security;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;
using ThreatModelingAgent.Domain.Interfaces;

namespace ThreatModelingAgent.Api.Controllers;

/// <summary>
/// Manages threats and the final analysis output for a completed job.
///
/// Endpoints:
///   GET    /v1/orgs/{orgId}/jobs/{jobId}/threats                    — list all threats
///   POST   /v1/orgs/{orgId}/jobs/{jobId}/threats                    — user-add a threat
///   PATCH  /v1/orgs/{orgId}/jobs/{jobId}/threats/{threatId}/status  — update threat status
///   GET    /v1/orgs/{orgId}/jobs/{jobId}/analysis                   — full analysis output
///
/// Authorization: org membership check on every request (defence-in-depth, RLS also active).
/// </summary>
[ApiController]
[Authorize]
[Route("v1/orgs/{orgId:guid}/jobs/{jobId:guid}")]
[EnableRateLimiting("api")]
public sealed class ThreatsController(
    IJobRepository jobs,
    IMembershipRepository memberships,
    IArchitectureRepository architectures,
    IThreatRepository threats,
    IBlobStorage blob,
    IAuditLogger audit,
    ILogger<ThreatsController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> AllowedStatuses =
        ["Open", "Accepted", "Mitigated", "Rejected"];

    // GET /v1/orgs/{orgId}/jobs/{jobId}/threats?elementId={guid}
    [HttpGet("threats")]
    public async Task<IActionResult> ListThreats(
        Guid orgId,
        Guid jobId,
        [FromQuery] Guid? elementId,
        [FromQuery] string[]? findingType,
        [FromQuery] string[]? status,
        [FromQuery] string[]? confidence,
        [FromQuery] string[]? method,
        [FromQuery] string[]? framework,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        var findingTypes = (findingType ?? [])
            .Select(v => Enum.TryParse<FindingType>(v, true, out var parsed) ? parsed : (FindingType?)null)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .Distinct()
            .ToArray();

        var statuses = (status ?? [])
            .Select(v => Enum.TryParse<ThreatStatus>(v, true, out var parsed) ? parsed : (ThreatStatus?)null)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .Distinct()
            .ToArray();

        var confidences = (confidence ?? [])
            .Select(v => Enum.TryParse<ConfidenceLevel>(v, true, out var parsed) ? parsed : (ConfidenceLevel?)null)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .Distinct()
            .ToArray();

        var methods = (method ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var frameworks = (framework ?? [])
            .Select(NormalizeFramework)
            .Where(v => v is not null)
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = await threats.ListByJobAsync(
            JobId.From(jobId),
            orgIdValue,
            elementId,
            findingTypes.Length > 0 ? findingTypes : null,
            statuses.Length > 0 ? statuses : null,
            confidences.Length > 0 ? confidences : null,
            methods.Length > 0 ? methods : null,
            frameworks.Length > 0 ? frameworks : null,
            ct);
        return Ok(new { data = items.Select(ThreatDto.From) });
    }

    // GET /v1/orgs/{orgId}/jobs/{jobId}/rejected-candidates
    [HttpGet("rejected-candidates")]
    public async Task<IActionResult> ListRejectedCandidates(
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

        var items = await threats.ListRejectedByJobAsync(JobId.From(jobId), orgIdValue, ct);
        return Ok(new { data = items.Select(RejectedCandidateDto.From) });
    }

    private static string? NormalizeFramework(string? framework)
    {
        if (string.IsNullOrWhiteSpace(framework)) return null;
        return framework.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_") switch
        {
            "stride" => "stride",
            "vast" => "vast",
            "pasta" => "pasta",
            "octave" or "ocatve" => "octave",
            "trike" => "trike",
            "mitre_attack" or "mitre_att&ck" or "mitre_attck" or "mitre" => "mitre_attack",
            "owasp_cumulus" => "owasp_cumulus",
            "owasp_cornucopia" or "owasp_conicopia" => "owasp_cornucopia",
            "owasp_top10" or "owasp_top_10" or "owasp" => "owasp_top10",
            "owasp_api_top10" or "owasp_api_top_10" or "owasp_api_security" => "owasp_api_top10",
            "asvs" => "asvs",
            "cis_controls" or "cis" or "cis_benchmarks" => "cis_controls",
            "ncsc" => "ncsc",
            "twelve_factor" or "12_factor" or "12factor" => "twelve_factor",
            _ => null
        };
    }

    // GET /v1/orgs/{orgId}/jobs/{jobId}/threats/{threatId}
    [HttpGet("threats/{threatId:guid}")]
    public async Task<IActionResult> GetThreat(Guid orgId, Guid jobId, Guid threatId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        // org_id scoping is defence-in-depth alongside RLS (CLAUDE.md §8.2 BOLA)
        var threat = await threats.GetByIdAsync(threatId, orgIdValue, ct);
        if (threat is null) return NotFound();

        // Verify threat belongs to this job
        if (threat.JobId != JobId.From(jobId)) return NotFound();

        return Ok(ThreatDto.From(threat));
    }

    // POST /v1/orgs/{orgId}/jobs/{jobId}/threats
    [HttpPost("threats")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> AddThreat(
        Guid orgId,
        Guid jobId,
        [FromBody] AddThreatRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        // GAP-TH7: user-added threats/concerns are allowed during AwaitingReview (pre-analysis)
        // as well as after analysis is complete or partial (spec §19 pre-analysis correction workflow)
        if (job.Status is not (JobStatus.Complete or JobStatus.Partial or JobStatus.AwaitingReview))
            return Conflict(new
            {
                code = "INVALID_JOB_STATUS",
                message = "Threats can only be added to jobs in AwaitingReview, Complete, or Partial status."
            });

        // Input validation (CLAUDE.md §6.3 — allow-list, explicit constraints)
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 500)
            return BadRequest(new { code = "INVALID_TITLE", message = "Title is required and must not exceed 500 characters." });
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { code = "INVALID_DESCRIPTION", message = "Description is required." });
        if (string.IsNullOrWhiteSpace(request.AttackScenario))
            return BadRequest(new { code = "INVALID_ATTACK_SCENARIO", message = "AttackScenario is required." });
        if (string.IsNullOrWhiteSpace(request.MethodCategory) || request.MethodCategory.Length > 100)
            return BadRequest(new { code = "INVALID_METHOD_CATEGORY", message = "MethodCategory is required and must not exceed 100 characters." });

        // GAP-TH2: enforce data-model §9 invariant — at least one element must be referenced
        if (request.AffectedElementIds is null || request.AffectedElementIds.Length == 0)
            return UnprocessableEntity(new
            {
                code = "ELEMENT_REQUIRED",
                message = "At least one affected element ID is required (spec data-model §9)."
            });

        // Enforce data-model §9: every affected element ID must belong to this job's architecture.
        var arch = await architectures.GetByJobIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (arch is null) return NotFound();

        var jobElements = await architectures.ListElementsAsync(arch.Id, orgIdValue, ct);
        var validElementIds = jobElements.Select(e => e.Id).ToHashSet();
        var invalidElementIds = request.AffectedElementIds.Where(id => !validElementIds.Contains(id)).ToArray();
        if (invalidElementIds.Length > 0)
            return UnprocessableEntity(new
            {
                code = "INVALID_AFFECTED_ELEMENT",
                message = "One or more affected elements do not belong to this job.",
                invalidElementIds
            });

        var identifier = await threats.NextIdentifierAsync(JobId.From(jobId), orgIdValue, ct);

        var threat = Threat.CreateUserAdded(
            jobId: JobId.From(jobId),
            orgId: orgIdValue,
            identifier: identifier,
            title: request.Title,
            methodCategory: request.MethodCategory,
            affectedElementIds: request.AffectedElementIds,
            description: request.Description,
            attackScenario: request.AttackScenario);

        await threats.AddAsync(threat, ct);
        await threats.SaveChangesAsync(ct);

        await audit.LogAsync("threat.added",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "threat",
            resourceId: threat.Id,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        logger.LogInformation(
            "User-added threat. JobId={JobId} ThreatId={ThreatId} Identifier={Identifier}",
            jobId, threat.Id, threat.Identifier);

        return CreatedAtAction(nameof(ListThreats), new { orgId, jobId }, ThreatDto.From(threat));
    }

    // PATCH /v1/orgs/{orgId}/jobs/{jobId}/threats/{threatId}/status
    [HttpPatch("threats/{threatId:guid}/status")]
    public async Task<IActionResult> UpdateThreatStatus(
        Guid orgId,
        Guid jobId,
        Guid threatId,
        [FromBody] PatchThreatStatusRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        // Validate status value against allow-list (CLAUDE.md §6.3)
        if (string.IsNullOrWhiteSpace(request.Status) || !AllowedStatuses.Contains(request.Status))
            return BadRequest(new
            {
                code = "INVALID_STATUS",
                message = $"Status must be one of: {string.Join(", ", AllowedStatuses)}."
            });

        // org_id on the threat itself is defence-in-depth alongside RLS (CLAUDE.md §8.2 BOLA)
        var threat = await threats.GetByIdAsync(threatId, orgIdValue, ct);
        if (threat is null) return NotFound();

        // Verify threat belongs to this job
        if (threat.JobId != JobId.From(jobId))
            return NotFound();

        var newStatus = Enum.Parse<ThreatStatus>(request.Status);
        threat.UpdateStatus(newStatus);
        await threats.SaveChangesAsync(ct);

        await audit.LogAsync("threat.status_updated",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "threat",
            resourceId: threatId,
            details: new { status = request.Status },
            ct: ct);

        return Ok(ThreatDto.From(threat));
    }

    // POST /v1/orgs/{orgId}/jobs/{jobId}/threats/{threatId}/notes
    [HttpPost("threats/{threatId:guid}/notes")]
    [EnableRateLimiting("strict")]
    public async Task<IActionResult> AddNote(
        Guid orgId,
        Guid jobId,
        Guid threatId,
        [FromBody] AddThreatNoteRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(new { code = "BODY_REQUIRED", message = "Note body is required." });

        if (request.Body.Length > 5000)
            return BadRequest(new { code = "BODY_TOO_LONG", message = "Note body must not exceed 5000 characters." });

        // org_id defence-in-depth (CLAUDE.md §8.2 BOLA)
        var threat = await threats.GetByIdAsync(threatId, orgIdValue, ct);
        if (threat is null) return NotFound();

        if (threat.JobId != JobId.From(jobId)) return NotFound();

        var note = ThreatNote.Create(threatId, orgIdValue, userId, request.Body);
        await threats.AddNoteAsync(note, ct);
        await threats.SaveChangesAsync(ct);

        await audit.LogAsync("threat.note_added",
            orgId: orgIdValue,
            userId: userId,
            resourceType: "threat_note",
            resourceId: note.Id,
            ct: ct);

        return CreatedAtAction(nameof(ListThreats), new { orgId, jobId },
            new { id = note.Id, authorId = note.CreatedBy.Value, createdAt = note.CreatedAt });
    }

    // GET /v1/orgs/{orgId}/jobs/{jobId}/export — GDPR right to portability (06-security §6.2)
    [HttpGet("export")]
    public async Task<IActionResult> ExportAnalysis(Guid orgId, Guid jobId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        if (job.Status is not (JobStatus.Complete or JobStatus.Partial))
            return Conflict(new
            {
                code = "ANALYSIS_NOT_READY",
                message = "Analysis output is only available once the job is complete or partial."
            });

        // Blob path is deterministic — org_id threads through to prevent cross-tenant access
        var blobPath = $"{orgId}/outputs/{jobId}/analysis.json";

        Stream blobStream;
        try
        {
            blobStream = await blob.DownloadAsync(blobPath, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analysis blob not found for export. JobId={JobId}", jobId);
            return NotFound(new { code = "ANALYSIS_NOT_FOUND", message = "Analysis output blob not found." });
        }

        // Stream blob directly as a file download — no re-serialization needed
        // Content is already valid JSON written by SynthesizeStage.PersistAsync
        Response.Headers.CacheControl = "no-store";  // CLAUDE.md §11.3
        return File(blobStream, "application/json", $"threat-model-{jobId}.json");
    }

    // GET /v1/orgs/{orgId}/jobs/{jobId}/analysis
    [HttpGet("analysis")]
    public async Task<IActionResult> GetAnalysis(Guid orgId, Guid jobId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orgIdValue = OrgId.From(orgId);

        if (!await memberships.HasOrgAccessAsync(orgIdValue, userId, ct: ct))
            return Forbid();

        var job = await jobs.GetByIdAsync(JobId.From(jobId), orgIdValue, ct);
        if (job is null) return NotFound();

        if (job.Status is not (JobStatus.Complete or JobStatus.Partial))
            return Conflict(new
            {
                code = "ANALYSIS_NOT_READY",
                message = "Analysis output is only available once the job is complete or partial."
            });

        // Blob path is deterministic: {orgId}/outputs/{jobId}/analysis.json
        // The org_id is threaded through the path — cross-tenant access is structurally prevented
        var blobPath = $"{orgId}/outputs/{jobId}/analysis.json";

        Stream blobStream;
        try
        {
            blobStream = await blob.DownloadAsync(blobPath, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analysis blob not found. JobId={JobId}", jobId);
            return NotFound(new { code = "ANALYSIS_NOT_FOUND", message = "Analysis output blob not found." });
        }

        // Parse and re-serialize via JsonDocument to ensure correct encoding for the HTTP context
        // (CLAUDE.md §7.3 — use framework serializers, not manual string construction)
        await using (blobStream)
        {
            using var ms = new MemoryStream();
            await blobStream.CopyToAsync(ms, ct);
            ms.Seek(0, SeekOrigin.Begin);

            JsonDocument doc;
            try
            {
                doc = await JsonDocument.ParseAsync(ms, cancellationToken: ct);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Analysis blob is not valid JSON. JobId={JobId}", jobId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { code = "ANALYSIS_CORRUPT", message = "Analysis output could not be read." });
            }

            return Ok(doc.RootElement);
        }
    }
}
