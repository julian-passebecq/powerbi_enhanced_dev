using System.Text;

namespace PbiBench.Core.Automation;

/// <summary>Interprets a deliberately small C#-shaped grammar. It never compiles code, resolves CLR types or invokes arbitrary members.</summary>
public static class SafeCSharpParser
{
    public static ScriptParseResult Parse(string source, string name = "Safe script")
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (source.Length > 262144) return new(null, new[] { new ScriptParseIssue(0, "Safe scripts are limited to 256 KiB.") });
        try { var parser = new Parser(source); var recipe = new ActionRecipe(name, parser.Read()); ActionRecipeRules.Validate(recipe); return new(recipe, Array.Empty<ScriptParseIssue>()); }
        catch (ParseFailure error) { return new(null, new[] { new ScriptParseIssue(error.Offset, error.Message) }); }
        catch (ArgumentException error) { return new(null, new[] { new ScriptParseIssue(0, error.Message) }); }
    }

    private sealed record Token(string Text, bool String, int Offset);
    private sealed class ParseFailure(int offset, string message) : Exception(message) { public int Offset { get; } = offset; }
    private sealed class Parser
    {
        private readonly List<Token> tokens = new(); private int at; private readonly List<RecipeStep> steps = new();
        public Parser(string source)
        {
            for (var i = 0; i < source.Length;)
            {
                var start = i; var c = source[i++]; if (char.IsWhiteSpace(c)) continue;
                if (c == '/' && i < source.Length && source[i] == '/') { while (i < source.Length && source[i] != '\n') i++; continue; }
                if (c == '/' && i < source.Length && source[i] == '*') { i++; var close = source.IndexOf("*/", i, StringComparison.Ordinal); if (close < 0) throw new ParseFailure(start, "Unterminated comment."); i = close + 2; continue; }
                var verbatim = c == '@' && i < source.Length && source[i] == '"'; if (verbatim) { c = '"'; i++; }
                if (c == '"')
                {
                    var text = new StringBuilder(); var closed = false;
                    while (i < source.Length)
                    {
                        c = source[i++];
                        if (c == '"') { if (verbatim && i < source.Length && source[i] == '"') { text.Append('"'); i++; continue; } closed = true; break; }
                        if (!verbatim && c == '\\')
                        {
                            if (i >= source.Length) break; c = source[i++];
                            text.Append(c switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '\\' => '\\', '"' => '"', '0' => '\0', _ => throw new ParseFailure(i - 1, "Unsupported string escape. Use standard quoted or verbatim strings.") });
                        }
                        else { if (!verbatim && (c == '\n' || c == '\r')) throw new ParseFailure(i - 1, "A quoted string cannot span lines. Use a verbatim @ string."); text.Append(c); }
                    }
                    if (!closed) throw new ParseFailure(start, "Unterminated string."); tokens.Add(new Token(text.ToString(), true, start));
                }
                else if (char.IsLetter(c) || c == '_') { while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++; tokens.Add(new Token(source.Substring(start, i - start), false, start)); }
                else if (".[](){}=;,+".Contains(c)) tokens.Add(new Token(c.ToString(), false, start));
                else throw new ParseFailure(start, "Unsupported syntax. Safe Preview accepts approved model assignments, AddMeasure/Delete and bounded foreach only.");
                if (tokens.Count > 20000) throw new ParseFailure(start, "Safe scripts are limited to 20,000 tokens.");
            }
            tokens.Add(new Token("<end>", false, source.Length));
        }
        public IReadOnlyList<RecipeStep> Read()
        {
            while (!Is("<end>"))
            {
                if (Take("foreach"))
                {
                    Need("("); Need("var"); var variable = Identifier(); if (variable is "Model" or "Selected") Fail("The loop variable cannot shadow Model or Selected."); Need("in"); var target = Target(null, null); Need(")"); Need("{");
                    if (target.Scope is RecipeScope.Measure or RecipeScope.Column or RecipeScope.Table) Fail("foreach requires an approved object collection.");
                    var count = steps.Count; while (!Take("}")) { if (Is("<end>")) Fail("Unterminated foreach body."); Statement(variable, target); }
                    if (steps.Count == count) Fail("The loop body is empty.");
                }
                else Statement(null, null);
            }
            return steps.ToArray();
        }
        private void Statement(string? variable, RecipeTarget? loop)
        {
            if (variable != null && !Is(variable)) Fail("A foreach body can edit only its current loop object in this subset.");
            var target = Target(variable, loop); Need("."); var member = Identifier();
            if (variable == null && target.Scope is not (RecipeScope.Table or RecipeScope.Column or RecipeScope.Measure)) Fail("Collection edits require foreach over the approved collection.");
            if (member == "AddMeasure")
            {
                Need("("); var name = Value(variable); Need(","); var expression = Value(variable); var folder = Take(",") ? Value(variable) : null; Need(")"); Need(";");
                steps.Add(new RecipeStep(target, RecipeOperation.CreateMeasure, "", name, expression, folder));
            }
            else if (member == "Delete") { Need("("); Need(")"); Need(";"); steps.Add(new RecipeStep(target, RecipeOperation.DeleteMeasure, "", RecipeValue.Literal(""))); }
            else { if (!ActionRecipeRules.Properties.Contains(member)) Fail("The property is not allowed in Safe Preview: " + member); Need("="); var value = Value(variable); Need(";"); steps.Add(new RecipeStep(target, RecipeOperation.SetProperty, member, value)); }
            if (steps.Count > 2000) Fail("Safe scripts are limited to 2,000 statements.");
        }
        private RecipeTarget Target(string? variable, RecipeTarget? loop)
        {
            if (variable != null && Take(variable)) return loop!;
            var root = Identifier(); Need("."); var collection = Identifier();
            if (root == "Selected") return collection switch { "Measures" => new(RecipeScope.SelectedMeasures), "Columns" => new(RecipeScope.SelectedColumns), "Tables" => new(RecipeScope.SelectedTables), _ => throw Error("Only Selected.Measures/Columns/Tables are supported.") };
            if (root != "Model") Fail("Only Model, Selected or the current loop variable is supported.");
            if (collection == "AllMeasures") return new(RecipeScope.AllMeasures); if (collection == "AllColumns") return new(RecipeScope.AllColumns);
            if (collection != "Tables") Fail("Only Model.Tables, Model.AllMeasures and Model.AllColumns are supported.");
            if (!Take("[")) return new(RecipeScope.AllTables); var table = String(); Need("]");
            if (Is(".") && at + 1 < tokens.Count && tokens[at + 1].Text is "Measures" or "Columns")
            { Need("."); var kind = Identifier(); Need("["); var name = String(); Need("]"); return new(kind == "Measures" ? RecipeScope.Measure : RecipeScope.Column, table, name); }
            return new(RecipeScope.Table, null, table);
        }
        private RecipeValue Value(string? variable)
        {
            var parts = new List<RecipeValuePart>();
            do
            {
                if (tokens[at].String) parts.Add(new(RecipeValueKind.Literal, String()));
                else if (Take("true")) parts.Add(new(RecipeValueKind.Literal, "true")); else if (Take("false")) parts.Add(new(RecipeValueKind.Literal, "false"));
                else if (Take("AggregateFunction")) { Need("."); var value = Identifier(); if (value is not ("None" or "Sum" or "Count" or "DistinctCount" or "Min" or "Max" or "Average" or "Default")) Fail("Unsupported aggregate function."); parts.Add(new(RecipeValueKind.Literal, value)); }
                else if (variable != null && Take(variable)) { Need("."); if (Take("Name")) parts.Add(new(RecipeValueKind.ObjectName)); else { Need("Table"); Need("."); Need("Name"); parts.Add(new(RecipeValueKind.TableName)); } }
                else Fail("Values must be string/Boolean literals, an approved enum, or the loop object's Name/Table.Name. Arbitrary expressions and calls are rejected.");
            } while (Take("+"));
            return new(parts);
        }
        private string String() { if (!tokens[at].String) Fail("A literal string is required here."); return tokens[at++].Text; }
        private string Identifier() { var token = tokens[at]; if (token.String || token.Text.Length == 0 || !(char.IsLetter(token.Text[0]) || token.Text[0] == '_')) Fail("An identifier is required here."); at++; return token.Text; }
        private bool Is(string value) => !tokens[at].String && tokens[at].Text == value;
        private bool Take(string value) { if (!Is(value)) return false; at++; return true; }
        private void Need(string value) { if (!Take(value)) Fail("Expected " + value + ". Unsupported syntax is not executed."); }
        private ParseFailure Error(string message) => new(tokens[Math.Min(at, tokens.Count - 1)].Offset, message);
        private void Fail(string message) => throw Error(message);
    }
}
