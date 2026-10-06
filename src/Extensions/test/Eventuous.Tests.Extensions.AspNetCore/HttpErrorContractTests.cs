using System.Net.Http.Json;
using System.Text.Json;
using Eventuous.Extensions.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace Eventuous.Tests.Extensions.AspNetCore;

public class HttpErrorContractTests {
    [Test]
    [Arguments("minimal", 200)]
    [Arguments("minimal", 400)]
    [Arguments("minimal", 404)]
    [Arguments("minimal", 409)]
    [Arguments("minimal", 500)]
    [Arguments("controller", 200)]
    [Arguments("controller", 400)]
    [Arguments("controller", 404)]
    [Arguments("controller", 409)]
    [Arguments("controller", 500)]
    public async Task Command_outcomes_have_consistent_http_contracts(string mapping, int status, CancellationToken ct) {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.AddSingleton<ICommandService<HttpOutcomeState>, HttpOutcomeService>();
            services.AddControllers().AddApplicationPart(typeof(HttpOutcomeController).Assembly);
            services.AddSingleton<ConfigureWebApplication>(_ => app => {
                app.MapControllers();
                app.MapCommands<HttpOutcomeState>().MapCommand<HttpOutcomeCommand>("/minimal/outcome");
            });
        }));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync($"/{mapping}/outcome", new HttpOutcomeCommand(Guid.NewGuid().ToString(), status), ct);
        response.StatusCode.ShouldBe((HttpStatusCode)status);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var body = json.RootElement;

        if (status == 200) {
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
            body.GetProperty("state").GetProperty("value").GetString().ShouldBe("ready");
            body.GetProperty("changes").GetArrayLength().ShouldBe(0);
        }
        else {
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            body.GetProperty("status").GetInt32().ShouldBe(status);
            body.GetProperty("title").GetString().ShouldBe("Error handling command HttpOutcomeCommand");
            var type = status switch {
                400 => nameof(DomainException),
                404 => nameof(AggregateNotFoundException),
                409 => nameof(OptimisticConcurrencyException),
                _ => nameof(InvalidOperationException)
            };
            body.GetProperty("type").GetString().ShouldBe(type);
            body.GetProperty("detail").GetString()!.ShouldContain(type);
            if (status == 400) {
                body.GetProperty("errors").GetProperty("Domain").EnumerateArray().Select(x => x.GetString())
                    .ShouldBe(new[] { "Error handling command HttpOutcomeCommand" });
            }
        }
    }
}

public record HttpOutcomeCommand(string Id, int Status);
public record HttpOutcomeState : State<HttpOutcomeState> {
    public string Value { get; init; } = "ready";
}

public class HttpOutcomeService : CommandService<HttpOutcomeState> {
    public HttpOutcomeService(IEventStore store) : base(store) {
        On<HttpOutcomeCommand>().InState(ExpectedState.New).GetStream(cmd => new(cmd.Id)).Act(cmd => {
            switch (cmd.Status) {
                case 400: throw new DomainException("Amount must be positive");
                case 404: throw new AggregateNotFoundException(typeof(Booking), new(cmd.Id), null);
                case 409: throw new OptimisticConcurrencyException(new(cmd.Id), null);
                case 500: throw new InvalidOperationException("Payment service unavailable");
            }
            return Array.Empty<object>();
        });
    }
}

[ApiController]
[Route("controller/outcome")]
public class HttpOutcomeController(ICommandService<HttpOutcomeState> service) : CommandHttpApiBase<HttpOutcomeState>(service) {
    [HttpPost]
    public Task<ActionResult<Result<HttpOutcomeState>.Ok>> Execute([FromBody] HttpOutcomeCommand command, CancellationToken ct)
        => Handle(command, ct);
}
