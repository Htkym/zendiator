# Configuration diagnostics

This index covers Zendiator 0.6.0 diagnostics. Read the full diagnostic message
and its source location: they name the affected type, registration or expression.
Generator diagnostics have severity Error; the lifetime analyzer reports Warnings.
Suppressing an error does not enable a rejected route. Fix C# compiler errors
reported for the same declaration before diagnosing its generated route.

## Discovery and configuration

These diagnostics concern assembly discovery, generic binding and the compatibility
configuration API. New explicit compositions can instead report the dedicated
contract diagnostics below.

| ID | Target and reason | Where to fix |
|---|---|---|
| <a id="zen0001"></a>ZEN0001 | A discovered request has no matching handler. | Include the handler's assembly and implement the request's response/void and sync/async contract. For explicit compositions, also select the marked handler with `Handlers`. |
| <a id="zen0002"></a>ZEN0002 | A single-handler request has multiple matching handlers. | Remove the overlapping registration or choose an explicit multiple-handler dispatch contract when fan-out is intended. |
| <a id="zen0003"></a>ZEN0003 | A request, handler or subscriber has an unsupported contract or accessibility. | Correct the declaration named in the message, including its response type and generic shape; reference matching Zendiator packages. |
| <a id="zen0004"></a>ZEN0004 | A compatibility pipeline behavior has an invalid contract, duplicate type/order or no matching request. | Correct the behavior declaration and its `PipelineBehavior` or `AddOpenBehavior` registration; preserve the intended order. |
| <a id="zen0005"></a>ZEN0005 | A compatibility mediator declaration or assembly marker is invalid. | Follow the named declaration's requirements. A class-level mediator must be an empty, top-level `public sealed partial class Zendiator`; `Namespace` belongs on assembly-level configuration. |
| <a id="zen0006"></a>ZEN0006 | Generation modes conflict or an assembly generation attribute is duplicated. | Keep one configuration mode and one assembly-level `[GenerateZendiator]` attribute. |
| <a id="zen0007"></a>ZEN0007 | A generated namespace or reserved type name conflicts with application code. | Set a valid, distinct generation namespace or rename the conflicting application declaration. |
| <a id="zen0008"></a>ZEN0008 | Request or handler contracts combine incompatible response, void, sync or multiple-handler shapes. | Keep one intended request kind and its matching handler contract; do not combine native-void and legacy `Unit` handlers for one request. |
| <a id="zen0009"></a>ZEN0009 | An open handler/subscriber type parameter cannot be inferred from its request/notification pattern. | Bind every receiver type parameter to one supported pattern position. Use a closed subscriber for a fixed notification specialization. |
| <a id="zen0010"></a>ZEN0010 | Open and closed bindings overlap, or generic mappings disagree. | Keep one authoritative binding or align the generic patterns named in the message; do not rely on implicit specialization priority. |
| <a id="zen0011"></a>ZEN0011 | A handler/subscriber requires constraints its request/notification does not guarantee. | Loosen receiver constraints or tighten the corresponding request/notification declaration. |
| <a id="zen0012"></a>ZEN0012 | A ref-like request, response, notification or type parameter cannot be used by the chosen route. | Follow the indicated synchronous/ref-like contract where supported; use an ownable response and ordinary notification type. See [limits](../release/known-limitations.md). |
| <a id="zen0013"></a>ZEN0013 | A registered handler or notification type is invalid. | Correct the named registration's type, public accessibility and implemented contract; supply the required `typeof` or generic argument. |
| <a id="zen0014"></a>ZEN0014 | Retained ID for unsupported notification erasure; it is not emitted by the 0.6.0 generator. | If encountered, check the actual generator/package version and full message. Current notification erasure accepts registered closed types; see [limits](../release/known-limitations.md). |
| <a id="zen0015"></a>ZEN0015 | Attribute-based configuration conflicts with an `AddZendiator` configuration lambda. | Choose one source for the compilation and move the named settings into it. |
| <a id="zen0016"></a>ZEN0016 | Multiple `AddZendiator` lambdas describe different structures. | Merge the settings into one consistent configuration; the message identifies the first conflicting location. |
| <a id="zen0017"></a>ZEN0017 | A configuration lambda, statement or argument is not statically supported. | Use a synchronous lambda with direct recorder calls and supported constant/`typeof` arguments; remove dynamic control flow and unsupported calls. |
| <a id="zen0018"></a>ZEN0018 | A configuration value, assembly, order or registration is invalid or duplicated. | Correct the named assignment or registration, keeping one value for each setting and unique behavior order values. |
| <a id="zen0019"></a>ZEN0019 | Retained ID for ambiguous registration binding; it is not emitted by the 0.6.0 generator. | Check the actual generator/package version and full message before changing registration; current conflicts use the diagnostics above. |
| <a id="zen0020"></a>ZEN0020 | An `AddZendiator` call cannot connect to generated registration. | Keep the call in an ordinary method body and use the supported registration shape. |

## Lifetime warnings

