using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SandwichOrders.IntegrationTests;

// A fresh database: the app must start without a single error in the log (the database is created before
// anything else reads from it, e.g. the Data Protection keys).
public class StartupTests : IDisposable
{
    private readonly TestWebApplicationFactory factory = new();

    [Fact]
    public async Task AFreshStart_LogsNoErrors()
    {
        var errors = new ConcurrentQueue<string>();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<ILoggerProvider>(new ErrorCollector(errors))));
        using var client = app.CreateClient();

        var response = await client.GetAsync("/"); // a page with forms: anti-forgery tokens need the keys

        Assert.True(response.IsSuccessStatusCode);
        Assert.Empty(errors);
    }

    public void Dispose()
    {
        factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class ErrorCollector(ConcurrentQueue<string> errors) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, errors);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<string> errors) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Error)
                {
                    errors.Enqueue($"{category}: {formatter(state, exception)} {exception?.Message}");
                }
            }
        }
    }
}
