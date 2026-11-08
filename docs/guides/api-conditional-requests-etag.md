# Working with ETags (conditional GET) — client integration guide

This guide is for teams building client applications against the v2 API. It explains how to use
`ETag` / `If-None-Match` conditional requests to avoid re-downloading unchanged responses.

**TL;DR:** Conditional requests are **opt-in and fully backward compatible**. If your client does nothing,
every call returns the same full `200` response as before. If your client stores the `ETag` and sends it back
as `If-None-Match`, the server answers an unchanged resource with `304 Not Modified` and an **empty body** —
saving bandwidth and parsing.

---

## What the server gives you

On the supported read endpoints, every `200 OK` response carries two extra headers:

| Header | Example | Meaning |
|---|---|---|
| `ETag` | `"8f871616685f57bf96cfb9e8b642e751ac2eb16751eda687b9bbe64835ad0f51"` | Strong validator — a SHA-256 hash of the exact response body, wrapped in double quotes. |
| `Cache-Control` | `private, no-cache` | The data is per-user and behind a bearer token (`private`); `no-cache` means *you may store it but must revalidate before reusing it*. |

The `ETag` changes whenever the response body changes — including when a permission or data change alters what
you can see. There is **no TTL to wait out**: a change is reflected on the very next request.

### Endpoints that support ETag

Conditional GET is enabled on these cached read endpoints:

- `GET /api/v2/UserInfo`
- `GET /api/v2/UserInfo/Extended`
- `GET /api/v2/Countries`
- `GET /api/v2/Statistics`
- `GET /api/v2/Projects/{projectShortName}`
- `GET /api/v2/Projects/{projectShortName}/Templates`

> List endpoints (e.g. `GET /api/v2/Projects`) currently do **not** emit an `ETag`. They instead expose
> pagination via the `X-Total-Count` and `Link` response headers. Don't rely on `ETag` being present on a
> response that doesn't advertise one — always read it from the response rather than assuming.

---

## The flow

```
1. First request  ─────────────────────────────►  GET /api/v2/Countries
                  ◄─────────────────────────────  200 OK
                                                   ETag: "8f87…0f51"
                                                   <full body>

   → Store the body AND the ETag (keyed by URL + the user/token).

2. Next request   ─────────────────────────────►  GET /api/v2/Countries
                                                   If-None-Match: "8f87…0f51"

   2a. Unchanged   ◄────────────────────────────  304 Not Modified   (empty body)
                                                   ETag: "8f87…0f51"
       → Reuse the body you stored in step 1.

   2b. Changed     ◄────────────────────────────  200 OK
                                                   ETag: "<new value>"
                                                   <new full body>
       → Replace your stored body + ETag.
```

The key rule: **store the ETag next to the cached body, and send it back verbatim** (including the double
quotes) on the next request to the same URL.

---

## Examples

### curl

```bash
# 1) First call — capture the ETag
curl -i -H "Authorization: Bearer $TOKEN" https://<host>/api/v2/Countries
#    ... ETag: "8f871616685f57bf96cfb9e8b642e751ac2eb16751eda687b9bbe64835ad0f51"

# 2) Conditional call — send it back verbatim (mind the quotes)
curl -i -H "Authorization: Bearer $TOKEN" \
     -H 'If-None-Match: "8f871616685f57bf96cfb9e8b642e751ac2eb16751eda687b9bbe64835ad0f51"' \
     https://<host>/api/v2/Countries
#    -> HTTP/1.1 304 Not Modified   (no body)
```

### JavaScript (fetch)

```js
const cache = new Map(); // key -> { etag, body }

async function getJson(url, token) {
  const prev = cache.get(url);
  const res = await fetch(url, {
    headers: {
      Authorization: `Bearer ${token}`,
      ...(prev ? { 'If-None-Match': prev.etag } : {}),
    },
  });

  if (res.status === 304) {
    return prev.body;            // unchanged — reuse what we already have
  }

  const body = await res.json(); // 200 — fresh data
  const etag = res.headers.get('ETag');
  if (etag) cache.set(url, { etag, body });
  return body;
}
```

`ETag` is readable from cross-origin browser requests because the API publishes it in
`Access-Control-Expose-Headers` (alongside `Link` and `X-Total-Count`).

### C# (HttpClient)

