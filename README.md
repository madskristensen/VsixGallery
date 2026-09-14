# Open VSIX Gallery

Home of the alternative Visual Studio extension gallery.

This website can be forked and deployed for a private version with the exact same capabilities.

## Operations

- Store `Extensions:Directory` on durable storage. The default directory under `wwwroot` is intended only for a single-node deployment with persistent local storage.
- Run exactly one application instance. Package mutation coordination is process-local, so scaling out can corrupt concurrent publications even when instances share the same storage.
- Use `/health/live` for liveness and `/health/ready` for readiness. Readiness verifies that extension storage is readable, writable, and above `Extensions:MinimumFreeSpaceBytes` (1 GiB by default).