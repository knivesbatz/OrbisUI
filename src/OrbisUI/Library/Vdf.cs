using System.Text;

namespace OrbisUI.Library;

/// <summary>Valve KeyValues readers: the text format (libraryfolders.vdf, *.acf, loginusers.vdf) and the binary format (shortcuts.vdf).</summary>
public static class Vdf
{
    public sealed class Node : Dictionary<string, object>
    {
        public Node() : base(StringComparer.OrdinalIgnoreCase) { }
        public string? Str(string key) => TryGetValue(key, out var v) ? v as string ?? v?.ToString() : null;
        public Node? Child(string key) => TryGetValue(key, out var v) ? v as Node : null;
    }

    public static Node ParseText(string text)
    {
        int i = 0;
        var root = new Node();
        ParseBody(text, ref i, root);
        return root;
    }

    static void ParseBody(string s, ref int i, Node node)
    {
        while (true)
        {
            var key = Token(s, ref i);
            if (key == null || key == "}") return;
            var val = Token(s, ref i);
            if (val == null) return;
            if (val == "{")
            {
                var child = new Node();
                ParseBody(s, ref i, child);
                node[key] = child;
            }
            else node[key] = val;
        }
    }

    static string? Token(string s, ref int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
            break;
        }
        if (i >= s.Length) return null;
        if (s[i] == '{' || s[i] == '}') return s[i++].ToString();
        var sb = new StringBuilder();
        if (s[i] == '"')
        {
            i++;
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] });
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;
        }
        else
        {
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}') sb.Append(s[i++]);
        }
        return sb.ToString();
    }

    // ---- Binary KeyValues (shortcuts.vdf) ----
    const byte TNode = 0, TString = 1, TInt = 2, TEnd = 8;

    public static Node ParseBinary(byte[] data)
    {
        int i = 0;
        var root = new Node();
        ReadBinary(data, ref i, root);
        return root;
    }

    static void ReadBinary(byte[] d, ref int i, Node node)
    {
        while (i < d.Length)
        {
            byte type = d[i++];
            if (type == TEnd) return;
            string key = CStr(d, ref i);
            switch (type)
            {
                case TNode: var child = new Node(); ReadBinary(d, ref i, child); node[key] = child; break;
                case TString: node[key] = CStr(d, ref i); break;
                case TInt: node[key] = BitConverter.ToUInt32(d, i); i += 4; break;
                default: throw new InvalidDataException($"Unknown binary VDF type {type} at {i - 1}");
            }
        }
    }

    static string CStr(byte[] d, ref int i)
    {
        int start = i;
        while (i < d.Length && d[i] != 0) i++;
        var s = Encoding.UTF8.GetString(d, start, i - start);
        i++;
        return s;
    }

    public static byte[] WriteBinary(Node root)
    {
        var ms = new MemoryStream();
        WriteNode(ms, root);
        ms.WriteByte(TEnd);
        return ms.ToArray();
    }

    static void WriteNode(MemoryStream ms, Node node)
    {
        foreach (var (k, v) in node)
        {
            switch (v)
            {
                case Node n: ms.WriteByte(TNode); WCStr(ms, k); WriteNode(ms, n); ms.WriteByte(TEnd); break;
                case uint u: ms.WriteByte(TInt); WCStr(ms, k); ms.Write(BitConverter.GetBytes(u)); break;
                case int n32: ms.WriteByte(TInt); WCStr(ms, k); ms.Write(BitConverter.GetBytes(n32)); break;
                default: ms.WriteByte(TString); WCStr(ms, k); WCStr(ms, v?.ToString() ?? ""); break;
            }
        }
    }

    static void WCStr(MemoryStream ms, string s) { ms.Write(Encoding.UTF8.GetBytes(s)); ms.WriteByte(0); }
}
