# Translating WowStopwatcher

Each language is one JSON file in `src/WowStopwatcher.Core/Localization/Translations/`, named by its ISO language code
(`en.json`, `uk.json`). `en.json` is the base and the fallback. No code or project edit is needed to add a language.

## Add a language

1. Copy `en.json` to `<code>.json` (for example `de.json`).
2. Set `_meta`: `name` is the language in itself ("Deutsch"), `englishName` is its English name ("German").
3. Translate the values. Do not change the keys.
4. Keep every `{placeholder}` exactly as in English (same names); you may move it within the sentence.
   Use `{{` and `}}` for a literal brace.
5. Run `dotnet test src/WowStopwatcher.slnx`. The tests check every translation file and name the file and key to fix.
6. Try it: `dotnet run --project src/WowStopwatcher.App`, choose the language in Settings, then restart (the Settings page offers a Restart button).

A translation may be partial: keys you leave out fall back to English. Keys that do not exist in `en.json` are an error (usually a typo).

## Plurals

A text that depends on a number is an object of plural categories instead of a string, and `{count}` is the number:

```json
"stats.goalsReached": { "one": "{count} goal reached", "other": "{count} goals reached" }
```

Use the categories your language needs (`zero`, `one`, `two`, `few`, `many`, `other`); see the
[CLDR plural rules](https://www.unicode.org/cldr/charts/latest/supplemental/language_plural_rules.html).
English needs `one` and `other`; Ukrainian needs `one`, `few`, `many` and `other`.
A key that is a plural in `en.json` must be a plural in your file, and the other way round.

If your language has plural rules that are not in `PluralRules` (`src/WowStopwatcher.Core/Localization/PluralRules.cs`),
add its code to `GetCategory` and `RequiredCategories` there. Languages not listed use the English rule.

## Adding a new text (developers)

1. Add the key to `en.json` (flat, dotted lowercase camelCase, for example `today.stopAll`). Put it in the other files when you can.
2. In code use `Loc.T("today.stopAll")`, `Loc.T("key", ("name", value))` or `Loc.Plural("key", count)`.
   In XAML use `Text="{loc:T today.stopAll}"` with `xmlns:loc="using:WowStopwatcher.App.Localization"`.
3. Dates use `Loc.Culture` for day and month names. Numbers in placeholders are formatted with the invariant culture.
