# Configuration diagnostics

These generator diagnostics have severity Error. Suppression does not enable a
rejected route. Zendiator provides no CodeFix for ZEN0026 or ZEN0037.

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
