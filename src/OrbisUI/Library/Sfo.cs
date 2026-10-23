using System.Text;

namespace OrbisUI.Library;

/// <summary>Reads PARAM.SFO (PS3 / PS4 / Vita / PSP) key-value tables, e.g. TITLE and TITLE_ID.</summary>
public static class Sfo
{
    public static Dictionary<string, string> Read(string path)
    {
        try { return Parse(File.ReadAllBytes(path)); }
        catch { return new(); }
    }

    public static Dictionary<string, string> Parse(byte[] b)
    {
        var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (b.Length < 20 || b[0] != 0 || b[1] != 'P' || b[2] != 'S' || b[3] != 'F') return r;
        int keyTable = BitConverter.ToInt32(b, 8), dataTable = BitConverter.ToInt32(b, 12), count = BitConverter.ToInt32(b, 16);
        for (int n = 0; n < count; n++)
        {
            int e = 20 + n * 16;
            if (e + 16 > b.Length) break;
            int keyOff = BitConverter.ToUInt16(b, e);
            int fmt = BitConverter.ToUInt16(b, e + 2);
            int len = BitConverter.ToInt32(b, e + 4);
            int dataOff = BitConverter.ToInt32(b, e + 12);
            int k = keyTable + keyOff, kEnd = Array.IndexOf(b, (byte)0, k);
            if (k >= b.Length || kEnd < 0) break;
            string key = Encoding.ASCII.GetString(b, k, kEnd - k);
            int d = dataTable + dataOff;
            if (d + len > b.Length) continue;
            r[key] = fmt == 0x0404 ? BitConverter.ToUInt32(b, d).ToString() : Encoding.UTF8.GetString(b, d, len).TrimEnd('\0');
        }
        return r;
    }
}
