# Analyzer reference

Termina ships Roslyn analyzers that report common layout node errors at build time.

This reference covers the analyzer rules that exist in the current source:

- [TERMINA002: Layout nodes in ViewModels](./termina002)
- [TERMINA003: Layout node child disposal](./termina003)
- [TERMINA004: Stateful node recreation](./termina004)

Termina does not define a `TERMINA001` rule in the current source.

The analyzer package contains no automatic code fixes. Apply the fix guidance on each rule page, then rebuild the project.
