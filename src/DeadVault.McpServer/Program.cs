using DeadVault.McpServer;
using DeadVault.Core.Interfaces;
using DeadVault.Core.Services;
using DeadVault.Store.Interfaces;
using DeadVault.Store.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

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
