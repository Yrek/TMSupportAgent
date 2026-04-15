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
public sealed class ArchitecturesControllerTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ArchitecturesControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── GET architecture ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetArchitecture_HappyPath_ReturnsArchitecture()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Arch Get Happy");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Arch Job");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            db.Jobs.Add(job);

            var arch = Architecture.Create(job.Id, orgId, "Test system", ["web"], "[]", "[]", "[]");
            db.Architectures.Add(arch);

            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/architecture");

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("systemPurpose").GetString().Should().Be("Test system");
    }

    [Fact]
    public async Task GetArchitecture_NoArchitecture_Returns404()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Arch Get 404");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "No Arch Job");
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/architecture");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetArchitecture_CrossOrg_Returns404()
    {
        var (orgAId, userAId) = await _factory.SeedOrgAndOwnerAsync("Arch CrossOrg A");
        var (orgBId, userBId) = await _factory.SeedOrgAndOwnerAsync("Arch CrossOrg B");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgAId, userAId, "OrgA Arch Job");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            db.Jobs.Add(job);
            db.Architectures.Add(Architecture.Create(job.Id, orgAId, "OrgA sys", [], "[]", "[]", "[]"));
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var clientB = _factory.CreateAuthenticatedClient(userBId, orgBId);
        var response = await clientB.GetAsync($"/v1/orgs/{orgBId.Value}/jobs/{jobId.Value}/architecture");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Confirm architecture ──────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmArchitecture_HappyPath_Returns200AndEnqueuesPhase2()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Arch Confirm Happy");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Confirm Job");
            job.SetArtifact($"{orgId}/uploads/test.png", "image");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            db.Jobs.Add(job);
            db.Architectures.Add(Architecture.Create(job.Id, orgId, "Confirm sys", [], "[]", "[]", "[]"));
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        _factory.JobQueue.EnqueueAnalyzePhaseAsync(
            Arg.Any<JobId>(), Arg.Any<OrgId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/architecture/confirm",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        await _factory.JobQueue.Received(1).EnqueueAnalyzePhaseAsync(
            Arg.Any<JobId>(), Arg.Any<OrgId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmArchitecture_WrongStatus_Returns409()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Arch Confirm WrongStatus");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            // Job is still Pending — not AwaitingReview
            var job = Job.Create(orgId, userId, "Wrong Status Job");
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/architecture/confirm",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INVALID_JOB_STATUS");
    }

    [Fact]
    public async Task ConfirmArchitecture_AlreadyConfirmed_Returns409()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Arch Already Confirmed");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Already Confirmed Job");
            job.SetArtifact($"{orgId}/uploads/test.png", "image");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            db.Jobs.Add(job);

            var arch = Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]");
            arch.Confirm(userId);
            db.Architectures.Add(arch);

            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/architecture/confirm",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ALREADY_CONFIRMED");
    }

    // ── PATCH element ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PatchElement_HappyPath_Returns200()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Patch Element Happy");
        JobId jobId = default!;
        Guid elementId = default;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Patch Job");
            job.SetArtifact($"{orgId}/uploads/test.png", "image");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            db.Jobs.Add(job);

            var arch = Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]");
            db.Architectures.Add(arch);

            var element = ArchitectureElement.CreateExtracted(arch.Id, orgId, ElementType.Component, "Original Name", "desc", "{}", ConfidenceLevel.High);
            db.ArchitectureElements.Add(element);

            await db.SaveChangesAsync();
            jobId = job.Id;
            elementId = element.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var patch = new StringContent(
            JsonSerializer.Serialize(new { name = "Updated Name", description = "Updated desc" }),
            Encoding.UTF8, "application/json");

        var response = await client.PatchAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/elements/{elementId}", patch);

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("name").GetString().Should().Be("Updated Name");
    }

    [Fact]
    public async Task PatchElement_WrongJobStatus_Returns409()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Patch Element WrongStatus");
        JobId jobId = default!;
        Guid elementId = default;

        await _factory.SeedAsync(async db =>
        {
            // Job is Pending — not AwaitingReview
            var job = Job.Create(orgId, userId, "Pending Job Patch");
            db.Jobs.Add(job);

            var arch = Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]");
            db.Architectures.Add(arch);

            var element = ArchitectureElement.CreateExtracted(arch.Id, orgId, ElementType.Component, "Name", "desc", "{}", ConfidenceLevel.Medium);
            db.ArchitectureElements.Add(element);

            await db.SaveChangesAsync();
            jobId = job.Id;
            elementId = element.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var patch = new StringContent(
            JsonSerializer.Serialize(new { name = "New Name", description = "desc" }),
            Encoding.UTF8, "application/json");

        var response = await client.PatchAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/elements/{elementId}", patch);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INVALID_JOB_STATUS");
    }

    // ── Add element ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AddElement_HappyPath_Returns201()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Add Element Happy");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Add Element Job");
            job.Transition(JobStatus.AwaitingReview);
            db.Jobs.Add(job);
            db.Architectures.Add(Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]"));
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = new StringContent(
            JsonSerializer.Serialize(new { elementType = "Component", name = "Payment Service", description = "Handles payments" }),
            Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/elements", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("name").GetString().Should().Be("Payment Service");
        doc.RootElement.GetProperty("source").GetString().Should().Be("user_added");
    }

    [Fact]
    public async Task AddElement_WrongJobStatus_Returns409()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Add Element WrongStatus");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Pending Job Add Element");
            db.Jobs.Add(job);
            db.Architectures.Add(Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]"));
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = new StringContent(
            JsonSerializer.Serialize(new { elementType = "Component", name = "X" }),
            Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/elements", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INVALID_JOB_STATUS");
    }

    [Fact]
    public async Task AddElement_InvalidElementType_Returns422()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Add Element InvalidType");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Element Type Job");
            job.Transition(JobStatus.AwaitingReview);
            db.Jobs.Add(job);
            db.Architectures.Add(Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]"));
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = new StringContent(
            JsonSerializer.Serialize(new { elementType = "NotARealType", name = "X" }),
            Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/elements", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INVALID_ELEMENT_TYPE");
    }

    // ── Delete element ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteElement_HappyPath_Returns204()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Delete Element Happy");
        JobId jobId = default!;
        Guid elementId = default;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Delete Element Job");
            job.Transition(JobStatus.AwaitingReview);
            db.Jobs.Add(job);

            var arch = Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]");
            db.Architectures.Add(arch);

            var element = ArchitectureElement.CreateUserAdded(arch.Id, orgId, ElementType.Component, "To Delete", null, "{}");
            db.ArchitectureElements.Add(element);

            await db.SaveChangesAsync();
            jobId = job.Id;
            elementId = element.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.DeleteAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/elements/{elementId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteElement_WrongJobStatus_Returns409()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Delete Element WrongStatus");
        JobId jobId = default!;
        Guid elementId = default;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Pending Delete Element Job");
            db.Jobs.Add(job);

            var arch = Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]");
            db.Architectures.Add(arch);

            var element = ArchitectureElement.CreateUserAdded(arch.Id, orgId, ElementType.Component, "Elem", null, "{}");
            db.ArchitectureElements.Add(element);

            await db.SaveChangesAsync();
            jobId = job.Id;
            elementId = element.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.DeleteAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/elements/{elementId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INVALID_JOB_STATUS");
    }

    // ── Correct element ───────────────────────────────────────────────────────

    [Fact]
    public async Task CorrectElement_HappyPath_Returns200WithCorrectionInResponse()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Correct Element Happy");
        JobId jobId = default!;
        Guid elementId = default;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Correct Element Job");
            job.Transition(JobStatus.AwaitingReview);
            db.Jobs.Add(job);

            var arch = Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]");
            db.Architectures.Add(arch);

            var element = ArchitectureElement.CreateExtracted(arch.Id, orgId, ElementType.Component, "API Gateway", "desc", "{}", ConfidenceLevel.High);
            db.ArchitectureElements.Add(element);

            await db.SaveChangesAsync();
            jobId = job.Id;
            elementId = element.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = new StringContent(
            JsonSerializer.Serialize(new
            {
                correctionType = "Update",
                fieldName = "name",
                originalValue = "API Gateway",
                correctedValue = "API Gateway (Nginx)",
                note = "Extracted name was incomplete"
            }),
            Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/elements/{elementId}", body);

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var corrections = doc.RootElement.GetProperty("corrections").EnumerateArray().ToList();
        corrections.Should().HaveCount(1);
        corrections[0].GetProperty("correctionType").GetString().Should().Be("Update");
        corrections[0].GetProperty("fieldName").GetString().Should().Be("name");
    }

    [Fact]
    public async Task CorrectElement_InvalidCorrectionType_Returns422()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Correct Element InvalidType");
        JobId jobId = default!;
        Guid elementId = default;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Invalid Correction Job");
            job.Transition(JobStatus.AwaitingReview);
            db.Jobs.Add(job);

            var arch = Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]");
            db.Architectures.Add(arch);

            var element = ArchitectureElement.CreateExtracted(arch.Id, orgId, ElementType.Component, "X", null, "{}", ConfidenceLevel.Low);
            db.ArchitectureElements.Add(element);

            await db.SaveChangesAsync();
            jobId = job.Id;
            elementId = element.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = new StringContent(
            JsonSerializer.Serialize(new { correctionType = "NotAType" }),
            Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/elements/{elementId}", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INVALID_CORRECTION_TYPE");
    }

    [Fact]
    public async Task CorrectElement_UpdateWithoutFieldName_Returns422()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Correct Element MissingField");
        JobId jobId = default!;
        Guid elementId = default;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Missing FieldName Job");
            job.Transition(JobStatus.AwaitingReview);
            db.Jobs.Add(job);

            var arch = Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]");
            db.Architectures.Add(arch);

            var element = ArchitectureElement.CreateExtracted(arch.Id, orgId, ElementType.Component, "X", null, "{}", ConfidenceLevel.Medium);
            db.ArchitectureElements.Add(element);

            await db.SaveChangesAsync();
            jobId = job.Id;
            elementId = element.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        // correctionType=Update but no fieldName
        var body = new StringContent(
            JsonSerializer.Serialize(new { correctionType = "Update", correctedValue = "new" }),
            Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/elements/{elementId}", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("FIELD_NAME_REQUIRED");
    }

    [Fact]
    public async Task CorrectElement_WrongJobStatus_Returns409()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Correct Element WrongStatus");
        JobId jobId = default!;
        Guid elementId = default;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Pending Correct Job");
            db.Jobs.Add(job);

            var arch = Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]");
            db.Architectures.Add(arch);

            var element = ArchitectureElement.CreateExtracted(arch.Id, orgId, ElementType.Component, "X", null, "{}", ConfidenceLevel.High);
            db.ArchitectureElements.Add(element);

            await db.SaveChangesAsync();
            jobId = job.Id;
            elementId = element.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var body = new StringContent(
            JsonSerializer.Serialize(new { correctionType = "MarkConfirmed" }),
            Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/elements/{elementId}", body);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INVALID_JOB_STATUS");
    }

    // ── Reanalyze job ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReanalyzeJob_HappyPath_Returns200AndResetsToAwaitingReview()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Reanalyze Happy");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Complete Job Reanalyze");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            job.Transition(JobStatus.Classifying);
            job.Transition(JobStatus.Analyzing);
            job.Transition(JobStatus.Synthesizing);
            job.Transition(JobStatus.Complete);
            db.Jobs.Add(job);

            var arch = Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]");
            arch.Confirm(userId);
            db.Architectures.Add(arch);

            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/architecture/reanalyze",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("AwaitingReview");
    }

    [Fact]
    public async Task ReanalyzeJob_WrongStatus_Returns409()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Reanalyze WrongStatus");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            // Job is still Pending — cannot reanalyze
            var job = Job.Create(orgId, userId, "Pending Reanalyze Job");
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/architecture/reanalyze",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("INVALID_JOB_STATUS");
    }

    [Fact]
    public async Task ReanalyzeJob_PartialJob_Returns200()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Reanalyze Partial");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "Partial Job Reanalyze");
            job.Transition(JobStatus.Parsing);
            job.Transition(JobStatus.Normalizing);
            job.Transition(JobStatus.AwaitingReview);
            job.Transition(JobStatus.Classifying);
            job.Transition(JobStatus.Analyzing);
            job.Transition(JobStatus.Synthesizing);
            job.Transition(JobStatus.Partial);
            db.Jobs.Add(job);

            var arch = Architecture.Create(job.Id, orgId, "sys", [], "[]", "[]", "[]");
            arch.Confirm(userId);
            db.Architectures.Add(arch);

            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);
        var response = await client.PostAsync(
            $"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}/architecture/reanalyze",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("AwaitingReview");
    }
}
