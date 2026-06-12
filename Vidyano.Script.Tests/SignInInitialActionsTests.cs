using System.Net;
using System.Net.Http;
using Vidyano;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Sign-in must not throw when GetApplication returns an initial PO that carries actions.
/// Drives a real Client over a canned HTTP response.
/// </summary>
public sealed class SignInInitialActionsTests
{
    // GetApplication response whose initial PO carries a non-empty actions array.
    private const string GetApplicationResponse = """
    {
      "authToken": "test-token",
      "userName": "admin",
      "application": {
        "type": "Application",
        "fullTypeName": "Vidyano.Application",
        "stateBehavior": "None",
        "attributes": [
          { "name": "Culture", "type": "String", "visibility": "Read", "value": "en-US" }
        ],
        "queries": [
          {
            "name": "ClientMessages",
            "result": {
              "columns": [ { "name": "Key", "type": "String" }, { "name": "Value", "type": "String" } ],
              "items": []
            }
          },
          {
            "name": "Actions",
            "result": {
              "columns": [
                { "name": "Name", "type": "String" },
                { "name": "DisplayName", "type": "String" },
                { "name": "IsPinned", "type": "Boolean" },
                { "name": "RefreshQueryOnCompleted", "type": "Boolean" },
                { "name": "Offset", "type": "Int32" },
                { "name": "Options", "type": "String" },
                { "name": "SelectionRule", "type": "String" }
              ],
              "items": [
                {
                  "id": "0",
                  "values": [
                    { "key": "Name", "value": "BulkEdit" },
                    { "key": "DisplayName", "value": "Bulk Edit" },
                    { "key": "IsPinned", "value": "false" },
                    { "key": "RefreshQueryOnCompleted", "value": "false" },
                    { "key": "Offset", "value": "0" },
                    { "key": "Options", "value": "" },
                    { "key": "SelectionRule", "value": "=1" }
                  ]
                }
              ]
            }
          }
        ]
      },
      "initial": {
        "type": "Initial",
        "fullTypeName": "Vidyano.Initial",
        "stateBehavior": "None",
        "isHidden": false,
        "actions": [ "Save" ],
        "attributes": []
      }
    }
    """;

    private sealed class CannedHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    [Fact]
    public async Task SignIn_WithInitialPoCarryingActions_Succeeds_AndPopulatesInitialAndActions()
    {
        var client = new Client(new HttpClient(new CannedHandler(GetApplicationResponse))) { Uri = "https://test.local/" };

        // Threw NullReferenceException before the fix.
        var application = await client.SignInUsingCredentialsAsync("admin", "password");

        // A completed sign-in proves Actions was populated: the trailing Actions["BulkEdit"]
        // lookup in SignInAsync would otherwise throw.
        Assert.NotNull(application);
        Assert.NotNull(client.Initial);
        Assert.Equal("Vidyano.Initial", client.Initial.FullTypeName);
        Assert.NotNull(client.Messages);
    }
}
