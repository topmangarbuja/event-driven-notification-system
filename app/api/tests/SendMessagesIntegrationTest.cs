using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace tests;

public class SendMessagesIntegrationTest(WebApplicationFactory<Program> factory): IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();
    
    [Fact]
    public async Task SendMessages_WithValidRequests_ReturnsOK()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/messages");
        request.Content = JsonContent.Create(new SendMessagesRequest(
            FullName: "John Doe",
            Message: "Hello, this is a test message.",
            Mobile: "0434567890",
            Email: "example@gmail.com"
        ));
        
        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
