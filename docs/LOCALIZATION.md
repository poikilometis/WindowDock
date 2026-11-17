# Localization

## Selection

WindowDock reads `CultureInfo.CurrentUICulture` before constructing its models or Avalonia interface. It selects a supported language and sets the process UI culture; number and date formatting (`CurrentCulture`) continues to follow OS preferences. The OS display language takes effect on the next app launch.

`--language <code>` overrides automatic selection; `WINDOWDOCK_LANGUAGE` is the environment-variable alternative. The command-line option takes precedence. These are useful for testing and for users whose preferred app language differs from the OS. Unsupported values fall back to English.

Country variants use their parent language. Chinese is resolved by script first: `zh-Hans` is simplified, `zh-Hant` is traditional. Without an explicit script, Taiwan, Hong Kong, and Macao select traditional; mainland China, Singapore, and neutral `zh` select simplified.

Existing workspace names, panel aliases, project names, commands, protocol tokens, and provider names are not translated. Newly created default names use the selected interface language.

## Resource files

All catalogs live in `src/WindowDock.Core/Resources/`:

- `Strings.resx`: neutral English and fallback text.
- `Strings.ru.resx`, `Strings.es.resx`, `Strings.vi.resx`.
- `Strings.zh-Hant.resx`, `Strings.zh-Hans.resx`.
- `Strings.th.resx`, `Strings.ja.resx`, `Strings.ko.resx`.
- `Strings.de.resx`, `Strings.tr.resx`.

Resource keys are stable semantic identifiers. The `L10n` helper serves the desktop interface, core validation messages, and Windows backend errors. Each catalog currently has 299 entries. Native OS dialogs and messages supplied directly by Windows or a third-party app follow that component's language.

## Editing a translation

1. Edit the value in the appropriate UTF-8 `.resx` file, using an XML or .NET resource editor.
2. Preserve keys and placeholders such as `{0}`, `{1}`, and the literal launch macro `{agentId}`. Preserve intentional leading/trailing spaces in message fragments.
3. Preserve product names, `/hooks`, `PATH`, `KEY=value`, and keyboard shortcuts. Avoid translating protocol identifiers.
4. Keep labels concise. Supporting text may wrap; button labels should remain readable in compact controls.
5. Run the core suite and, on Windows, `--self-test`. Test the locale with `--language <code>` and a separate `--data-dir`.

For a new language, add a complete catalog and update `L10n.SupportedLanguages`, `ResolveLanguage`, the tests, and the README language table. English and German/Spanish singular forms and Russian one/few/other column counts are selected by `L10n.Columns`; other current languages use their appropriate count label.

## Checks

Core tests require identical keys in every catalog, nonempty values, identical formatting placeholders, valid composite formats, correct culture aliases, and preservation of user names. Windows tests render 11 editor types in each locale at their minimum widths and check for clipped labels. Representative English, German, Thai, Japanese, and traditional Chinese images are saved under `artifacts/`.

Automated checks do not establish linguistic quality. Native-speaker corrections and terminology improvements are welcome; include the locale, screen, existing wording, and suggested replacement.