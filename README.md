# Open VSIX Gallery

Home of the alternative Visual Studio extension gallery.

This website can be forked and deployed for a private version with the exact same capabilities.

## Operations

- Store `Extensions:Directory` on durable storage. The default directory under `wwwroot` is intended only for a single-node deployment with persistent local storage.
- Run exactly one application instance. Package mutation coordination is process-local, so scaling out can corrupt concurrent publications even when instances share the same storage.
- Use `/health/live` for liveness and `/health/ready` for readiness. Readiness verifies that extension storage is readable, writable, and above `Extensions:MinimumFreeSpaceBytes` (1 GiB by default).

## Resource usage

- Public output caching is limited to 32 MiB, with an 8 MiB maximum response body. Extension pages, badges, and extension/author feeds use scoped invalidation; publication, deletion, and restoration still invalidate aggregate listings, API responses, feeds, and the sitemap. Tracking query parameters do not create duplicate entries on endpoints whose content does not depend on queries. Homepage pagination remains query-dependent.
- Sanitized READMEs use a separate 16 MiB cache budget, accounting for UTF-16 text and entry overhead. Concurrent misses for the same source are serialized across requests. Cache budgets are not total process-memory limits: Razor, package metadata, native image buffers, and transient allocations also consume memory.
- Upload processing stages the incoming file and validates the remaining multipart body before acquiring the publication lock, so slow transfers do not block unrelated publications, deletes, or restores. Archive validation is retained, but only gallery metadata and referenced assets are expanded, rather than extension binaries. Contained manifest-relative paths (including `./` references) remain supported. Icon decoding is bounded before allocation and uses reduced-resolution decoding where supported.
- Benchmark Release builds using isolated extension storage and the same catalogue, request mix, concurrency, and uploads before and after a change. Compare CPU seconds per workload, working set/private memory, peak memory, and latency; local synthetic results do not predict Azure savings or replace production monitoring.
