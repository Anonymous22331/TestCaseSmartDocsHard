namespace TestCaseSmartDocsHard;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();
        app.UseSwagger(options => options.RouteTemplate = "api/swagger/{documentName}/swagger.json");
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "api/swagger";
            options.SwaggerEndpoint("/api/swagger/v1/swagger.json", "TestCaseSmartDocsHard API");
        });
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.MapControllers();
        app.Run();
    }
}
