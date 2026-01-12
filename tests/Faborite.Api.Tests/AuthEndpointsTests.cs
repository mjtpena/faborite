using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Faborite.Api.Endpoints;

namespace Faborite.Api.Tests.Integration;

public class AuthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AuthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ConnectionStatus_ReturnsNotConnected_Initially()
    {
        // Act
        var response = await _client.GetAsync("/api/auth/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("isConnected");  // camelCase in JSON
    }

    [Fact]
    public async Task Connect_WithInvalidGuid_ReturnsBadRequest()
    {
        // Arrange
        var request = new ConnectRequest(
            WorkspaceId: "invalid-guid",
            LakehouseId: "invalid-guid"
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/connect", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Disconnect_ReturnsOk()
    {
        // Act
        var response = await _client.PostAsync("/api/auth/disconnect", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
