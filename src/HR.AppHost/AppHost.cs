var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.HR_Api>("api");

builder.AddProject<Projects.HR_Web>("web")
	.WithReference(api)
	.WaitFor(api);

builder.Build().Run();
