# VSIX Gallery Audit Implementation Plan

This plan tracks implementation of the September 2026 website audit. Each work item maps to one focused Git commit. The checklist will be updated as work progresses.

## Scope

- [x] 1. Establish automated test coverage for critical existing behavior.
- [x] 2. Harden upload processing: filesystem containment, strict extension IDs, bounded archive extraction, cancellation, safe error responses, URL validation, XML safety, and SVG escaping.
- [x] 3. Improve package-storage reliability with atomic replacement and safer lifecycle cleanup.
- [x] 4. Correct HTTP and application behavior: conditional requests, feed timestamps, pagination, unlisted API output, author feeds, optional links, sitemap generation, canonical URLs, and error responses.
- [x] 5. Secure and improve README rendering with server-side retrieval, sanitization, and caching.
- [x] 6. Improve accessibility, SEO, performance, content quality, and broken-link handling.
- [x] 7. Add operational safeguards: health checks, storage diagnostics, and useful telemetry/logging.
- [ ] 8. Harden dependencies and CI: reproducible restore, PR validation, dependency automation, deployment serialization, smoke checks, and safer action references.
- [ ] 9. Run final build/tests and verify the commit history and clean working tree.

## Explicit exclusions

- Publisher ownership or identity.
- Changes to upload authentication.
- Changes to admin authentication.
- Changes to per-extension manage-token authentication.

Security fixes that do not alter those authentication or ownership models remain in scope.

## Progress

| Work item | Status | Commit |
| --- | --- | --- |
| Implementation plan | Complete | `docs: add audit implementation plan` |
| Test foundation | Complete | `test: establish automated test foundation` |
| Upload safety | Complete | `security: harden VSIX upload processing` |
| Storage reliability | Complete | `reliability: make package publication recoverable` |
| HTTP correctness | Complete | `fix: correct HTTP and gallery behavior` |
| README safety | Complete | `security: sanitize README rendering` |
| Web quality | Complete | `web: improve quality and content` |
| Operations | Complete | `ops: add storage health and diagnostics` |
| Dependencies and CI | Pending | Pending |
| Final validation | Pending | Pending |

## Validation

Each work item will receive the smallest relevant build/test pass before its commit. The final work item will run the complete solution build and test suite.

- README safety: main project and test project builds succeeded; all 39 tests passed.
- Web quality: main project build and all 39 tests passed; local HTTP smoke tests verified fingerprinted CSS, strict CSP, search/404 robots directives, and current guide content.
- Operations: main and test project builds succeeded; all 42 tests passed; HTTP smoke tests verified direct 200 liveness/readiness JSON with no-store caching and storage capacity data.
