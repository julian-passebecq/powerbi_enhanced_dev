# V9.6 Agent workspace and provider boundary

PbiBench's Agent page is an original interface over the same typed command services used by the CLI. It starts offline. Merely opening the page, selecting a model, capturing context, or loading a proposal makes no provider request.

## Local workflow

1. Select the model objects and the context sections needed for the task.
2. Capture context and inspect the exact JSON, including omitted-item and shortened-text markers.
3. Use the offline selected-measure folder template, load/paste a `.pbiagent` proposal, or explicitly request an optional online proposal.
4. Validate the typed proposal. Action recipes require 1–100 explicitly named table, column or measure operations with literal values. Model applicability is checked during preview.
5. Preview an action, review its exact before/after rows and diagnostics, then apply the reviewed command hash. The shared native TE2 transaction supplies one Undo batch. The Agent page does not save files or deploy the model.
6. Query and scalar-test proposals open as drafts in DAX and QA. Review their query and expected result, then explicitly run them there. No query, test, tool, script or refresh runs automatically from model-generated text.

The offline provider produces a deterministic context-review summary, not an LLM answer. It is usable without credentials. Templates and imported proposals use the full common preview pipeline.

## Shared implementation

- `Core.Agent.AgentProposalJson` defines a strict, versioned proposal envelope and exports its JSON Schema. The recipe payload is the existing `Core.Automation.ActionRecipe`; it is not a second action language. Unknown/duplicate properties, missing nullable fields, numeric/unknown enums, mismatched payloads, implicit/all-object scopes, unsupported properties and oversized values are rejected.
- `Automation.Agent.AgentProposalService` binds capture and response handling to the current handler and whole-model fingerprint, then calls `Automation.Commands.SemanticCommandService` for preparation and application.
- The common command service captures live metadata on its owning thread, computes a detached-model diff on a worker, and materializes the guarded native preview on the owning thread. The Agent cannot construct an executable `PreparedCommand` from a JSON response.
- Pending responses and previews are invalidated by edits to the proposal, prompt, provider configuration, sharing options, model or selection. Cancellation and context checks discard late results even when a replaceable provider ignores cancellation.
- The model-facing tool catalog comes from the common command layer. Provider requests do not enable tools or dispatch generated tool calls. Approval is a host action and is not an accepted proposal field.

## Context sharing

All context checkboxes initially remain unchecked. Supported sections are selected objects, a bounded model inventory, current DAX, BPA finding summaries, semantic workspace differences, test evidence, and connection capabilities.

Context capture projects only declared fields. It never serializes the database, source partitions, connection strings, credentials or local file paths. Workspace differences include only approved semantic presentation/DAX properties. Capability entries are enum names; endpoints and transport details are excluded. Names, DAX, finding text and test evidence can still be business-sensitive; the displayed payload is the exact text selected for transmission.

The total context limit is 128 KiB. Selected objects are limited to 200, inventory entries to 1,000, and finding/diff/test rows to 100 per section. DAX is limited to 32,000 characters; other explanatory fields are shortened with explicit markers. Excess total size requires a narrower capture. The local fingerprint is not included in the provider payload.

## Optional OpenAI configuration

Choose OpenAI Responses, enter an API-project model ID supporting Structured Outputs, and enter a project API key. Keys remain in window memory and can be cleared with **Forget key**; they are not persisted in layouts, proposal files, context payloads, diagnostics or logs. No default model or account access is assumed.

Review the context and prompt, check the explicit sharing acknowledgment, then generate one proposal. The acknowledgment clears after a request or relevant draft/configuration change. Calls use `https://api.openai.com/v1/responses`, TLS and a client with automatic redirects disabled. There is no configurable arbitrary endpoint and no automatic POST retry after an uncertain result.

The request uses `text.format.type=json_schema`, `strict=true`, the published PbiBench envelope, `store=false`, `background=false`, and a bounded output budget. It contains no tools, conversation ID, previous response ID, file upload or server-side state chain. Only a completed response with one proposal text payload is accepted. Refusals, tool calls, incomplete/failed responses, oversized bodies and invalid proposals are rejected. Requests have a 120-second deadline and cancellation; input and response limits are 256 KiB and 512 KiB.

`store=false` controls API application-state storage; it is not a claim of zero provider retention. API data handling also depends on the organization's OpenAI settings and applicable abuse-monitoring controls. See the official data-control documentation below.

## Sources and evidence

Public references consulted on 2026-09-05:

- [OpenAI Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs): strict JSON Schema, required fields, `additionalProperties:false`, refusal handling.
- [Responses create reference](https://developers.openai.com/api/reference/cli/resources/responses/methods/create): create parameters, output content, status and state controls.
- [OpenAI API data controls](https://developers.openai.com/api/docs/guides/your-data): application-state and abuse-monitoring distinctions.

Implementation is original PbiBench C# using existing .NET HTTP/JSON dependencies and public interfaces. No proprietary TE3 implementation or assets are involved.

Tests use synthetic HTTP responses, injected providers and explicit local TOM fixtures. They exercise strict parsing, sharing exclusion, request shape, refusal/tool-call/error rejection, cancellation/limits, stale context/session handling, shared preview/approval/undo and draft-only staging. No real OpenAI API request, account verification, provider charge or user-model transmission is claimed by these fixtures. Live provider availability remains dependent on user configuration and API access.
