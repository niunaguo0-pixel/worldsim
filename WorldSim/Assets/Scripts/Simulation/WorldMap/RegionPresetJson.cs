namespace WorldSim.Simulation.WorldMap
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// region-presets.json 轻量解析器 (纯 System.*, 无 UnityEngine / 无 BinaryFormatter).
    /// 仅覆盖本契约子集: schemaVersion + presets[{key,name,center,radiusDeg,ethnicSeed,legalFamilyDefault}].
    /// </summary>
    public static class RegionPresetJson
    {
        public static RegionPresetCatalog Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new ArgumentException("json empty");
            var catalog = new RegionPresetCatalog();
            catalog.SchemaVersion = ExtractString(json, "schemaVersion") ?? "";
            int presetsIdx = json.IndexOf("\"presets\"", StringComparison.Ordinal);
            if (presetsIdx < 0) throw new FormatException("missing presets");
            int arrStart = json.IndexOf('[', presetsIdx);
            int arrEnd = FindMatchingBracket(json, arrStart, '[', ']');
            string arrBody = json.Substring(arrStart + 1, arrEnd - arrStart - 1);

            foreach (var obj in SplitTopLevelObjects(arrBody))
            {
                var p = new RegionPreset
                {
                    Key = ExtractString(obj, "key") ?? "",
                    Name = ExtractString(obj, "name") ?? "",
                    RadiusDeg = ExtractNumber(obj, "radiusDeg"),
                    LegalFamilyDefault = ExtractString(obj, "legalFamilyDefault") ?? "",
                };
                // center
                int cIdx = obj.IndexOf("\"center\"", StringComparison.Ordinal);
                if (cIdx >= 0)
                {
                    int brace = obj.IndexOf('{', cIdx);
                    int braceEnd = FindMatchingBracket(obj, brace, '{', '}');
                    string center = obj.Substring(brace, braceEnd - brace + 1);
                    p.CenterLat = ExtractNumber(center, "lat");
                    p.CenterLon = ExtractNumber(center, "lon");
                }
                // ethnicSeed array
                int eIdx = obj.IndexOf("\"ethnicSeed\"", StringComparison.Ordinal);
                if (eIdx >= 0)
                {
                    int a0 = obj.IndexOf('[', eIdx);
                    int a1 = FindMatchingBracket(obj, a0, '[', ']');
                    string eBody = obj.Substring(a0 + 1, a1 - a0 - 1);
                    foreach (var eObj in SplitTopLevelObjects(eBody))
                    {
                        p.EthnicSeed.Add(new EthnicSeedEntry(
                            ExtractString(eObj, "languageFamily") ?? "",
                            ExtractString(eObj, "name") ?? "",
                            ExtractNumber(eObj, "share")));
                    }
                }
                if (!string.IsNullOrEmpty(p.Key))
                    catalog.Presets.Add(p);
            }
            return catalog;
        }

        private static List<string> SplitTopLevelObjects(string body)
        {
            var list = new List<string>();
            int i = 0;
            while (i < body.Length)
            {
                while (i < body.Length && (char.IsWhiteSpace(body[i]) || body[i] == ',')) i++;
                if (i >= body.Length) break;
                if (body[i] != '{') { i++; continue; }
                int end = FindMatchingBracket(body, i, '{', '}');
                list.Add(body.Substring(i, end - i + 1));
                i = end + 1;
            }
            return list;
        }

        private static int FindMatchingBracket(string s, int openIdx, char open, char close)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = openIdx; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' && (i == 0 || s[i - 1] != '\\')) inStr = !inStr;
                if (inStr) continue;
                if (c == open) depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            throw new FormatException("unbalanced brackets");
        }

        private static string ExtractString(string obj, string key)
        {
            string pattern = "\"" + key + "\"";
            int k = obj.IndexOf(pattern, StringComparison.Ordinal);
            if (k < 0) return null;
            int colon = obj.IndexOf(':', k + pattern.Length);
            if (colon < 0) return null;
            int q0 = obj.IndexOf('"', colon + 1);
            if (q0 < 0) return null;
            var sb = new StringBuilder();
            for (int i = q0 + 1; i < obj.Length; i++)
            {
                char c = obj[i];
                if (c == '\\' && i + 1 < obj.Length) { sb.Append(obj[++i]); continue; }
                if (c == '"') return sb.ToString();
                sb.Append(c);
            }
            return null;
        }

        private static double ExtractNumber(string obj, string key)
        {
            string pattern = "\"" + key + "\"";
            int k = obj.IndexOf(pattern, StringComparison.Ordinal);
            if (k < 0) return 0;
            int colon = obj.IndexOf(':', k + pattern.Length);
            if (colon < 0) return 0;
            int i = colon + 1;
            while (i < obj.Length && char.IsWhiteSpace(obj[i])) i++;
            int start = i;
            if (i < obj.Length && (obj[i] == '-' || obj[i] == '+')) i++;
            while (i < obj.Length && (char.IsDigit(obj[i]) || obj[i] == '.' || obj[i] == 'e' || obj[i] == 'E')) i++;
            string num = obj.Substring(start, i - start);
            return double.Parse(num, CultureInfo.InvariantCulture);
        }
    }

    public sealed class RegionPresetCatalog
    {
        public string SchemaVersion;
        public List<RegionPreset> Presets = new List<RegionPreset>();

        public RegionPreset Get(string key)
        {
            for (int i = 0; i < Presets.Count; i++)
                if (string.Equals(Presets[i].Key, key, StringComparison.Ordinal))
                    return Presets[i];
            return null;
        }

        public static readonly string[] RequiredKeys =
        {
            "fertile_crescent", "yellow_yangtze", "nile",
            "mediterranean_europe", "indus_ganges", "mesoamerica"
        };
    }
}
