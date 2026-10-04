# The API contract

The web client ships inside the server's image, so the two always deploy together.
The Android app (and later the iPhone app) doesn't: it's installed once and updated
whenever its owner gets round to it, and a self-hosted server may be older or newer
than the app talking to it. So the HTTP API is a published contract.

## Where it's written down

`docs/openapi.json` is generated on every Debug build
(`Microsoft.Extensions.ApiDescription.Server`) and committed. Review its diff with
every change to an endpoint: a field that disappears or is renamed there is a break.

Endpoints that return a plain `IResult` don't show their response in the document
unless they declare it (`.Produces<T>()`), so declare it on anything a client uses.

The field names the apps rely on are also pinned by tests
(`BookContractTests`, `ServerInfoTests`), which fail on a rename or removal.

## The rules

1. **Additive only.** New fields and new routes are fine; clients ignore fields they
   don't know. Never rename, remove or retype a field, or change what a status code
   means, on an existing route.
2. **A breaking change gets a new route** (the old one keeps its behaviour until no
   supported app uses it) **and bumps `apiVersion`** in `ServerInfoEndpoints`.
3. **Clients check `GET /api/server-info` first.** It's anonymous and returns
   `{ product, version, apiVersion }`. An app compares `apiVersion` with the range it
   supports and tells the user to update the server or the app, rather than failing
   to parse. `version` is for display and bug reports only.
4. **Every error is ProblemDetails** (`application/problem+json`, with `status` and
   a human-readable `detail`). Return `Results.Problem(...)`, never an ad-hoc body.
5. **Files are addressed by `(bookId, sequence)`**, never by file ID: files are
   replaced wholesale on rescan.
