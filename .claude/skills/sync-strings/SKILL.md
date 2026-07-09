---
name: sync-strings
description: Add a new UI string field with EN/ZH/JA translations to Strings.cs. Use when adding a new label to the editor UI.
---

# Sync Strings

When adding a new UI string to `Strings.cs`, follow this exact pattern to keep all 3 languages in sync.

## Template

### 1. Add field declaration (~line 90 in the appropriate group)
```csharp
public string NewField;   // "English default"
```

### 2. Add to English instance (En, ~line 160)
```csharp
NewField = "English text",
```

### 3. Add to Chinese instance (ZhCN, ~line 220)
```csharp
NewField = "中文文本",
```

### 4. Add to Japanese instance (Ja, ~line 280)
```csharp
NewField = "日本語テキスト",
```

## Rules
- The field name is PascalCase, same across all languages
- Keep entries in the same order across all 4 locations
- Use `Fmt` suffix for format strings (e.g., `ProgressFmt`)
- Use `Btn` suffix for button labels, `Dlg` prefix for dialog strings
- If unsure about translation quality, flag with a `// TODO: review` comment

## Verification
After adding, verify:
- `grep "NewField" Editor/Strings.cs` returns exactly 4 lines (1 declaration + 3 assignments)
- No field is assigned in only 2 of 3 languages
