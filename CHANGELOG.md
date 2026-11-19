# Changelog

## 1.0.0

- Conditional DI registration honors a null receiver. Assigning the registration method itself to a delegate now reports ZEN0020 during the build.
- Stream middleware can forward or decorate a cursor. The mediator selects the forwarding path using the cursor actually returned and its cancellation token.
- PackageReference builds report a version mismatch between Zendiator and Zendiator.Abstractions.

See the [release notes](docs/release/1.0.0-release-notes.md) and the [upgrade guide](docs/upgrading.md).

## 0.7.0

Eligible closed `SendAsync` routes with synchronous Before hooks resolve their receivers before running the first hook. Review handler construction timing and Transient sharing when upgrading applications that depend on either behavior.

## 0.6.0

The handler marker changed from `[ZendiatorHandler]` to `[Handler]`. Standard Stream middleware gained the typed `OpenAsync<TNext>` overload and retained an adapter for the existing overload.

## 0.5.0–0.5.1

Named compositions added explicit handler selection and ordered pipelines. Stream handlers can supply either an async enumerable or a named cursor. The optional CodeFixes package can add a missing `[Handler]` attribute. Version 0.5.1 added the experimental `[GeneratePooledProducer]` attribute.

## 0.1–0.4

The initial releases introduced typed dispatch, notification subscribers, multiple-handler results, synchronous requests and Streams. From 0.3.0 the default Scoped mediator reuses its Transient dependencies and the DI container owns disposal. Version 0.4.0 added synchronous Stream input validation and a span-based overload for synchronous multiple-handler results.
