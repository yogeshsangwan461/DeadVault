using DeadVault.McpServer;
using DeadVault.Core.Interfaces;
using DeadVault.Core.Services;
using DeadVault.Store.Interfaces;
using DeadVault.Store.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// MCP stdio uses stdout for JSON protocol messages.
// Any log output to stdout breaks JSON parsing in the client.
// Redirect all logging to stderr so the protocol channel stays clean.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<IMetadataStore, JsonMetadataStore>();
builder.Services.AddSingleton<IRepoManager, RepoManager>();
builder.Services.AddSingleton<ISnapshotManager, SnapshotManager>();
builder.Services.AddSingleton<IDiffManager, DiffManager>();
builder.Services.AddSingleton<ILockManager, LockManager>();
builder.Services.AddSingleton<IRestoreManager, RestoreManager>();
builder.Services.AddSingleton<DeadVaultMcpTools>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DeadVaultMcpTools>();

await builder.Build().RunAsync();
