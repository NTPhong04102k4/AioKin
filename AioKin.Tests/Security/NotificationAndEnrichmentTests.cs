using System.Net;
using System.Net.Http.Json;
using AioKin.Models.InputModel.Admin;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Tests.Infrastructure;
using Xunit;

namespace AioKin.Tests.Security;

[Collection(ApiCollection.Name)]
public class NotificationAndEnrichmentTests
{
    private readonly ApiFixture _fixture;

    public NotificationAndEnrichmentTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task BroadcastUpdate_KhongCoToken_TraVe401()
    {
        var client = _fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/admin/notifications/broadcast-update", new BroadcastUpdateRequest
        {
            Version = "1.0.0",
            Title = "Test Update",
            Body = "Test Body"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BroadcastUpdate_CustomerToken_TraVe403()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var response = await user.Client.PostAsJsonAsync("/admin/notifications/broadcast-update", new BroadcastUpdateRequest
        {
            Version = "1.0.0",
            Title = "Test Update",
            Body = "Test Body"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