```csharp
// Store (etag, payload) per URL for the current user.
request = new HttpRequestMessage(HttpMethod.Get, url);
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
if (cachedEtag is not null)
    request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(cachedEtag));

var response = await httpClient.SendAsync(request, ct);

if (response.StatusCode == HttpStatusCode.NotModified)   // 304
    return cachedPayload;                                 // reuse

var payload = await response.Content.ReadAsStringAsync(ct);
cachedEtag = response.Headers.ETag?.ToString();           // includes the quotes
cachedPayload = payload;
return payload;
```

### Python (requests)

```python
import requests

# Per-user cache: { url: {"etag": ..., "body": ...} }
_cache: dict[str, dict] = {}

def get_json(url: str, token: str):
    prev = _cache.get(url)
    headers = {"Authorization": f"Bearer {token}"}
    if prev:
        headers["If-None-Match"] = prev["etag"]   # sent verbatim, quotes included

    res = requests.get(url, headers=headers)

    if res.status_code == 304:        # Not Modified — reuse what we already have
        return prev["body"]

    res.raise_for_status()
    body = res.json()                 # 200 — fresh data
    etag = res.headers.get("ETag")
    if etag:
        _cache[url] = {"etag": etag, "body": body}
    return body
```

> `requests` does **not** do conditional revalidation for you — you must store the `ETag` and send
> `If-None-Match` yourself, exactly as above. (If you use a `requests-cache` session it can handle this
> transparently, much like a browser.)

### React (hook)

Browsers often revalidate transparently, so a plain `fetch` may already benefit without any code. The hook
below is for when you keep your **own** in-memory cache and want to handle `304` explicitly (e.g. to skip a
re-render or a re-parse).

```jsx
import { useEffect, useRef, useState } from 'react';

// Module-level cache shared across components: url -> { etag, body }
const etagCache = new Map();

export function useConditionalResource(url, token) {
  const [data, setData] = useState(() => etagCache.get(url)?.body ?? null);
  const abortRef = useRef(null);

  useEffect(() => {
    if (!url || !token) return;
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    (async () => {
      const prev = etagCache.get(url);
      const res = await fetch(url, {
        signal: controller.signal,
        headers: {
          Authorization: `Bearer ${token}`,
          ...(prev ? { 'If-None-Match': prev.etag } : {}),
        },
      });

      if (res.status === 304) {        // unchanged — reuse cached body
        setData(prev.body);
        return;
      }

      const body = await res.json();   // 200 — fresh data
      const etag = res.headers.get('ETag');
      if (etag) etagCache.set(url, { etag, body });
      setData(body);
    })().catch((e) => {
      if (e.name !== 'AbortError') throw e;
    });

    return () => controller.abort();
  }, [url, token]);

  return data;
}
```

`ETag` is readable here because the API lists it in `Access-Control-Expose-Headers` (along with `Link` and
`X-Total-Count`). Note the browser's HTTP cache may transparently turn a `304` into a `200` from its own
cache before your code sees it — that's fine; the manual flow is only needed when you maintain the cache
yourself.

---

## Rules and gotchas

- **Send the ETag exactly as received**, including the surrounding double quotes. `If-None-Match: "abc…"` is
  correct; `If-None-Match: abc…` is not.
- **A `304` has an empty body.** Always keep the last good body to return when you get a `304`.
- **Cache per user.** Responses are `private` (per token/identity). Never share a cached body or its ETag
  across different users — key your cache by `(URL, user)`.
- **One ETag per URL+query.** A different query string (filters, paging) is a different resource with its own
  ETag. Don't reuse an ETag across different query strings.
- **It is not a write precondition.** This ETag answers `If-None-Match` for reads (bandwidth/`304`) only. It is
  **not** currently honored as an `If-Match` precondition on writes (`PUT`/`PATCH`), so do not use it for
  optimistic-concurrency conflict detection — that is a separate, future capability.
- **Weak validators are tolerated inbound.** If your HTTP stack echoes the value back as `W/"…"`, the server
  still matches it; but the server always emits a *strong* ETag.
- **Browsers may handle this for you.** A browser's built-in HTTP cache can perform the conditional revalidation
  transparently and hand your `fetch()` a `200` served from its cache — so you may never observe a literal
  `304`. That's expected; only implement the manual flow above if you maintain your own response cache.

---

## Doing nothing is safe

If a client never sends `If-None-Match`, it always receives the full `200` response with the complete body,
identical to the pre-ETag behavior. The only visible difference is the two extra response headers (`ETag`,
`Cache-Control`), which such a client can ignore. Adopting conditional requests is purely an optimization you
opt into when you're ready.
