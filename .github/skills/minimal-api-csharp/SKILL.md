---
name: minimal-api-csharp
description: Instrukce pro vytváření nových Minimal API endpointů podle přikladu CreateRoleEndpoint.cs. Tento skill popisuje pouze konstrukci samotného endpointu; neřeší registraci do DI ani spuštění aplikace.
---

Účel

- Definovat přesnou strukturu a konvence nového Minimal API endpointu, které musí dodržet agent při generování souboru endpointu.
- Skill obsahuje jediný zdrojový soubor: CreateRoleEndpoint.cs — canonical příklad, podle kterého se nové endpointy tvoří.

Rozsah

- Tento skill popisuje pouze lokální strukturu endpointu (soubor, třída, records, handler, mapování, dokumentace).
- Neřeší konfiguraci služeb, registraci do DI kontejneru, hostování ani routing v Program.cs.

Povinná struktura nového endpointu (přesně podle CreateRoleEndpoint.cs)

1) Soubor a třída
- Název souboru: <EndpointName>Endpoint.cs (např. CreateRoleEndpoint.cs)
- Uvnitř: internal static class <EndpointName>Endpoint

2) DTO
- Vnitřní sealed record Request { ... } pro vstupní payload
  - Použít data annotations (např. [Required], [StringLength(...)]) pro validaci
  - Použít nullable typy pro nepovinná pole
- Vnitřní sealed record Response(...) pro výstup

3) Handler
- Deklarace: private static async Task<IResult> HandleAsync(Request request, [FromServices] ILoggerFactory loggerFac, CancellationToken cancellationToken)
- Povinnosti handleru:
  - Vytvořit logger: var logger = loggerFac.CreateLogger("<EndpointName>Endpoint");
  - Provést hlavní logiku (business logika není součástí tohoto skillu — může být volána přes služby předané přes [FromServices])
  - Vrátit Results.* odpověď (např. Results.Created("roles/{name}", response))

4) Mapování endpointu
- Poskytnout extension metodu na IEndpointRouteBuilder:
  internal IEndpointRouteBuilder Map<EndpointName>Endpoint() {
      app.MapPost("roles", HandleAsync).MapDocumentation();
      return app;
  }
- Mapování nesmí provádět žádné globální konfigurace; pouze vracet app po namapování.

5) Dokumentace (OpenAPI)
- Poskytnout RouteHandlerBuilder extension MapDocumentation() vracející builder s:
  - WithName, WithSummary, WithDescription, WithTags, WithOpenApi()
  - Produces<T>(status) a Produces(status) pro dokumentaci možných HTTP odpovědí

Konvence jmennování

- Třídy: <Verb><Resource>Endpoint (PascalCase)
- Request/Response: vnitřní sealed recordy pojmenované Request a Response
- Handler: HandleAsync
- Map metoda: Map<EndpointName>Endpoint

Osobitosti a best practices

- Zahrnout CancellationToken do handleru a předávat ho dál do asynchronních volání.
- Neblokovat: používat pouze async/await, vyvarovat se .Result/.Wait().
- Validaci deklarovat atributy; konkrétní ověření dat provádí business vrstva nebo validační middleware.
- Logovat pomocí ILogger získaného přes [FromServices].
- Endpoint vrací typ IResult a používá Results.* factory metody.
- Dokumentovat všechny veřejné statusy (201, 400, 401, 403 apod.).

Kontrolní seznam při vytvoření nového endpointu

- [ ] Vytvořen soubor <EndpointName>Endpoint.cs
- [ ] Obsahuje internal static class <EndpointName>Endpoint
- [ ] Obsahuje Request a Response records s atributy validace
- [ ] Handler má správný podpis (Request, [FromServices] ILoggerFactory, CancellationToken)
- [ ] Map metoda na IEndpointRouteBuilder přidána a vrací app
- [ ] MapDocumentation extension přidána s OpenAPI metadata a Produces
- [ ] Neobsahuje konfiguraci DI nebo Program.cs logiku

Poznámka

- Tento skill je zaměřený čistě na generování souboru endpointu podle předepsané šablony. Po vygenerování musí vývojář zajistit registraci a volání Map<EndpointName>Endpoint() v hostitelské aplikaci.

Všechny potřebné ukázky a referenční kód jsou obsaženy v CreateRoleEndpoint.cs v této složce.

Testování (xUnit v3) — pokyny pro agenta

Tento skill nyní také popisuje doporučený přístup k unit testům handleru (HandleAsync). Testy jsou pouze průvodcem pro agenty generující endpointy; samotný test projekt není automaticky vytvářen.

Umístění testů

- Preferovat existující testovou strukturu v repozitáři (např. složku Tests nebo Tests/Modules).
- Pokud žádné testy nejsou, doporučené fallback umístění: Tests/MinimalApi.Endpoints.Tests

Požadavky na testní projekt

- Test project by měl být xUnit v3 (PackageReference na xunit 3.x a Microsoft.NET.Test.Sdk).
- Test project musí mít ProjectReference na projekt, ve kterém se endpoint nachází. Pokud endpoint bude ve vlastním projektu, v tom projektu musí být přidán InternalsVisibleTo pro název testovacího projektu.

Jak přidat InternalsVisibleTo (v projektu, kde bude endpoint)

Možnosti:
1) Vytvořit soubor AssemblyInfo.cs v hlavním projektu a přidat:

```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("MinimalApi.Endpoints.Tests")]
```

2) Nebo do .csproj hlavního projektu přidat:

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>MinimalApi.Endpoints.Tests</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

Doporučený test (xUnit v3) — AAA pattern

- Arrange: vytvořit Request DTO a potřebné fakes/Null logger
- Act: zavolat CreateRoleEndpoint.HandleAsync(request, loggerFactory, CancellationToken.None)
- Assert: vykonat IResult přes DefaultHttpContext a ověřit HTTP status (201) a případně Location header nebo tělo

Příklad testu (uložte do Tests/MinimalApi.Endpoints.Tests/CreateRoleEndpointTests.cs):

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class CreateRoleEndpointTests
{
    [Fact]
    public async Task HandleAsync_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var request = new CreateRoleEndpoint.Request { Name = "role1" };
        var loggerFactory = NullLoggerFactory.Instance;

        // Act
        var result = await CreateRoleEndpoint.HandleAsync(request, loggerFactory, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var httpContext = new DefaultHttpContext();
        await result.ExecuteAsync(httpContext);
        Assert.Equal(StatusCodes.Status201Created, httpContext.Response.StatusCode);
    }
}
```

Poznámky a best practices pro testy

- Testy by měly být rychlé a deterministické; všechny závislosti fiktivní nebo Null/Stub.
- Separujte logiku handleru od persistence tak, aby business logiku bylo možné testovat izolovaně.
- Nepíšeme integrační testy do tohoto skillu; tady jsou pouze unit testy handleru.
- Pokud handler používá další služby, předávejte je přes [FromServices] a v testu je nahradit stubem nebo fake implementací.

Kontrolní seznam pro agenta při generování endpointu + testů

- [ ] V endpointu je handler internal (pro unit testování)
- [ ] Agent navrhne název testového projektu a umístění (pokud existují testy, použít jejich strukturu)
- [ ] Agent vygeneruje ukázkový test podle AAA a připojí instrukce pro přidání InternalsVisibleTo v hlavním projektu

