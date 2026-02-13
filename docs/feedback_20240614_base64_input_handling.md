# Feedback: Base64 Input Handling in LLM/API Integration

**Date**: 2024-06-14  
**Author**: Qwen Code Agent  
**Related Issue**: `[API Error: <400> InternalError.Algo.InvalidParameter: Range of input length should be [1, 260096]`

---

## 🔹 Problem Summary
When passing base64-encoded data (e.g., screenshot from `mcp__WindowsDesktopUse__see`) to an LLM or internal API, the request failed with:
```
[API Error: <400> InternalError.Algo.InvalidParameter: Range of input length should be [1, 260096]
```

## 🔹 Root Cause
The base64 string contained newline characters (`\n`, `\r`). Although RFC 4648 permits line breaks in base64 for readability (e.g., PEM), the receiving API/LLM parser:
- Treated newlines as invalid characters, or
- Counted them toward the input length, exceeding the 260,096 limit, or
- Parsed the input as empty due to parsing failure → length = 0.

## 🔹 Verification Steps
1. Captured image via `mcp__WindowsDesktopUse__see`.
2. Inspected raw base64 output — confirmed embedded `\n`.
3. Removed newlines → retry succeeded.

## 🔹 Immediate Fix
Always normalize base64 before sending to APIs:
```csharp
public static class Base64Utils
{
    public static string NormalizeForApi(string base64) =>
        base64?.Replace("\n", "").Replace("\r", "") ?? string.Empty;
}
```

## 🔹 Prevention Measures
| Area | Action |
|------|--------|
| ✅ Input Validation | Enforce `NormalizeForApi()` on all base64 inputs before API calls |
| ✅ Logging | Add debug log: `Console.Error.WriteLine($"[DEBUG] Base64 length after normalization: {clean.Length}");` |
| ✅ Testing | Add unit test: `GivenBase64WithNewlines_WhenNormalized_ThenLengthValid()` |
| ✅ Documentation | Update `DEVELOPMENT.md`: “All base64 payloads must be stripped of whitespace and newlines.” |

## 🔹 Note on Logging
Per project guidelines, all logs use `Console.Error.WriteLine` — stdout is reserved for JSON-RPC.

---
*This document follows the naming convention: `feedback_YYYYMMDD_theme.md`.*