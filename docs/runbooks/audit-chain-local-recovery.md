# Runbook: recovering the local audit chain under `aspire run`

The audit chain is producer (API, Gateway) -> audit-proxy -> immugw -> immudb (ADR-0096). Under
`aspire run` the proxy keeps its key ring as files (ADR-0097 file mode) beside its sealing key store, in
one named volume, so a recreated dev Vault does not touch it. This runbook covers the local states in
which audit delivery stalls and how to get out of them.

`<AppHostKey>` below is the first 8 hex characters of the SHA-256 of the app host directory
(`AuditChainExtensions.AppHostKey`). The proxy state volume `audit-proxy-state-<AppHostKey>` carries
the same key, so `docker volume ls | grep audit-proxy-state` shows it.
`LocalApplicationData` is `~/.local/share` on Linux and `%LOCALAPPDATA%` on Windows.

## Producer directories

| Directory | What it holds |
|---|---|
| `LocalApplicationData/userorg/<AppHostKey>/file-ring/<Module>-sealing-keys` | the producer key cache: the proxy's public sealing-key document |
| `LocalApplicationData/userorg/<AppHostKey>/file-ring/<Module>-spool` | sealed envelopes not yet delivered |
| `LocalApplicationData/userorg/<AppHostKey>/file-ring/dead-letter` | envelopes the drain gave up on, shared by both producers |

`<Module>` is `UserOrg.Api` or `UserOrg.Gateway`.

## Resetting the audit chain after a key change

A dev Vault restart needs no reset: the ring is in the volume, not in Vault. To force one anyway, for
example after the proxy refused to start on a store it cannot open, stop the app host, remove the volume
`audit-proxy-state-<AppHostKey>`, empty the `<Module>-sealing-keys` and `<Module>-spool` directories of
both producers under `file-ring` (not `dead-letter`), and start again. Envelopes in the spool were sealed
to the lost key and cannot be opened by anyone, so they are dropped. If you leave them, they dead-letter
on their own: the proxy answers 422 `sealing.unknown_key` for a key gone from a loaded store, the drain
moves the envelope to `dead-letter` with reason `rejected`, and the spool health check turns Unhealthy
(ADR-0116).

## Clearing the dead-letter box

The reset leaves `LocalApplicationData/userorg/<AppHostKey>/file-ring/dead-letter` alone on purpose. Any record in
it turns the spool health check, and so `/health` of both producers, Unhealthy. Each record is evidence
that an audit event never reached the archive. Read the producer logs for the Critical entry that moved
it, decide whether the loss matters, and only then delete the files by hand.

## Removing old persistent containers after an Aspire bump

immudb and immugw are Persistent containers. After an Aspire version bump the app host computes a new
container hash and starts new ones while the old ones keep running and holding their ports. List them
with `docker ps -a --filter name=immu` and remove the ones whose name suffix no longer matches the
running app host.

## Checking immugw's restart policy

immugw runs with `--restart on-failure:5`, so the container runtime restarts it at most five times after
a failed start. The policy is set when the container is created, so check it once on a recreated
container:

```sh
docker inspect --format '{{.HostConfig.RestartPolicy.Name}}:{{.HostConfig.RestartPolicy.MaximumRetryCount}}' $(docker ps -aq --filter name=immugw)
```

The expected output is `on-failure:5`. An older container created before this setting shows `no:0`;
remove it so the app host creates it again.

## immugw waits for immudb readiness

immugw starts only after immudb answers 200 on `/readyz` (port 9497), not as soon as its container is running. The persistent immudb container is recreated once after this change because its endpoints changed (see "Removing old persistent containers after an Aspire bump"); the archive data in the old container is not carried over.

## Delivery retries

DrainWorker (Lukdrasil.StepUpLogging 5.2.0) is the only retrying layer between a producer and the proxy.
It posts one envelope per request and, while the proxy keeps failing, doubles its wait from
`DrainInterval` (5 s) up to `MaxDrainBackoff` (5 min). The README documents no jitter, so treat the
backoff as unjittered.

The library strips every resilience handler the spool's HttpClient (`Lukdrasil.StepUpLogging.Audit`)
inherits, so `DeliveryTimeout` is its only timeout. ServiceDefaults sets it to 10 s (the package default
is 30 s) before binding the `EncryptedSpool` section, so a hung proxy is abandoned after 10 s, logged at
Warning, and the envelope stays spooled for the next drain.
Three failed deliveries in a row, or an oldest record older than `OldestRecordMaxAge` (15 min), report
the spool check Degraded, so the producer stays in readiness while writes still spool. A dead-lettered
envelope, for example one the proxy rejected with 422, turns the check Unhealthy (ADR-0116). The check
also exports `audit_spool_oldest_age_seconds`.

## Deleting the leftovers of earlier runs

Two earlier layouts are orphaned and nothing reads them any more; delete them by hand.

- Before the directories were keyed by the app host, they lived at
  `LocalApplicationData/userorg/<Module>-sealing-keys` and `LocalApplicationData/userorg/<Module>-spool`,
  with a `LocalApplicationData/userorg/dead-letter` beside them.
- While the proxy kept its ring in Vault, the producer directories were
  `LocalApplicationData/userorg/<AppHostKey>/<Module>-sealing-keys` and `<Module>-spool`, the dead-letter
  box was `LocalApplicationData/userorg/<AppHostKey>/dead-letter`, and the proxy volume was
  `audit-proxy-sealing-keys-<AppHostKey>`. Read the old `dead-letter` first, because its records are
  evidence of events that never reached the archive, then delete it, and remove the volume with
  `docker volume rm audit-proxy-sealing-keys-<AppHostKey>`.
