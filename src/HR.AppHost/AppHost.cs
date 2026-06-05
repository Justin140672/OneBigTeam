var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var hrDatabase = postgres.AddDatabase("hr");

var api = builder.AddProject<Projects.HR_Api>("api")
    .WithReference(hrDatabase)
    .WaitFor(hrDatabase);

builder.AddProject<Projects.HR_Web>("web")
	.WithReference(api)
	.WaitFor(api);

builder.Build().Run();
