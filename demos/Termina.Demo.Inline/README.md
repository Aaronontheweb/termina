# Termina Inline Demo

This demo proves the opt-in primary-buffer presentation mode.

Run it from the repository root:

```bash
dotnet run --project demos/Termina.Demo.Inline/Termina.Demo.Inline.csproj
```

Use these keys:

- `Enter` commits one stable block.
- `R` changes the live region.
- `Ctrl+Q` exits the application.

After several commits, use native terminal selection and scrollback.

Set `TERMINA_INLINE_TRACE` to a file path when you need lifecycle traces.

```bash
TERMINA_INLINE_TRACE=/tmp/termina-inline.log \
  dotnet run --project demos/Termina.Demo.Inline/Termina.Demo.Inline.csproj
```

CAUTION: Do not send normal log text to standard output during the session.

Direct standard output does not use the inline coordinator. It can damage the live region.
