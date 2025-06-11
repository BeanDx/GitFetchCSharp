using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;


public record GitHubUser(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("avatar_url")] string AvatarUrl,
    [property: JsonPropertyName("bio")] string? Bio,
    [property: JsonPropertyName("public_repos")] int PublicRepos,
    [property: JsonPropertyName("followers")] int Followers,
    [property: JsonPropertyName("following")] int Following,
    [property: JsonPropertyName("location")] string? Location
);

// usage man D:
public class FetchCommandSettings : CommandSettings
{
    [Description("Name of github user.")]
    [CommandArgument(0, "<USERNAME>")]
    public required string Username { get; init; }
}


public class FetchCommand : AsyncCommand<FetchCommandSettings>
{
    private static readonly HttpClient httpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "GithubFetch-CSharp-App" } }
    };

    public override async Task<int> ExecuteAsync(CommandContext context, FetchCommandSettings settings)
    {
        GitHubUser? user = null;
        int starredCount = 0;

        await AnsiConsole.Status()
            .StartAsync("Получение данных с GitHub API...", async ctx =>
            {
                try
                {
                    var userUrl = $"https://api.github.com/users/{settings.Username}";
                    user = await httpClient.GetFromJsonAsync<GitHubUser>(userUrl);
                    if (user is null) return;

                    var starredUrl = $"https://api.github.com/users/{settings.Username}/starred";
                    var starredRepos = await httpClient.GetFromJsonAsync<object[]>(starredUrl);
                    starredCount = starredRepos?.Length ?? 0;
                }
                catch (HttpRequestException e)
                {
                    AnsiConsole.MarkupLine($"[red]Error connection or API: {e.Message}[/]");
                    user = null;
                }
            });

        if (user is null)
        {
            AnsiConsole.MarkupLine($"[red]Failed to get data for user '{settings.Username}'.[/]");
            return 1;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();

        // --- ADAPTIVE OUTPUT ---
        string? termEnv = Environment.GetEnvironmentVariable("TERM");
        if (termEnv != null && termEnv.Contains("kitty", StringComparison.OrdinalIgnoreCase))
        {
            // for kitty terms: real pic
            await DisplayAvatarWithKitten(user.AvatarUrl);
            RenderProfileWithLeftPlaceholder(user, starredCount, new Text(""));
        }
        else
        {
            // for other terms: generate ascii art
            var asciiArt = await CreateAsciiArt(user.AvatarUrl);
            RenderProfileWithLeftPlaceholder(user, starredCount, asciiArt);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();

        return 0;
    }

    // Method for kitty
    private async Task DisplayAvatarWithKitten(string avatarUrl)
    {
        string? tempFilePath = null;
        try
        {
            byte[] imageBytes = await httpClient.GetByteArrayAsync(avatarUrl);
            tempFilePath = Path.GetTempFileName();
            await File.WriteAllBytesAsync(tempFilePath, imageBytes);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "kitten",
                Arguments = $"icat --align left --place 24x12@2x1 {tempFilePath}",
                UseShellExecute = false,
            };

            using var process = Process.Start(processStartInfo);
            if (process != null) await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (Exception) { }
        finally
        {
            if (tempFilePath != null && File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    // method for ascii art
    private async Task<IRenderable> CreateAsciiArt(string avatarUrl)
    {
        try
        {
            byte[] imageBytes = await httpClient.GetByteArrayAsync(avatarUrl);
            var image = new CanvasImage(imageBytes);
            
            image.MaxWidth(10); // art size
            
            return image;
        }
        catch (Exception)
        {
            return new Text("");
        }
    }

    private void RenderProfileWithLeftPlaceholder(GitHubUser user, int starredCount, IRenderable leftContent)
    {
        string githubUrl = $"{user.Login}@github.com";

        var textGrid = new Grid()
            .AddColumn()
            .AddColumn(new GridColumn().Padding(1, 0, 0, 0));

        textGrid.AddRow("[blue]Username:[/]", $"[bold]{user.Login}[/]");
        textGrid.AddRow("[yellow]Repos:[/]", user.PublicRepos.ToString());
        textGrid.AddRow("[green]Bio:[/]", user.Bio ?? "N/A");
        textGrid.AddRow("[red]From:[/]", user.Location ?? "Not Provided");
        textGrid.AddRow("[red]Followers:[/]", user.Followers.ToString());
        textGrid.AddRow("[blue]Following:[/]", user.Following.ToString());
        textGrid.AddRow("[yellow]Starred repos:[/]", starredCount.ToString());

        var content = new Rows(
            new Markup(githubUrl),
            new Markup(new string('-', githubUrl.Length)),
            textGrid
        );

        var mainLayout = new Grid()
            .Expand()
            .AddColumn(new GridColumn().Width(30).PadRight(0))
            .AddColumn(new GridColumn().PadLeft(0));

        mainLayout.AddRow(leftContent, content);
        AnsiConsole.Write(mainLayout);
    }
}

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var app = new CommandApp<FetchCommand>();
        app.Configure(config =>
        {
            config.SetApplicationName("githubfetch");
            config.ValidateExamples();
        });

        return await app.RunAsync(args);
    }
}