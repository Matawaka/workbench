# Start here

1. Extract the archive into a dedicated directory, not inside `uu-aap`, `KONTUR`, or `FREESHIELD`.
2. Keep the full Matawaka source catalog in a sibling directory such as `%USERPROFILE%\source\Matawaka-Catalog`.
3. Build and run the WPF app.
4. After the app opens, normal v0 operation is GUI-only:
   - paste JSON from the clipboard,
   - load UTF-8 JSON from a file,
   - validate and run,
   - enable/disable the AgentHost,
   - monitor progress and cancel,
   - inspect the local Matawaka catalog,
   - optionally permit and trigger fixed `git fetch` refreshes.

`catalog.fetch` is disabled unless the user explicitly checks **Разрешить git fetch**.
