# Configuration diagnostics

Use the diagnostic ID and source location to find the declaration or registration
to change. Configuration errors prevent route generation. Lifetime diagnostics
are warnings. If the C# compiler reports another error on the same declaration,
correct that error first.

## Discovery and configuration

These diagnostics concern assembly discovery, generic binding and the compatibility
configuration API. New explicit compositions can instead report the dedicated
contract diagnostics below.

| ID | Target and reason | Where to fix |
|---|---|---|
| <a id="zen0001"></a>ZEN0001 | A discovered request has no matching handler. | Include the handler's assembly and implement the request's response/void and sync/async contract. For explicit compositions, also select the marked handler with `Handlers`. |
| <a id="zen0002"></a>ZEN0002 | A single-handler request has multiple matching handlers. | Remove the overlapping registration or choose an explicit multiple-handler dispatch contract when fan-out is intended. |
| <a id="zen0003"></a>ZEN0003 | A request, handler or subscriber has an unsupported contract or accessibility. | Check the declaration's accessibility, response type and generic arguments. Use matching Zendiator package versions. |
| <a id="zen0004"></a>ZEN0004 | A compatibility pipeline behavior has an invalid contract, duplicate type/order or no matching request. | Correct the behavior and its `PipelineBehavior` or `AddOpenBehavior` registration. Give each behavior its intended order. |
| <a id="zen0005"></a>ZEN0005 | A compatibility mediator declaration or assembly marker is invalid. | Declare a class-level mediator as an empty, top-level `public sealed partial class Zendiator`. Set `Namespace` on the assembly-level configuration. |
| <a id="zen0006"></a>ZEN0006 | Generation modes conflict or an assembly generation attribute is duplicated. | Keep one configuration mode and one assembly-level `[GenerateZendiator]` attribute. |
| <a id="zen0007"></a>ZEN0007 | A generated namespace or reserved type name conflicts with application code. | Set a valid, distinct generation namespace or rename the conflicting application declaration. |
| <a id="zen0008"></a>ZEN0008 | Request or handler contracts combine incompatible response, void, sync or multiple-handler shapes. | Choose one request kind and match its handler contract. For commands, choose either native void or legacy `Unit`. |
| <a id="zen0009"></a>ZEN0009 | An open handler/subscriber type parameter cannot be inferred from its request/notification pattern. | Bind every receiver type parameter to one supported pattern position. Use a closed subscriber for a fixed notification specialization. |
| <a id="zen0010"></a>ZEN0010 | Open and closed bindings overlap, or generic mappings disagree. | Remove the overlapping registration or align the generic patterns named in the message. |
| <a id="zen0011"></a>ZEN0011 | A handler/subscriber requires constraints its request/notification does not guarantee. | Loosen receiver constraints or tighten the corresponding request/notification declaration. |
| <a id="zen0012"></a>ZEN0012 | A ref-like request, response, notification or type parameter cannot be used by the chosen route. | Use the synchronous contract indicated for a ref-like request. Choose a response type that can outlive the call and a non-ref-like notification type. [Dispatch limits](../release/known-limitations.md) |
| <a id="zen0013"></a>ZEN0013 | A registered handler or notification type is invalid. | Check the registered type, its accessibility and its implemented contract. Pass the required `typeof` or generic argument. |
| <a id="zen0014"></a>ZEN0014 | Older diagnostic for unsupported notification erasure. | Check the package version that reported the error. Notification erasure accepts registered closed types. [Dispatch limits](../release/known-limitations.md#native-aot-and-dispatch-boundaries) |
| <a id="zen0015"></a>ZEN0015 | Attribute-based configuration conflicts with an `AddZendiator` configuration lambda. | Choose one source for the compilation and move the named settings into it. |
| <a id="zen0016"></a>ZEN0016 | Multiple `AddZendiator` lambdas describe different structures. | Start at the first conflict named in the message. Merge the settings into one consistent configuration. |
| <a id="zen0017"></a>ZEN0017 | A configuration lambda, statement or argument is not statically supported. | Use a synchronous lambda that calls configuration methods directly. Pass constants or `typeof` arguments and move unrelated logic outside the lambda. |
| <a id="zen0018"></a>ZEN0018 | A configuration value, assembly, order or registration is invalid or duplicated. | Correct the named assignment or registration, keeping one value for each setting and unique behavior order values. |
| <a id="zen0019"></a>ZEN0019 | Older diagnostic for ambiguous registration binding. | Check the package version and inspect the conflicting registrations named in the message. |
| <a id="zen0020"></a>ZEN0020 | An `AddZendiator` call cannot connect to generated registration, or the method itself is used as a delegate. | Call the method directly. When passing registration as a callback, put the direct call inside a lambda or a helper method. |

## Lifetime warnings

The analyzer detects direct lifetime mistakes within a method. Keep the scope
alive across asynchronous calls and cleanup, including when a method returns a
Stream to its caller. [Scope lifetime](../getting-started.md#keep-the-scope-alive)

| ID | Target and reason | Where to fix |
|---|---|---|
| <a id="zen0021"></a>ZEN0021 | Code casts or tests a generated Mediator as `IDisposable`. | Remove the cast or type test. Dispose the DI scope or provider that owns the dependencies. |
| <a id="zen0022"></a>ZEN0022 | A returned Mediator escapes the `using` scope that supplied it. | Keep its use inside the scope, or make the caller own the scope through dispatch, enumeration and cleanup. |
| <a id="zen0023"></a>ZEN0023 | A dispatch follows explicit disposal of the Mediator's scope. | Move disposal after all awaited dispatch and Stream cleanup, or resolve from a new live scope. |

## Composition contracts

| ID | Target and reason | Where to fix |
|---|---|---|
| <a id="zen0025"></a>ZEN0025 | A stage/factory lacks the required contract, or an Owned session lacks the framework's exact cleanup contract. | Correct the hook/wrapper/factory registration or the session's `IDisposable`/`IAsyncDisposable` implementation using the [contract correspondence](ZEN0025.md). |
| <a id="zen0026"></a>ZEN0026 | A stage's request, response/item or session types do not match its route. | Follow the types through the route and correct the first mismatch. [Stage type mismatch](#zen0026-stage-type-mismatch) |
| <a id="zen0027"></a>ZEN0027 | A stage/factory has multiple applicable contracts for the same phase, including a combined Stream Filter and Map. | Split the competing capabilities into separate stage types or supply an explicit adapter for the intended contract. |
| <a id="zen0028"></a>ZEN0028 | Pipeline declarations overlap or disagree on an authoritative order. | Remove overlapping request lists and specify the stages in execution order. |
| <a id="zen0029"></a>ZEN0029 | A dispatch family is absent, ambiguous, unknown or unsupported for the selected metadata. | For named Send and Stream routes, select the matching family. For notifications, register the subscriber assembly with the non-generic `AddZendiator`. [Send and Publish example](../getting-started.md#send-a-request-and-publish-a-notification) |
| <a id="zen0030"></a>ZEN0030 | An asynchronous hook or session factory is placed on a synchronous route. | Use a synchronous hook or factory. If the operation must await work, choose an asynchronous dispatch family and a matching handler. |
| <a id="zen0031"></a>ZEN0031 | A capability or handler has a different response/void/item shape from its route. | Align every selected handler and stage with the route's result shape. |
| <a id="zen0033"></a>ZEN0033 | Generated code cannot access a stage or session factory. | Correct the named type/member accessibility and its assembly reference. |
| <a id="zen0034"></a>ZEN0034 | A DI route would capture a ref-like value or unsupported receiver. | Keep stack-only values within the supported ref-like call. Use a DI service or session that can outlive the call. |
| <a id="zen0035"></a>ZEN0035 | The named declaration or registration cannot be generated. | Correct the cause named in the message. [Missing handler marker and other fixes](ZEN0035.md) |
| <a id="zen0037"></a>ZEN0037 | Contracts bind, but no supported execution path can be emitted. | Check the selected route against the supported contracts. [Incomplete execution contract](#zen0037-incomplete-execution-contract) |

## Opt-in pooled producers

These errors apply to methods marked with `[GeneratePooledProducer]`. See
[supported declarations and calls](../pooled-producer.md#supported-declarations-and-calls).

| ID | Target and reason | Where to fix |
|---|---|---|
| <a id="zen0040"></a>ZEN0040 | An opted-in producer's declaration, body or visible helper graph is unsupported. | Correct the named method to a supported private, parameterless `async ValueTask<bool>` shape, or remove the opt-in and retain the original implementation. |
| <a id="zen0041"></a>ZEN0041 | Producer references escape, call sites cannot be intercepted, or interceptor configuration is missing. | Call the marked method explicitly. Keep `Zendiator.Generated.Interceptors` in `InterceptorsNamespaces`. |
| <a id="zen0042"></a>ZEN0042 | The target lacks required pooling support, a helper name collides, or generated helper binding changes. | Fix the framework, name or binding issue identified by the message. Remove the attribute if pooling would change the method's behavior. |

## Automatic fixes

The optional [CodeFix package](../../src/Zendiator.CodeFixes/README.md) adds a missing
`[Handler]` only for supported, unambiguous ZEN0035 cases. Fix All is limited to
the current document. Review pipeline order, wrapper behavior, generic bindings
and dependency lifetimes manually.

## ZEN0026: Stage type mismatch

Match the registered request and response/item types to the implemented contract.
A session factory must also match the registered session type. For explicitly
declared Send map ports, match each input/output, including nullability.

For a type-changing Stream, use `StreamPipeline<TRequest, TFinal>`. Follow the
item from the handler through each map in registration order. Match each stage
to the previous output type, including nullability. For example, the filter
after an `int` to `string` map must accept `string`. Match the final output to
`TFinal` and preserve the order your application needs.

## ZEN0037: Incomplete execution contract

The contracts bind successfully, but the selected combination has no supported
execution path.

Compare the registration with the supported API contract. If a documented
supported shape still fails, report a minimal reproduction with the registration,
full diagnostic and package version. Preserve the required stage order and
ownership when choosing another supported shape.
