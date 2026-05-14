using System.Diagnostics;

using WorkIQChat.AppHost;
using WorkIQChat.ServiceDefaults;

var osArch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;

var builder = DistributedApplication.CreateBuilder(args);

var dbPassword = builder.AddParameter("sql-password", "P@ssw0rd!")
    .InitiallyHidden();

var sqlServer = builder.AddSqlServer("sqlserver", dbPassword)
    .WithContainerName("workiqchat-sqlserver");

if (osArch == System.Runtime.InteropServices.Architecture.Arm64
    && System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
{
    sqlServer.WithImage("azure-sql-edge");
}

var db = sqlServer.WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase(Constants.DatabaseConnectionString);

var migrations = builder.AddExecutable("db-migrations", "dotnet", "../../src/WorkIQChat.Server",
    [
        "ef",
        "database",
        "update",
        "--no-build",
        "--project",
        "../WorkIQChat.Data",
        "--connection",
        db.Resource.ConnectionStringExpression
    ])
    .WithCommand("dotnet-tools", "Restore Tools", async (ExecuteCommandContext x) =>
    {
        var process = Process.Start(new ProcessStartInfo()
        {
            FileName = "dotnet",
            ArgumentList = { "tool", "restore" },
        });
        if (process is null) return CommandResults.Failure();
        await process.WaitForExitAsync(x.CancellationToken);
        return CommandResults.Success();
    }, new CommandOptions())
    //.HideWhen(KnownResourceStates.Finished)
    .WaitFor(db);

builder.AddProject<Projects.WorkIQChat_Server>("server", "https")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck(Constants.HealthEndpointPath)
    .WithReference(db)
    .WaitForCompletion(migrations);

builder.Build().Run();