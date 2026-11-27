using System.Text.RegularExpressions;

namespace Badlands.LiveryTool;

// Token positions allow surgical insertions without reformatting FiveM Lua or touching existing permissions.
internal static class LuaLiveryEditor
{
    internal sealed record Token(string Value, int Start, int End);

    internal static List<Token> Tokens(string text)
    {
        var tokens = new List<Token>();
        for (int i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            int start = i;
            bool comment = i + 1 < text.Length && text[i..(i + 2)] == "--";
            if (comment) i += 2;
            Match longString = Regex.Match(text[i..], @"^\[(=*)\[");
            if (longString.Success)
            {
                string close = "]" + longString.Groups[1].Value + "]";
                int end = text.IndexOf(close, i + longString.Length, StringComparison.Ordinal);
                if (end < 0) throw new InvalidDataException("Unterminated Lua long string/comment.");
                i = end + close.Length;
                if (!comment) tokens.Add(new Token(text[start..i], start, i));
                continue;
            }
            if (comment) { while (i < text.Length && text[i] != '\n') i++; continue; }
            char c = text[i++];
            if (c is '\'' or '"' or '`')
            {
                bool closed = false;
                while (i < text.Length)
                {
                    if (text[i] == '\\') { i += 2; continue; }
                    if (text[i] is '\n' or '\r') throw new InvalidDataException("Unescaped newline in a Lua string.");
                    if (text[i++] == c) { closed = true; break; }
                }
                if (!closed) throw new InvalidDataException("Unterminated Lua string/hash literal.");
            }
            else if (char.IsLetterOrDigit(c) || c == '_')
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
            tokens.Add(new Token(text[start..i], start, i));
        }
        var stack = new Stack<string>();
        foreach (Token token in tokens)
        {
            if (token.Value is "{" or "[" or "(") stack.Push(token.Value);
            if (token.Value is "}" or "]" or ")")
            {
                string expected = token.Value == "}" ? "{" : token.Value == "]" ? "[" : "(";
                if (!stack.TryPop(out string? open) || open != expected) throw new InvalidDataException("Unbalanced Lua delimiters.");
            }
        }
        if (stack.Count != 0) throw new InvalidDataException("Unbalanced Lua delimiters.");
        return tokens;
    }

    private static int Close(List<Token> tokens, int open)
    {
        int depth = 0;
        for (int i = open; i < tokens.Count; i++)
        {
            if (tokens[i].Value == "{") depth++;
            if (tokens[i].Value == "}") depth--;
            if (tokens[i].Value == "}" && depth == 0) return i;
        }
        throw new InvalidDataException("Missing Lua table close.");
    }

    private static (List<Token> Tokens, int Open, int Close) Table(string text)
    {
        List<Token> tokens = Tokens(text);
        var matches = Enumerable.Range(0, Math.Max(0, tokens.Count - 2))
            .Where(i => tokens[i].Value == "custom_liveries" && tokens[i + 1].Value == "=" && tokens[i + 2].Value == "{").ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Expected one custom_liveries table assignment.");
        int open = matches[0] + 2;
        int close = Close(tokens, open);
        int cursor = open;
        ValidateLiteralTable(tokens, ref cursor);
        return (tokens, open, close);
    }

    private static void ValidateLiteralTable(List<Token> tokens, ref int cursor)
    {
        if (tokens[cursor++].Value != "{") throw new InvalidDataException("Expected Lua table.");
        while (cursor < tokens.Count && tokens[cursor].Value != "}")
        {
            if (tokens[cursor].Value == "[")
            {
                cursor++;
                Literal(tokens, ref cursor);
                if (tokens[cursor++].Value != "]" || tokens[cursor++].Value != "=") throw new InvalidDataException("Invalid key in custom_liveries.");
            }
            else if (cursor + 1 < tokens.Count && tokens[cursor + 1].Value == "=") cursor += 2;
            Literal(tokens, ref cursor);
            if (tokens[cursor].Value == "}") break;
            if (tokens[cursor].Value is not "," and not ";") throw new InvalidDataException("Missing comma between custom_liveries entries. Fix the existing Lua before importing.");
            cursor++;
        }
        if (cursor >= tokens.Count || tokens[cursor++].Value != "}") throw new InvalidDataException("Missing custom_liveries table close.");
    }

