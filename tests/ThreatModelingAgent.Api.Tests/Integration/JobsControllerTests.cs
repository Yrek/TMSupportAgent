using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Enums;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Api.Tests.Integration;

[Collection("Integration")]
public sealed class JobsControllerTests
{
    private readonly ApiWebApplicationFactory _factory;

    public JobsControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Submit job ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitJob_HappyPath_Returns202WithJobId()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Submit Happy Org");
        _factory.BlobStorage.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("uploaded-path"));
        _factory.JobQueue.EnqueueParsePhaseAsync(
                Arg.Any<JobId>(),
                Arg.Any<OrgId>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        using var content = BuildMultipartJob("test.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png");
        var response = await client.PostAsync($"/v1/orgs/{orgId.Value}/jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("id", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SubmitJob_FileTooLarge_Returns413()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Submit TooLarge Org");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        // 11 MB file (just over the 10 MB limit)
        var largeContent = new byte[11 * 1024 * 1024];
        using var content = BuildMultipartJob("big.png", largeContent, "image/png");

        var response = await client.PostAsync($"/v1/orgs/{orgId.Value}/jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ARTIFACT_TOO_LARGE");
    }

    [Fact]
    public async Task SubmitJob_UnsupportedExtension_Returns415()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Submit BadExt Org");
        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        using var content = BuildMultipartJob("malware.exe", new byte[] { 0x4D, 0x5A }, "application/octet-stream");

        var response = await client.PostAsync($"/v1/orgs/{orgId.Value}/jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("UNSUPPORTED_ARTIFACT_TYPE");
    }

    [Fact]
    public async Task SubmitJob_NoMembership_Returns403()
    {
        var (orgId, _) = await _factory.SeedOrgAndOwnerAsync("Submit NoMember Org");
        // Create a user who is NOT a member of this org
        var (_, outsiderId) = await _factory.SeedOrgAndOwnerAsync("Other Org For Submit");

        var client = _factory.CreateAuthenticatedClient(outsiderId, orgId);
        using var content = BuildMultipartJob("test.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png");

        var response = await client.PostAsync($"/v1/orgs/{orgId.Value}/jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── List jobs ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListJobs_HappyPath_ReturnsJobList()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("List Happy Org");

        await _factory.SeedAsync(async db =>
        {
            db.Jobs.Add(Job.Create(orgId, userId, "Alpha"));
            db.Jobs.Add(Job.Create(orgId, userId, "Beta"));
            await db.SaveChangesAsync();
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs");
        response.EnsureSuccessStatusCode();

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data").EnumerateArray().ToList();
        data.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListJobs_StatusFilter_ReturnsMatchingJobsOnly()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("List Filter Org");

        await _factory.SeedAsync(async db =>
        {
            var j1 = Job.Create(orgId, userId, "Pending Job");
            var j2 = Job.Create(orgId, userId, "Parsing Job");
            j2.Transition(JobStatus.Parsing);
            db.Jobs.AddRange(j1, j2);
            await db.SaveChangesAsync();
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs?status=Pending");
        response.EnsureSuccessStatusCode();

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data").EnumerateArray().ToList();
        data.Should().HaveCount(1);
        data[0].GetProperty("status").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task ListJobs_PageSize_IsCapped()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("List PageSize Org");

        await _factory.SeedAsync(async db =>
        {
            for (var i = 0; i < 5; i++)
                db.Jobs.Add(Job.Create(orgId, userId, $"Job {i}"));
            await db.SaveChangesAsync();
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        // Request far more than actually exist — should return up to the existing count, capped at 100
        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs?pageSize=200");
        response.EnsureSuccessStatusCode();

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // 5 jobs seeded; pageSize was clamped to 100 but only 5 exist
        var data = doc.RootElement.GetProperty("data").EnumerateArray().ToList();
        data.Should().HaveCount(5);
    }

    // ── Get job ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetJob_HappyPath_ReturnsJobDetail()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Get Happy Org");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "My Job");
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.GetAsync($"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}");
        response.EnsureSuccessStatusCode();

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("title").GetString().Should().Be("My Job");
    }

    [Fact]
    public async Task GetJob_CrossOrg_Returns404()
    {
        var (orgAId, userAId) = await _factory.SeedOrgAndOwnerAsync("Get CrossOrg A");
        var (orgBId, userBId) = await _factory.SeedOrgAndOwnerAsync("Get CrossOrg B");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgAId, userAId, "Org A Job");
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var clientB = _factory.CreateAuthenticatedClient(userBId, orgBId);

        var response = await clientB.GetAsync($"/v1/orgs/{orgBId.Value}/jobs/{jobId.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Delete job ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteJob_HappyPath_Returns204()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Delete Happy Org");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "To Delete");
            job.Transition(JobStatus.Failed); // terminal => deletable
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        _factory.BlobStorage.DeleteByPrefixAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.DeleteAsync($"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteJob_InProgressJob_Returns409()
    {
        var (orgId, userId) = await _factory.SeedOrgAndOwnerAsync("Delete InProgress Org");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgId, userId, "In Progress");
            job.Transition(JobStatus.Parsing);  // now IsInProgress = true
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var client = _factory.CreateAuthenticatedClient(userId, orgId);

        var response = await client.DeleteAsync($"/v1/orgs/{orgId.Value}/jobs/{jobId.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("JOB_IN_PROGRESS");
    }

    [Fact]
    public async Task DeleteJob_CrossOrg_Returns404()
    {
        var (orgAId, userAId) = await _factory.SeedOrgAndOwnerAsync("Delete CrossOrg A");
        var (orgBId, userBId) = await _factory.SeedOrgAndOwnerAsync("Delete CrossOrg B");
        JobId jobId = default!;

        await _factory.SeedAsync(async db =>
        {
            var job = Job.Create(orgAId, userAId, "Org A job to delete");
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        });

        var clientB = _factory.CreateAuthenticatedClient(userBId, orgBId);

        var response = await clientB.DeleteAsync($"/v1/orgs/{orgBId.Value}/jobs/{jobId.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MultipartFormDataContent BuildMultipartJob(
        string filename, byte[] fileBytes, string contentType, string title = "Test Job")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "title");

        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "artifact", filename);

        return form;
    }
}
