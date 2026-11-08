# JWT Authentication Diagnostics

## Pøidané diagnostické nástroje

### 1. Detailní logování v ConfigureJwtBearerOptions

Loguje všechny fáze autentizace s prefixem `[AUTH-*]`:
- `[AUTH-1]` - Pøíchozí zpráva s Authorization headerem
- `[AUTH-SUCCESS]` - Úspìšná validace tokenu vèetnì claims
- `[AUTH-FAILED]` - Selhání autentizace s detaily exception
- `[AUTH-CHALLENGE]` - Vızva k autentizaci
- `[AUTH-FORBIDDEN]` - Zakázanı pøístup

### 2. Detailní logování v JwtBearerAuthenticationHandler

Loguje s prefixem `[HANDLER-*]`:
- `[HANDLER-1]` a `[HANDLER-7]` - Kroky autentizaèního procesu
- `[HANDLER-SUCCESS]` - Úspìšná autentizace
- `[HANDLER-FAILED]` - Selhání autentizace
- `[HANDLER-ERROR]` - Chyba pøi naèítání OIDC konfigurace
- `[HANDLER-EXCEPTION]` - Neoèekávaná vıjimka

**Dùleité**: Handler se pokusí naèíst OIDC konfiguraci a vypíše:
- Authority URL
- JwksUri endpoint
- Poèet dostupnıch signing keys
- KeyId jednotlivıch klíèù

### 3. Diagnostickı endpoint

**URL**: `GET http://localhost:{port}/_diagnostics/oidc`

Dostupnı pouze v Development reimu.

Zobrazí:
- Aktuální konfiguraci JwtBearerOptions
- OIDC discovery metadata
- Poèet a detaily signing keys
- Token validation parameters
- Podporované response types a scopes
- Detailní stack trace v pøípadì chyby

### Jak diagnostikovat problém IDX10500

1. **Spuste aplikaci v Development reimu**

2. **Zavolejte diagnostickı endpoint**:
   ```bash
   curl http://localhost:5000/_diagnostics/oidc
   ```
   nebo otevøete v prohlíeèi.

3. **Zkontrolujte logy**:
   ```
   dotnet run --project src\Modules\Services\Gateway\UserOrg.Gateway\UserOrg.Gateway.csproj
   ```

4. **Hledejte v logu**:
   - `[HANDLER-4]` - Mìl by zobrazit, e OIDC configuration byla naètena
   - `[HANDLER-5]` - ERROR, pokud nejsou signing keys
   - `[HANDLER-ERROR]` - Chyba pøi naèítání OIDC konfigurace (napø. SSL problém)

### Typické pøíèiny IDX10500:

#### A. Certifikát není dùvìryhodnı
**Symptom**: 
```
[HANDLER-ERROR] Failed to retrieve OIDC configuration
System.Net.Http.HttpRequestException: The SSL connection could not be established
```

**Øešení**: Ji implementováno v `ConfigureJwtBearerOptions`:
```csharp
if (_environment.IsDevelopment())
{
    options.BackchannelHttpHandler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = 
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };
}
```

#### B. JWKS endpoint je prázdnı nebo nedostupnı
**Symptom**:
```
[HANDLER-5] No signing keys found in OIDC configuration!
```

**Kontrola**:
1. Zavolejte `/_diagnostics/oidc`
2. Ovìøte, e `JwksUri` je správná
3. Manuálnì zkontrolujte JWKS endpoint:
   ```bash
   curl https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV/protocol/openid-connect/certs
   ```

#### C. Authority URL je chybná
**Symptom**:
```
[HANDLER-ERROR] Failed to retrieve OIDC configuration
System.Net.Http.HttpRequestException: Response status code does not indicate success: 404
```

**Kontrola**:
1. Ovìøte `.well-known/openid-configuration` endpoint:
   ```bash
   curl https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV/.well-known/openid-configuration
   ```

#### D. Síovı problém (proxy, firewall)
**Symptom**:
```
[HANDLER-ERROR] Failed to retrieve OIDC configuration
System.Net.Http.HttpRequestException: The request timed out
```

**Øešení**: Zkontrolujte network connectivity z aplikace k authority serveru.

## Úroveò logování

### Produkce (appsettings.json)
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore.Authentication": "Warning",
      "UserOrg.Gateway": "Information"
    }
  }
}
```
- Pouze **Error** logy pøi selhání autentizace
- ádné Debug zprávy

### Vıvoj (appsettings.Development.json)
```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.AspNetCore.Authentication.JwtBearer": "Trace",
      "Microsoft.IdentityModel": "Debug",
      "System.Net.Http.HttpClient": "Debug",
      "UserOrg.Gateway": "Debug"
    }
  }
}
```
- **Debug** logy pro kadı autentizaèní request
- Detaily OIDC discovery a JWKS naèítání

## Diagnostické zprávy

Všechny zprávy mají prefix `[AUTH]` pro snadné vyhledávání:

| Zpráva | Level | Kdy se zobrazí |
|--------|-------|----------------|
| `[AUTH] Message received for {Path}` | Debug | Kadı request s Authorization headerem |
| `[AUTH] Token validated for user {UserId}` | Debug | Úspìšná validace tokenu |
| `[AUTH] Authentication failed for {Path}` | **Error** | Selhání validace tokenu |
| `[AUTH] Challenge triggered for {Path}` | Debug | 401 Unauthorized response |

### Pøíklad úspìšného logu:

```
[HANDLER-1] Starting authentication for /api/test
[HANDLER-2] Authority: https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV, RequireHttpsMetadata: False
[HANDLER-3] Attempting to retrieve OIDC configuration...
[HANDLER-4] OIDC Configuration retrieved successfully. Issuer: https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV, JwksUri: https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV/protocol/openid-connect/certs, SigningKeys count: 2
[HANDLER-6] Signing key available - KeyId: xyz123, Type: RsaSecurityKey
[HANDLER-SUCCESS] Token validated successfully for user abc-def-123
[AUTH-SUCCESS] Token validated for user abc-def-123. Claims: 5
```

### Pøíklad neúspìšného logu (IDX10500):

```
[HANDLER-1] Starting authentication for /api/test
[HANDLER-2] Authority: https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV, RequireHttpsMetadata: False
[HANDLER-3] Attempting to retrieve OIDC configuration...
[HANDLER-ERROR] Failed to retrieve OIDC configuration. This will cause signature validation to fail. Authority: https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV
System.Net.Http.HttpRequestException: The SSL connection could not be established, see inner exception.
[HANDLER-FAILED] Authentication failed. Error: IDX10500: Signature validation failed. No security keys were provided to validate the signature.
[AUTH-FAILED] Authentication failed for path /api/test. Exception type: SecurityTokenSignatureKeyNotFoundException, Message: IDX10500: Signature validation failed...
```

## Další kroky

Po zjištìní pøíèiny problému mùete:
1. Upravit `BackchannelHttpHandler` pro specifické certifikáty
2. Nastavit `BackchannelTimeout` pro pomalé sítì
3. Implementovat fallback mechanismus pro offline JWKS
4. Pøidat caching pro OIDC metadata

## Monitoring v produkci

Pro monitoring autentizace v produkci hledejte Error logy:

```
[AUTH] Authentication failed for /api/endpoint
```

Pro detailní diagnostiku doèasnì zmìòte log level v `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "UserOrg.Gateway": "Debug"
    }
  }
}
```

**Dùleité**: Nezapomeòte vrátit zpìt na `Information` po dokonèení diagnostiky!