    private static void Literal(List<Token> tokens, ref int cursor)
    {
        string value = tokens[cursor].Value;
        if (value == "{") { ValidateLiteralTable(tokens, ref cursor); return; }
        if (value == "function") { FunctionLiteral(tokens, ref cursor); return; }
        if (value is "-" or "+") value = tokens[++cursor].Value;
        if (value.StartsWith('\'') || value.StartsWith('"') || value.StartsWith('`') || value.StartsWith('[') ||
            value is "true" or "false" or "nil" || double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
        { cursor++; return; }
        throw new InvalidDataException("Unsupported expression in custom_liveries. Resolve it to a literal restriction table before importing.");
    }

    private static void FunctionLiteral(List<Token> tokens, ref int cursor)
    {
        // Existing BLRP restrictions include inline permission functions. Retain their original bytes;
        // only walk block/delimiter boundaries so their contents cannot be mistaken for slot entries.
        if (cursor + 1 >= tokens.Count || tokens[cursor + 1].Value != "(") throw new InvalidDataException("Expected an inline restriction function.");
        var blocks = new Stack<string>(); blocks.Push("function");
        var delimiters = new Stack<string>();
        while (++cursor < tokens.Count)
        {
            string value = tokens[cursor].Value;
            if (value is "(" or "[" or "{") delimiters.Push(value);
            else if (value is ")" or "]" or "}")
            {
                string expected = value == ")" ? "(" : value == "]" ? "[" : "{";
                if (!delimiters.TryPop(out string? open) || open != expected) throw new InvalidDataException("Unclosed restriction function before its containing table ends.");
            }
            else if (value is "function" or "if" or "do" or "repeat") blocks.Push(value);
            else if (value is "end" or "until")
            {
                if (!blocks.TryPop(out string? block) || (value == "until") != (block == "repeat")) throw new InvalidDataException("Unbalanced restriction function blocks.");
                if (blocks.Count == 0)
                {
                    if (delimiters.Count != 0) throw new InvalidDataException("Unbalanced restriction function delimiters.");
                    cursor++;
                    return;
                }
            }
        }
        throw new InvalidDataException("Missing restriction function end.");
    }

    internal static HashSet<int> Slots(string text, string model)
    {
        var (tokens, open, close) = Table(text);
        var result = new HashSet<int>();
        int blocks = 0;
        for (int i = open + 1; i < close; i++)
        {
            if (i + 4 < close && tokens[i].Value == "[" && tokens[i + 2].Value == "]" && tokens[i + 3].Value == "=" && tokens[i + 4].Value == "{")
            {
                int end = Close(tokens, i + 4);
                if (tokens[i + 1].Value.Equals("`" + model + "`", StringComparison.OrdinalIgnoreCase))
                {
                    blocks++;
                    for (int j = i + 5; j + 3 < end; j++)
                    {
                        if (tokens[j].Value == "{" ) { j = Close(tokens, j); continue; }
                        if (tokens[j].Value == "function") { int next = j; FunctionLiteral(tokens, ref next); j = next - 1; continue; }
                        if (tokens[j].Value == "[" && int.TryParse(tokens[j + 1].Value, out int slot) && tokens[j + 2].Value == "]" && tokens[j + 3].Value == "=")
                            if (!result.Add(slot)) throw new InvalidDataException($"Duplicate Lua slot {slot} for {model}.");
                    }
                }
                i = end;
            }
        }
        if (blocks > 1) throw new InvalidDataException($"Duplicate Lua vehicle tables for {model}.");
        return result;
    }

    internal static string Append(string text, string model, int slot, string label, string displayName, string accessGroup, string permission, IEnumerable<string>? relatedLabels = null)
    {
        if (Slots(text, model).Contains(slot)) throw new InvalidDataException($"Lua restriction already occupies {model} slot {slot}; existing permissions are preserved.");
        var tokens = Tokens(text);
        if (Enumerable.Range(0, Math.Max(0, tokens.Count - 2)).Any(i => tokens[i].Value == "AddTextEntry" && tokens[i + 1].Value == "(" &&
            tokens[i + 2].Value.Trim('\'', '"').Equals(label, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Text label {label} already exists.");
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        text = InsertDisplayName(text, label, displayName, relatedLabels, newline);
        if (string.IsNullOrWhiteSpace(accessGroup)) return text; // public liveries have no restriction entry
        string value = accessGroup.Trim() + (string.IsNullOrWhiteSpace(permission) ? "" : ":" + permission.Trim().TrimStart(':'));
        var table = Table(text);
        tokens = table.Tokens;
        for (int i = table.Open + 1; i + 4 < table.Close; i++)
        {
            if (tokens[i].Value == "[" && tokens[i + 1].Value.Equals("`" + model + "`", StringComparison.OrdinalIgnoreCase) && tokens[i + 4].Value == "{")
            {
                int end = Close(tokens, i + 4);
                string indent = EntryIndent(text, tokens, i + 4, end);
                return InsertEntry(text, tokens, i + 4, end, $"{indent}[{slot}] = '{Escape(value)}',", newline);
            }
            if (tokens[i].Value == "{") i = Close(tokens, i);
        }
        var models = new List<(string Key, int Start, int Open, int Close)>();
        for (int i = table.Open + 1; i + 4 < table.Close; i++)
        {
            if (tokens[i].Value == "[" && tokens[i + 1].Value.StartsWith('`') && tokens[i + 3].Value == "=" && tokens[i + 4].Value == "{")
            {
                int end = Close(tokens, i + 4);
                models.Add((tokens[i + 1].Value.Trim('`'), tokens[i].Start, i + 4, end));
                i = end;
            }
            else if (tokens[i].Value == "{") i = Close(tokens, i);
        }
        int next = SortedInsertion(models.Select(item => item.Key).ToArray(), model);
        var neighbor = models.Count == 0 ? default : models[Math.Min(next, models.Count - 1)];
        string outerIndent = models.Count == 0 ? Indent(text, tokens[table.Close].Start) + "  " : Indent(text, neighbor.Start);
        if (outerIndent.Length == 0) outerIndent = "  ";
        string innerIndent = models.Count == 0 ? outerIndent + "  " : EntryIndent(text, tokens, neighbor.Open, neighbor.Close);
        string entry = $"{outerIndent}[`{model}`] = {{{newline}{innerIndent}[{slot}] = '{Escape(value)}',{newline}{outerIndent}}},";
        if (next < models.Count)
        {
            int position = LineStart(text, models[next].Start);
            // Inline tables have no line boundary at which to insert a separate vehicle block.
            if (text[position..models[next].Start].All(char.IsWhiteSpace))
            {
                string updated = text.Insert(position, entry + newline + newline);
                Table(updated);
                return updated;
            }
        }
        int closeLine = LineStart(text, tokens[table.Close].Start);
        bool separated = text[..closeLine].EndsWith(newline + newline, StringComparison.Ordinal);
        return InsertEntry(text, tokens, table.Open, table.Close, (models.Count > 0 && !separated ? newline : "") + entry, newline);
    }

    private sealed record DisplayEntry(string Label, int Start, int End, char LabelQuote, char ValueQuote);

    private static string InsertDisplayName(string text, string label, string displayName, IEnumerable<string>? relatedLabels, string newline)
    {
        var table = Table(text);
        var tokens = table.Tokens;
        var entries = new List<DisplayEntry>();
        for (int i = 0; i + 5 < table.Open; i++)
        {
            if (tokens[i].Value != "AddTextEntry" || tokens[i + 1].Value != "(" ||
                !(tokens[i + 2].Value.StartsWith('\'') || tokens[i + 2].Value.StartsWith('"'))) continue;
            int depth = 0, end = i + 1;
            for (; end < table.Open; end++)
            {
                if (tokens[end].Value == "(") depth++;
                if (tokens[end].Value == ")" && --depth == 0) break;
            }
            if (end >= table.Open) continue;
            entries.Add(new DisplayEntry(tokens[i + 2].Value.Trim('\'', '"'), tokens[i].Start, tokens[end].End, tokens[i + 2].Value[0],
                tokens[i + 4].Value.StartsWith('"') ? '"' : '\''));
            i = end;
        }
        var related = (relatedLabels ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Matches(DisplayEntry entry) => related.Contains(entry.Label) || related.Count == 0 && Family(entry.Label).Equals(Family(label), StringComparison.OrdinalIgnoreCase);
        // Prefer the established vehicle group over isolated entries previously appended elsewhere.
        int bestStart = -1, bestLength = 0;
        for (int i = 0; i < entries.Count;)
        {
            if (!Matches(entries[i])) { i++; continue; }
            int start = i;
            while (i < entries.Count && Matches(entries[i])) i++;
            if (i - start > bestLength) { bestStart = start; bestLength = i - start; }
        }
        int position;
        DisplayEntry? neighbor;
        if (bestStart >= 0)
        {
            neighbor = entries[bestStart + bestLength - 1];
            int lineEnd = text.IndexOf('\n', neighbor.End);
            position = lineEnd < 0 ? text.Length : lineEnd + 1;
        }
        else if (entries.Count > 0)
        {
            int next = SortedInsertion(entries.Select(entry => Family(entry.Label)).ToArray(), Family(label));
            neighbor = entries[Math.Min(next, entries.Count - 1)];
            position = next < entries.Count ? LineStart(text, neighbor.Start) :
                text.IndexOf('\n', neighbor.End) is int lineEnd && lineEnd >= 0 ? lineEnd + 1 : text.Length;
        }
        else
        {
            neighbor = null;
            int assignment = text.LastIndexOf("custom_liveries", tokens[table.Open].Start, StringComparison.Ordinal);
            position = LineStart(text, assignment); // Retain a leading 'local' with its declaration.
        }
        string Quote(string value, char quote) => quote + value.Replace("\\", "\\\\").Replace(quote.ToString(), "\\" + quote).Replace("\r", "\\r").Replace("\n", "\\n") + quote;
        string indent = neighbor == null ? "" : Indent(text, neighbor.Start);
        return text.Insert(position, (position > 0 && text[position - 1] != '\n' ? newline : "") +
            $"{indent}AddTextEntry({Quote(label, neighbor?.LabelQuote ?? '\'')}, {Quote(displayName, neighbor?.ValueQuote ?? '\'')}){newline}");
    }

    private static string Family(string label) => Regex.Replace(label, @"\d+$", "");
    private static int LineStart(string text, int position) => position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;
    private static string Indent(string text, int position)
    {
        string prefix = text[LineStart(text, position)..position];
        return prefix.All(char.IsWhiteSpace) ? prefix : "";
    }
    private static string EntryIndent(string text, List<Token> tokens, int open, int close)
    {
        if (open + 1 < close && LineStart(text, tokens[open + 1].Start) > LineStart(text, tokens[open].Start))
            return Indent(text, tokens[open + 1].Start);
        return Indent(text, tokens[close].Start) + "  ";
    }
    private static int SortedInsertion(string[] keys, string key)
    {
        // Follow the main ordered section while preserving hand-placed blocks at either end.
        int bestStart = 0, bestLength = 0;
        for (int i = 0; i < keys.Length;)
        {
            int start = i++;
            while (i < keys.Length && StringComparer.OrdinalIgnoreCase.Compare(keys[i - 1], keys[i]) <= 0) i++;
            if (i - start > bestLength) { bestStart = start; bestLength = i - start; }
        }
        if (bestLength < 2) return keys.Length;
        for (int i = bestStart; i < bestStart + bestLength; i++)
            if (StringComparer.OrdinalIgnoreCase.Compare(keys[i], key) > 0) return i;
        return bestStart + bestLength;
    }

    private static string InsertEntry(string text, List<Token> tokens, int open, int close, string entry, string newline)
    {
        int insert = tokens[close].Start;
        int line = text.LastIndexOf('\n', Math.Max(0, insert - 1)) + 1;
        bool ownLine = text[line..insert].All(char.IsWhiteSpace);
        if (ownLine) insert = line;
        string updated = text.Insert(insert, (ownLine ? "" : newline) + entry + newline);
        Token previous = tokens[close - 1];
        if (close > open + 1 && previous.Value is not "," and not ";")
            updated = updated.Insert(previous.End, ","); // before any trailing comment
        Table(updated);
        return updated;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n");
}
