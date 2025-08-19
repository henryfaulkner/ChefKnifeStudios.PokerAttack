var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.ChefKnifeStudios_PokerAttack_Server_WebAPI>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.ChefKnifeStudios_PokerAttack_Client_WebApp>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
