using Overlock.Server.Hubs;
using Overlock.Server.Rooms;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<RoomRegistry>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<PadHub>("/hubs/pad");
app.MapGet("/health", () => Results.Ok(new { status = "ok", name = "overlock" }));

app.Run();
