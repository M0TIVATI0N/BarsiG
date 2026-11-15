using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceFlow.Desktop;

/// <summary>
/// Hosts the server of BarsiG inside the desktop application.
/// The server listens on a free port of the local machine, so the user never has to type
/// an address. When the configuration contains the address of another server, the desktop
/// application connects to that one and starts no server of its own.
/// </summary>
public sealed class ServerHost : IAsyncDisposable
{
    private readonly WebApplication? _application;

    private ServerHost(WebApplication? application, Uri? address)
    {
        _application = application;
        Address = address ?? new Uri("http://127.0.0.1");
    }

    /// <summary>The address of the server the desktop application talks to.</summary>
    public Uri Address { get; }

    /// <summary>
    /// The name of the file in which the address of the server is written.
    /// It lets the user open the same application in a normal browser and it lets the
    /// tests of the published application find the server.
    /// </summary>
    public const string AddressFileName = "barsig-server.url";

    /// <summary>The full path of the file with the address of the server.</summary>
    public static string AddressFilePath => Path.Combine(AppContext.BaseDirectory, AddressFileName);

    /// <summary>Starts the server of BarsiG.</summary>
    /// <param name="configuration">The configuration of the desktop application.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<ServerHost> StartAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        string? remote = configuration["Desktop:ServerUrl"];

        if (!string.IsNullOrWhiteSpace(remote))
        {
            // The desktop application is a client of a server on another computer.
            return new ServerHost(null, new Uri(remote.TrimEnd('/')));
        }

        // Resolve the relative SQLite path in appsettings.json beside this executable,
        // never relative to the shell or IDE directory that happened to launch it.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        int port = FindFreePort();
        string url = $"http://127.0.0.1:{port}";

        string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? configuration["Desktop:Environment"]
            ?? Environments.Development;

        WebApplication application = Program.CreateApp(
            new[] { "--urls", url, "--environment", environment });
        // Desktop distributions must never inherit the web project's Development demo seed.
        // Phone-only LocalDemo verification remains explicitly configured by appsettings.json.
        application.Configuration["DemoData:EnableSeedAccount"] = "false";

        try
        {
            await Program.PrepareAsync(application);

            await application.StartAsync(cancellationToken);

            WriteAddress(url);

            return new ServerHost(application, new Uri(url));
        }
        catch
        {
            await application.DisposeAsync();
            throw;
        }
    }

    /// <summary>Writes the address of the server into the file next to the executable.</summary>
    private static void WriteAddress(string url)
    {
        try
        {
            File.WriteAllText(AddressFilePath, url);
        }
        catch (IOException)
        {
            // The file is only a help, it never stops the application.
        }
    }

    /// <summary>Removes the file with the address of the server.</summary>
    private static void RemoveAddress()
    {
        try
        {
            if (File.Exists(AddressFilePath))
            {
                File.Delete(AddressFilePath);
            }
        }
        catch (IOException)
        {
            // The file is removed by the operating system later.
        }
    }

    /// <summary>Stops the server of BarsiG.</summary>
    public async ValueTask DisposeAsync()
    {
        RemoveAddress();

        if (_application is null)
        {
            return;
        }

        try
        {
            await _application.StopAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"The server of BarsiG could not be stopped cleanly: {exception.Message}");
        }

        await _application.DisposeAsync();
    }

    /// <summary>Finds a free port of the local machine.</summary>
    private static int FindFreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);

        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        listener.Stop();

        return port;
    }
}
