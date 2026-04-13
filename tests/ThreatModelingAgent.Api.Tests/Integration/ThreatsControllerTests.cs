using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Tests.Integration;

[Collection("Integration")]
public sealed class ThreatsControllerTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ThreatsControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Get single threat ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetThreat_HappyPath_ReturnsThreat()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Get Threat Happy");
        JobId jobId = default!;
        Guid threatId = default;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Get Threat Job");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            job.Transition(JobStatus.Classifying);
            job.Transition(JobStatus.Analyzing);
            job.Transition(JobStatus.Synthesizing);
            job.Transition(JobStatus.Complete);
            db.Jobs.Add(job);

            var threat = Threat.CreateFromPipeline(
                job.Id, orgId, "T-042", "SSRF via image fetch", "SSRF",
                [], "Attacker controls image URL", "Via upload endpoint",
                null, [], null, null, null, null,
                ConfidenceLevel.High, [], EvidenceStrength.Direct, null, FindingType.Confirmed);
            db.Threats.Add(threat);

            await db.SaveChangesAsync();
            jobId = job.Id;
            threatId = threat.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/threats/{threatId}");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("identifier").GetString().Should().Be("T-042");
        doc.RootElement.GetProperty("title").GetString().Should().Be("SSRF via image fetch");
    }

    [Fact]
    public async Task GetThreat_CrossOrg_Returns404()
    {
        var (orgAId, userAId) = await _factory.SeedOrgAndOwnerAsync("Get Threat CrossOrg A");
        var (orgBId, userBId) = await _factory.SeedOrgAndOwnerAsync("Get Threat CrossOrg B");
        JobId jobId = default!;
        Guid threatId = default;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgAId, userAId, "OrgA Threat");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            job.Transition(JobStatus.Classifying);
            job.Transition(JobStatus.Analyzing);
            job.Transition(JobStatus.Synthesizing);
            job.Transition(JobStatus.Complete);
            db.Jobs.Add(job);

            var threat = Threat.CreateFromPipeline(
                job.Id, orgAId, "T-001", "Title", "Cat", [], "Desc", "Attack",
                null, [], null, null, null, null,
                ConfidenceLevel.Medium, [], EvidenceStrength.Inferred, null, FindingType.Confirmed);
            db.Threats.Add(threat);

            await db.SaveChangesAsync();
            jobId = job.Id;
            threatId = threat.Id;
        });

        // User B tries to fetch org A's threat via their own org context
        var clientB = _factory.CreateAuthenticatedClient(userBId, orgBId);
        var response = await clientB.GetAsync($"/v1/orgs/{orgBId.Value}/jobs/{jobId.Value}/threats/{threatId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetThreat_WrongJob_Returns404()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Get Threat WrongJob");
        JobId jobId = default!;
        JobId otherJobId = default!;
        Guid threatId = default;

        await _factory.SeedAsync(async db =>
        {
            // Threat belongs to job 1, but we'll fetch with job 2's ID
            var job1 = Job.Create(orgId, userId, "Job 1");
            job1.Transition(JobStatus.Parsing);
            job1.Transition(JobStatus.Normalizing);
            job1.Transition(JobStatus.AwaitingReview);
            job1.Transition(JobStatus.Classifying);
            job1.Transition(JobStatus.Analyzing);
            job1.Transition(JobStatus.Synthesizing);
            job1.Transition(JobStatus.Complete);

            var job2 = Job.Create(orgId, userId, "Job 2");
            job2.Transition(JobStatus.Parsing);
            job2.Transition(JobStatus.Normalizing);
            job2.Transition(JobStatus.AwaitingReview);
            job2.Transition(JobStatus.Classifying);
            job2.Transition(JobStatus.Analyzing);
            job2.Transition(JobStatus.Synthesizing);
            job2.Transition(JobStatus.Complete);

            db.Jobs.AddRange(job1, job2);

            var threat = Threat.CreateFromPipeline(
                job1.Id, orgId, "T-001", "Title", "Cat", [], "Desc", "Attack",
                null, [], null, null, null, null,
                ConfidenceLevel.Low, [], EvidenceStrength.AssumptionDependent, null, FindingType.Conditional);
            db.Threats.Add(threat);

            await db.SaveChangesAsync();
            jobId = job1.Id;
            otherJobId = job2.Id;
            threatId = threat.Id;
        });

        // Fetch threat T using job2's ID — should 404 because job2 doesn't own it
        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs/{otherJobId.Value}/threats/{threatId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── List threats ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListThreats_HappyPath_ReturnsThreats()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Threats List Happy");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Threat Job");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            job.Transition(JobStatus.Classifying);
            job.Transition(JobStatus.Analyzing);
            job.Transition(JobStatus.Synthesizing);
            job.Transition(JobStatus.Complete);
            db.Jobs.Add(job);

            var threat = Threat.CreateFromPipeline(
                job.Id, orgId, "T-001", "SQL Injection", "Injection",
                [], "Attacker injects SQL.", "Via login form",
                null, [], null, null, null, null,
                ConfidenceLevel.High, [], EvidenceStrength.Direct, null, FindingType.Confirmed);
            db.Threats.Add(threat);

            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/threats");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data").EnumerateArray().ToList();
        data.Should().HaveCount(1);
        data[0].GetProperty("identifier").GetString().Should().Be("T-001");
    }

    [Fact]
    public async Task ListThreats_CrossOrg_Returns403()
    {
        var (orgAId, userAId) = await _factory.SeedOrgAndOwnerAsync("Threats CrossOrg A");
        var (orgBId, userBId) = await _factory.SeedOrgAndOwnerAsync("Threats CrossOrg B");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgAId, userAId, "OrgA Threat Job");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            job.Transition(JobStatus.Classifying);
            job.Transition(JobStatus.Analyzing);
            job.Transition(JobStatus.Synthesizing);
            job.Transition(JobStatus.Complete);
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var clientB = _factory.CreateAuthenticatedClient(userBId, orgBId);
        var response = await clientB.GetAsync($"/v1/orgs/{orgBId.Value}/jobs/{jobId.Value}/threats");

        // User B has no job with that ID in their org — 404
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Add threat ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddThreat_HappyPath_Returns201()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Add Threat Happy");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Complete Job");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            job.Transition(JobStatus.Classifying);
            job.Transition(JobStatus.Analyzing);
            job.Transition(JobStatus.Synthesizing);
            job.Transition(JobStatus.Complete);
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = JsonSerializer.Serialize(new
        {
            title = "New Threat",
            description = "Attack description",
            attackScenario = "Via API",
            methodCategory = "Injection"
        });

        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/threats",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("title").GetString().Should().Be("New Threat");
    }

    [Fact]
    public async Task AddThreat_WrongJobStatus_Returns409()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Add Threat WrongStatus");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            // Job is still Pending — cannot add threats
            var job = Job.Create(orgId, userId, "Pending Job");
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = JsonSerializer.Serialize(new
        {
            title = "Threat",
            description = "desc",
            attackScenario = "scenario",
            methodCategory = "Injection"
        });

        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/threats",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INVALID_JOB_STATUS");
    }

    [Fact]
    public async Task AddThreat_MissingTitle_Returns400()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Add Threat MissingTitle");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Complete Job 2");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            job.Transition(JobStatus.Classifying);
            job.Transition(JobStatus.Analyzing);
            job.Transition(JobStatus.Synthesizing);
            job.Transition(JobStatus.Complete);
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        // title is empty
        var body = JsonSerializer.Serialize(new
        {
            title = "",
            description = "desc",
            attackScenario = "scenario",
            methodCategory = "Injection"
        });

        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/threats",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INVALID_TITLE");
    }

    // ── PATCH threat status ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Open")]
    [InlineData("Accepted")]
    [InlineData("Mitigated")]
    [InlineData("Rejected")]
    public async Task PatchThreatStatus_AllowedStatus_Returns200(string status)
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync($"Patch Status {status}");
        JobId jobId = default!;
        Guid threatId = default;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, $"Job for {status}");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            job.Transition(JobStatus.Classifying);
            job.Transition(JobStatus.Analyzing);
            job.Transition(JobStatus.Synthesizing);
            job.Transition(JobStatus.Complete);
            db.Jobs.Add(job);

            var threat = Threat.CreateFromPipeline(
                job.Id, orgId, "T-001", "Title", "Category", [], "Desc", "Attack",
                null, [], null, null, null, null,
                ConfidenceLevel.Medium, [], EvidenceStrength.Inferred, null, FindingType.Confirmed);
            db.Threats.Add(threat);

            await db.SaveChangesAsync();
            jobId = job.Id;
            threatId = threat.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = JsonSerializer.Serialize(new { status });

        var response = await client.PatchAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/threats/{threatId}/status",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be(status);
    }

    [Fact]
    public async Task PatchThreatStatus_InvalidStatus_Returns400()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Patch Invalid Status");
        JobId jobId = default!;
        Guid threatId = default;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Job Invalid Status");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            job.Transition(JobStatus.Classifying);
            job.Transition(JobStatus.Analyzing);
            job.Transition(JobStatus.Synthesizing);
            job.Transition(JobStatus.Complete);
            db.Jobs.Add(job);

            var threat = Threat.CreateFromPipeline(
                job.Id, orgId, "T-001", "T", "C", [], "D", "A",
                null, [], null, null, null, null,
                ConfidenceLevel.Low, [], EvidenceStrength.Inferred, null, FindingType.Conditional);
            db.Threats.Add(threat);

            await db.SaveChangesAsync();
            jobId = job.Id;
            threatId = threat.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = JsonSerializer.Serialize(new { status = "NotAValidStatus" });

        var response = await client.PatchAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/threats/{threatId}/status",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INVALID_STATUS");
    }

    // ── Export ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAnalysis_CompleteJob_Returns200WithJsonFile()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Export Happy");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Export Job");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            job.Transition(JobStatus.Classifying);
            job.Transition(JobStatus.Analyzing);
            job.Transition(JobStatus.Synthesizing);
            job.Transition(JobStatus.Complete);
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var analysisJson = """{"threats":[],"summary":"test"}""";
        _factory.BlobStorage.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new System.IO.MemoryStream(
                Encoding.UTF8.GetBytes(analysisJson))));

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/export");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task ExportAnalysis_IncompleteJob_Returns409()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Export Incomplete");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            // Job not yet complete
            var job = Job.Create(orgId, userId, "Incomplete Export Job");
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/export");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ANALYSIS_NOT_READY");
    }
}
