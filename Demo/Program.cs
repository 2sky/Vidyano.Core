using Vidyano;

Console.WriteLine("Vidyano.Core Demo Application");
Console.WriteLine("==============================");
Console.WriteLine("Connecting to demo.vidyano.com...\n");

try
{
    // Create client instance
    var client = new Client()
    {
        Uri = "https://demo.vidyano.com/"
    };

    var clientData = await client.GetClientData();

    // The demo service allows anonymous access
    // Sign in without credentials (anonymous)
    var application = await client.SignInUsingCredentialsAsync(clientData.DefaultUserName, null);

    Console.WriteLine("Successfully connected to Vidyano demo service!");

    if (application != null)
    {
        Console.WriteLine($"Application: {application.ObjectId ?? "Demo"}");
        Console.WriteLine($"User: {client.User ?? "Guest"}");

        // Display application culture if available
        var cultureAttr = application.Attributes.FirstOrDefault(a => a.Name == "Culture");
        if (cultureAttr != null)
        {
            Console.WriteLine($"Culture: {cultureAttr.Value}");
        }

        // List available queries
        if (application.Queries != null && application.Queries.Count > 0)
        {
            Console.WriteLine("\nAvailable Queries:");
            Console.WriteLine("------------------");

            foreach (var query in application.Queries.Take(10))
            {
                Console.WriteLine($"  - {query.Key}");
            }

            // Execute the first available query as a demonstration
            var firstQuery = application.Queries.Values.FirstOrDefault();
            if (firstQuery != null)
            {
                Console.WriteLine($"\nExecuting query: {firstQuery.Name}");
                Console.WriteLine("------------------");

                // Search/execute the query
                if (!firstQuery.HasSearched)
                    await firstQuery.SearchTextAsync(string.Empty);

                Console.WriteLine($"Total items: {firstQuery.TotalItems}");
                Console.WriteLine($"Retrieved items: {firstQuery.Count}");

                // Display first few items
                if (firstQuery.Count > 0)
                {
                    Console.WriteLine("\nFirst few results:");
                    int count = Math.Min(5, firstQuery.Count);

                    for (int i = 0; i < count; i++)
                    {
                        var item = firstQuery[i];
                        Console.WriteLine($"\nItem {i + 1}:");

                        // Display ID if available
                        Console.WriteLine($"  ID: {item.Id}");

                        // Try to display some common field names that might exist
                        var possibleFields = new[] { "Name", "Title", "Description", "Code", "Value" };
                        foreach (var field in possibleFields)
                        {
                            try
                            {
                                var value = item[field];
                                if (value != null)
                                {
                                    Console.WriteLine($"  {field}: {value}");
                                }
                            }
                            catch
                            {
                                // Field doesn't exist, continue
                            }
                        }
                    }
                }
            }
        }
        else
        {
            Console.WriteLine("\nNo queries available or not accessible as guest.");
        }

        // Display some attributes from the application object
        Console.WriteLine("\nApplication Attributes:");
        Console.WriteLine("------------------------");
        int displayedAttrs = 0;
        foreach (var attr in application.Attributes)
        {
            if (displayedAttrs >= 5) break;
            if (attr.Value != null)
            {
                Console.WriteLine($"  {attr.Name}: {attr.Value}");
                displayedAttrs++;
            }
        }
    }

    Console.WriteLine("\n✅ Demo completed successfully!");
    Console.WriteLine("\nThis demo shows basic connectivity to a Vidyano service.");
    Console.WriteLine("For authenticated access and more features, use SignInUsingCredentialsAsync with actual credentials.");

}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
    }
}

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();