The analyzer proves direct mistakes within a method. No warning does not establish
safe scope usage across arbitrary asynchronous control flow. See the
[capture and lifetime rules](../getting-started-0.5.0.md#scope-and-asynchronous-results).

| ID | Target and reason | Where to fix |
|---|---|---|
| <a id="zen0021"></a>ZEN0021 | Code casts or tests a generated Mediator as `IDisposable`. | Dispose the DI scope/provider that owns its dependencies; remove the Mediator disposal cast or test. |
| <a id="zen0022"></a>ZEN0022 | A returned Mediator escapes the `using` scope that supplied it. | Keep its use inside the scope, or make the caller own the scope through dispatch, enumeration and cleanup. |
| <a id="zen0023"></a>ZEN0023 | A dispatch follows explicit disposal of the Mediator's scope. | Move disposal after all awaited dispatch and Stream cleanup, or resolve from a new live scope. |

## Composition contracts

| ID | Target and reason | Where to fix |
|---|---|---|
| <a id="zen0025"></a>ZEN0025 | A stage/factory lacks the required contract, or an Owned session lacks the framework's exact cleanup contract. | Correct the hook/wrapper/factory registration or the session's `IDisposable`/`IAsyncDisposable` implementation using the [contract correspondence](ZEN0025.md). |
| <a id="zen0026"></a>ZEN0026 | A stage's request, response/item or session types do not match its route. | Trace each type through the route and correct the contract/registration; see [type mismatch](#zen0026-stage-type-mismatch). |
| <a id="zen0027"></a>ZEN0027 | A stage/factory has multiple applicable contracts for the same phase, including a combined Stream Filter and Map. | Split the competing capabilities into separate stage types or supply an explicit adapter for the intended contract. |
| <a id="zen0028"></a>ZEN0028 | Pipeline declarations overlap or disagree on an authoritative order. | Keep one nonoverlapping request list and one explicit stage order; the generator does not infer open/closed specialization priority. |
| <a id="zen0029"></a>ZEN0029 | A dispatch family is absent, ambiguous, unknown or unsupported for the selected metadata. | Select a known `DispatchFamily` and a contract supported by that family. Notifications use their separate registration API. |
| <a id="zen0030"></a>ZEN0030 | An asynchronous hook or session factory is placed on a synchronous route. | Use the synchronous hook/factory contract or choose an asynchronous dispatch family and matching handler; blocking conversion is unsupported. |
| <a id="zen0031"></a>ZEN0031 | A capability or handler has a different response/void/item shape from its route. | Align every selected handler and stage with the route's result shape. |
| <a id="zen0033"></a>ZEN0033 | Generated code cannot access a stage or session factory. | Correct the named type/member accessibility and its assembly reference. |
| <a id="zen0034"></a>ZEN0034 | A DI route would capture a ref-like value or unsupported receiver. | Use an ownable DI service/session and a supported ref-like dispatch boundary; do not retain stack-only state. |
| <a id="zen0035"></a>ZEN0035 | The named declaration or registration cannot be generated. | Follow the specific reason in the message; [missing marker and other fixes](ZEN0035.md) distinguishes the limited automatic fix. |
| <a id="zen0037"></a>ZEN0037 | Contracts bind, but no supported execution path can be emitted. | Compare the selected shape with the supported contracts; see [incomplete execution](#zen0037-incomplete-execution-contract). |

## Opt-in pooled producers

These errors concern `[GeneratePooledProducer]`, not ordinary compiler-generated
async iterators. The [pooled producer contract](../design/pooled-producer-interceptors.md#supported-and-rejected-source)
lists supported declarations, body syntax and call sites.

| ID | Target and reason | Where to fix |
|---|---|---|
| <a id="zen0040"></a>ZEN0040 | An opted-in producer's declaration, body or visible helper graph is unsupported. | Correct the named method to a supported private, parameterless `async ValueTask<bool>` shape, or remove the opt-in and retain the original implementation. |
| <a id="zen0041"></a>ZEN0041 | Producer references escape, call sites cannot be intercepted, or interceptor configuration is missing. | Use supported explicit calls and preserve `Zendiator.Generated.Interceptors` in `InterceptorsNamespaces`; implicit iterator lowering is not intercepted. |
| <a id="zen0042"></a>ZEN0042 | The target lacks required pooling support, a helper name collides, or generated helper binding changes. | Correct the specific framework/name/binding issue in the message; keep the original method without opt-in if its semantics cannot be preserved. |

## Automatic fixes

The optional [CodeFix package](../../src/Zendiator.CodeFixes/README.md) adds a missing
`[Handler]` only for supported, unambiguous ZEN0035 cases. Fix All is limited to
the current document. Pipeline order, wrapper intent, generic binding and lifetime
ownership require application decisions; they have no automatic migration fix.

## ZEN0026: Stage type mismatch

Match the registered request and response/item types to the implemented contract.
A session factory must also match the registered session type. For explicitly
declared Send map ports, match each input/output, including nullability.

For a type-changing Stream, use `StreamPipeline<TRequest, TFinal>`. Follow the item
type from the handler through each map in registration order: each stage must
accept the current item type, including nullability, and the last output must
match `TFinal`. After an `int` to `string` map, the next filter must accept
`string`, not `int`.

Correct the contract or registration manually; choose any change of stage order
according to the intended behavior.

## ZEN0037: Incomplete execution contract

The dedicated contracts bind, but the generator cannot emit an execution path
for the selected route shape. This does not mean an implementation method is
missing.

Compare the registration with the supported API contract. If a documented
supported shape still fails, report a minimal reproduction with the registration,
full diagnostic and package version. Preserve the required stage order and
ownership when choosing another supported shape.
