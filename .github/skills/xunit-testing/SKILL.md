---
name: xunit-testing
description: Guidance for writing xUnit v3 tests in .NET projects. Includes Arrange–Act–Assert structure, naming conventions, parameterized tests, isolation, CI tips and examples (Czech/English).
context: developer
compatibility: "Requires only .NET SDK and test runner"
---

# xUnit Testing Skill — AAA & Best Practices (Czech / English)

Popis (CZ):
Tento SKILL poskytuje jasná pravidla a šablonu pro vytváření xUnit v3 testů v .NET projektech. Obsahuje doporučení pro strukturu testu, pojmenování, izolaci, parametrizaci a běh testů podle oficiální dokumentace Microsoft a xUnit v3.

Summary (EN):
A compact, practical guide for writing maintainable xUnit v3 tests: Arrange–Act–Assert structure, naming convention, parameterized tests, test isolation, running tests and CI tips — aligned with Microsoft and xUnit v3 guidance.

**Core Rules / Hlavní pravidla**
- **AAA:** Rozdělte každý test na Arrange (příprava), Act (provedení jedné akce) a Assert (ověření výsledku).
- **One Act per test:** Jeden Act na test — pokud potřebujete více vstupů, použijte `[Theory]` s `[InlineData]` nebo datové zdroje.
- **Naming / Pojmenování:** `MethodName_Scenario_ExpectedResult`. Příklad: `Add_NegativeNumber_ThrowsArgumentException`.
- **Isolate tests:** Mockujte závislosti na I/O (DB, HTTP, filesystem). Unit testy by měly být rychlé a deterministické.
- **Minimal inputs:** Používejte co nejmenší sadu dat, která správně ověří chování.
- **Avoid shared state:** Nepoužívejte globální setup, preferujte pomocné továrny (factories) a buildery pro testovací data.
- **Prefer Theories:** Pro parametrizované scénáře používejte `[Theory]` + `[InlineData]` nebo vlastní data attributes.
- **Assert clearly:** Preferujte konkrétní, čitelné assertiony (např. `Assert.ThrowsAsync<...>`, `FluentAssertions` apod.).

**Projektová struktura & organizace**
- Oddělte unit testy od integračních testů (např. `tests/Unit` vs `tests/Integration`).
- Unit test projekty: malé závislosti, používají fakes/mocks.
- Integrační testy: spouštějí reálné DB nebo služby, označujte je zvlášť (např. kategorií/attribute).

**Jak nastavit xUnit v3 projekt (rychle)**
- Nainstalovat šablonu (volitelné):

```powershell
dotnet new install xunit.v3.templates
dotnet new xunit3 -n MyProject.Tests
```

- Přidat nezbytné balíčky v `csproj` (Microsoft.NET.Test.Sdk užitečné pro `dotnet test` a CI):

```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="" />
<PackageReference Include="xunit" Version="" />
<PackageReference Include="xunit.runner.visualstudio" Version="" />
```

- Spuštění testů lokálně a v CI:

```powershell
dotnet test
```

V CI: používejte `dotnet test --no-build` pokud build proběhl zvlášť.

**Praktické příklady**
- Jednoduchý unit test (AAA):

```csharp
[Fact]
public void CalculateTotal_EmptyCart_ReturnsZero()
{
    // Arrange
    var cart = CartFactory.Empty();

    // Act
    var total = cart.CalculateTotal();

    // Assert
    Assert.Equal(0m, total);
}
```

- Parametrizovaný test s Theory:

```csharp
[Theory]
[InlineData(1, 2, 3)]
[InlineData(2, 2, 4)]
public void Add_TwoNumbers_ReturnsSum(int a, int b, int expected)
{
    // Act
    var result = Calculator.Add(a, b);

    // Assert
    Assert.Equal(expected, result);
}
```

**Tipy pro stabilitu a čitelnost**
- Používejte helper factories pro vytváření modelů testovacích dat.
- Mockování: preferujte `Moq`, `NSubstitute` nebo jiné knihovny pro nahrazení I/O závislostí.
- Asynchronní testy: označte `async Task` a používejte `await`; nepoužívejte `.Result`/`.Wait()`.
- Testy by měly být rychlé — cíleně pod 100ms pokud je to možné.
- Nepište testy, které závisí na pořadí spouštění.

**CI & běh v pipelines**
- Spouštějte `dotnet test --no-build` v CI po fázi build.
- Pro integrační testy použijte samostatné joby s testovací infrastrukturou (DB container, test fixtures).

**Reference (oficiální)**
- Microsoft: Unit testing best practices — https://learn.microsoft.com/dotnet/core/testing/unit-testing-best-practices
- Microsoft: Unit testing C# with xUnit — https://learn.microsoft.com/dotnet/core/testing/unit-testing-csharp-with-xunit
- Microsoft: Testing minimal APIs (xUnit) — https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/test-min-api
- xUnit v3: Getting Started — https://xunit.net/docs/getting-started/v3/getting-started

**Checklist pro psaní nového testu**
1. `Name`: `Method_Scenario_Expected`.
2. `Arrange`: připravit pouze nezbytné vstupy.
3. `Act`: jediná akce.
4. `Assert`: jedno chování nebo úzce související tvrzení.
5. Pokud víc vstupů → změňte na `Theory`.
6. Zajistit izolaci a rychlost.

---
**Instrukce pro AI / Agent Guidelines**

- **Source of truth:** Kód v `src/` je autoritativní zdroj (source of truth). Testy v `tests/` ověřují chování tohoto kódu.
- **Test-only code:** Pokud test potřebuje doplňkový pomocný kód (fakes, test helpers, test data builders), vytvořte ho v rámci testovacího projektu (`tests/...`), nikoli v `src/`.
- **Nedělat tiché opravy:** Pokud spouštěním testu objevíte chybu v `src/`, **neprovádějte automatickou opravu**. Nejprve požádejte o potvrzení opravy od autora/maintainerů.
- **Dotaz o opravě musí obsahovat:** krátký popis problému (očekávané vs. pozorované chování), jméno selhávajícího testu, relevantní chybové zprávy nebo stack trace a navrhované řešení (1-2 věty).
- **Po potvrzení opravy:** proveďte opravu, aktualizujte nebo přidejte test, aby nejprve selhával a poté procházel, a vytvořte smysluplný commit/PR s popisem změny.

Poznámka pro agenty: Pokud PR upravuje behavior produkčního kódu, přidejte/neonačtěte testy, které explicitně ukazují očekávané chování.
