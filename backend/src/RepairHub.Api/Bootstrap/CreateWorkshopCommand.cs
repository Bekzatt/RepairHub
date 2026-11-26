using System.Text;
using RepairHub.Application.Common;
using RepairHub.Application.Workshops;

namespace RepairHub.Api.Bootstrap;

/// <summary>
/// Server-side command that creates a workshop and its first Owner:
/// <code>dotnet RepairHub.Api.dll create-workshop --workshop-name "..." --owner-name "..." --owner-email "..."</code>
/// There is intentionally no HTTP equivalent: being able to run this command on the server is the authorization.
/// The password is read from RH_OWNER_PASSWORD, or from a hidden interactive prompt — never from arguments,
/// which would leak into shell history and process listings.
/// </summary>
internal static class CreateWorkshopCommand
{
    public const string Name = "create-workshop";
    private const string PasswordVariable = "RH_OWNER_PASSWORD";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        if (!TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            PrintUsage();
            return 64; // EX_USAGE
        }

        var password = Environment.GetEnvironmentVariable(PasswordVariable) ?? ReadPassword();
        if (password is null)
        {
            Console.Error.WriteLine($"No password provided. Set {PasswordVariable} or run interactively (docker compose run with a TTY).");
            return 64;
        }

        var request = new CreateWorkshopRequest
        {
            WorkshopName = options.GetValueOrDefault("workshop-name") ?? string.Empty,
            Address = options.GetValueOrDefault("address"),
            Phone = options.GetValueOrDefault("phone"),
            Currency = options.GetValueOrDefault("currency") ?? "KZT",
            TimeZone = options.GetValueOrDefault("timezone") ?? "Asia/Almaty",
            OwnerFullName = options.GetValueOrDefault("owner-name") ?? string.Empty,
            OwnerEmail = options.GetValueOrDefault("owner-email") ?? string.Empty,
            OwnerPassword = password,
            AllowAdditionalWorkshop = options.ContainsKey("allow-additional-workshop"),
        };

        await using var scope = services.CreateAsyncScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<WorkshopProvisioningService>();
        try
        {
            var result = await provisioning.CreateAsync(request, CancellationToken.None);
            Console.WriteLine($"Workshop created: {result.WorkshopId}");
            Console.WriteLine($"Owner created:    {result.OwnerId} ({request.OwnerEmail})");
            return 0;
        }
        catch (ValidationFailedException ex)
        {
            Console.Error.WriteLine("Invalid input:");
            foreach (var (field, messages) in ex.Errors)
                Console.Error.WriteLine($"  {field}: {string.Join(" ", messages)}");
            return 65; // EX_DATAERR
        }
        catch (ConflictException ex)
        {
            Console.Error.WriteLine($"Not created: {ex.Message}");
            return 75; // EX_TEMPFAIL-ish: state conflict, nothing changed
        }
    }

    private static readonly HashSet<string> ValueOptions =
        ["workshop-name", "owner-name", "owner-email", "address", "phone", "currency", "timezone"];
    private static readonly HashSet<string> FlagOptions = ["allow-additional-workshop"];
    private static readonly string[] RequiredOptions = ["workshop-name", "owner-name", "owner-email"];

    private static bool TryParse(string[] args, out Dictionary<string, string?> options, out string error)
    {
        options = [];
        error = string.Empty;
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i].StartsWith("--", StringComparison.Ordinal) ? args[i][2..] : null;
            if (name is not null && FlagOptions.Contains(name))
            {
                options[name] = null;
            }
            else if (name is not null && ValueOptions.Contains(name) && i + 1 < args.Length)
            {
                options[name] = args[++i];
            }
            else
            {
                error = $"Unknown or incomplete option '{args[i]}'.";
                return false;
            }
        }

        var parsed = options;
        var missing = RequiredOptions.Where(o => !parsed.ContainsKey(o)).ToArray();
        if (missing.Length > 0)
        {
            error = "Missing required option(s): " + string.Join(", ", missing.Select(m => "--" + m));
            return false;
        }
        return true;
    }

    private static string? ReadPassword()
    {
        if (Console.IsInputRedirected)
            return Console.In.ReadLine(); // e.g. piped from a secret manager

        var first = Prompt("Owner password: ");
        var second = Prompt("Repeat password: ");
        if (first != second)
        {
            Console.Error.WriteLine("Passwords do not match.");
            return null;
        }
        return first;
    }

    private static string Prompt(string label)
    {
        Console.Write(label);
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
        Console.WriteLine();
        return buffer.ToString();
    }

    private static void PrintUsage() => Console.Error.WriteLine("""
        Usage:
          create-workshop --workshop-name <name> --owner-name <full name> --owner-email <email>
                          [--address <text>] [--phone <text>] [--currency KZT] [--timezone Asia/Almaty]
                          [--allow-additional-workshop]

        The Owner password is read from RH_OWNER_PASSWORD or prompted for interactively.
        By default the command refuses to run if any workshop already exists.
        """);
}
