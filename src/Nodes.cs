using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace JTox
{
    internal sealed class NodeInfo
    {
        public readonly string Host;
        public readonly ushort Port;
        public readonly string Key;

        public NodeInfo(string host, ushort port, string key)
        {
            Host = host;
            Port = port;
            Key = key;
        }
    }

    internal static class Nodes
    {
        public static readonly NodeInfo[] Defaults =
        {
            new NodeInfo("node.tox.biribiri.org", 33445, "F404ABAA1C99A9D37D61AB54898F56793E1DEF8BD46B1038B9D822E8460FAB67"),
            new NodeInfo("85.172.30.117",         33445, "8E7D0B859922EF569298B4D261A8CCB5FEA14FB91ED412A7603A585A25698832"),
            new NodeInfo("194.249.212.109",       33445, "3CEE1F054081E7A011234883BC4FC39F661A55B73637A5AC293DDF1251D9432B"),
            new NodeInfo("185.25.116.107",        33445, "DA4E4ED4B697F2E9B000EEFE3A34B554ACD3F45F5C96EAEA2516DD7FF9AF7B43"),
            new NodeInfo("tox.verdict.gg",        33445, "1C5293AEF2114717547B39DA8EA6F1E331E5E358B35F9B6B5F19317911C5F976"),
            new NodeInfo("tox1.a68366.com",       33445, "AEC204B9A4501412D5F0BB67D9C81B5DB3EE6ADA64122D32A3E9B093D544327D"),
            new NodeInfo("163.172.136.118",       33445, "2C289F9F37C20D09DA83565588BF496FAB3764853FA38141817A72E3F18ACA0B"),
            new NodeInfo("78.46.73.141",          33445, "02807CF4F8BB8FB390CC3794BDF1E8449E9A8392C5D3F2200019DA9F1E812E46"),
            new NodeInfo("tox.initramfs.io",      33445, "3F0A45A268367C1BEA652F258C85F4A66DA76BCAA667A49E770BCC4917AB6A25"),
            new NodeInfo("46.229.52.198",         33445, "813C8F4187833EF0655B10F7752141A352248462A567529A38B6BBF73E979307"),
            new NodeInfo("tox.ngc.zone",          33445, "15E9C309CFCB79FDDF0EBA057DABB49FE15F3803B1BFF06536AE2E5BA5E4690E"),
            new NodeInfo("144.217.167.73",        33445, "7E5668E0EE09E19F320AD47902419331FFEE147BB3606769CFBE921A2A2FD34C"),
            new NodeInfo("tox.abilinski.com",     33445, "10C00EB250C3233E343E2AEBA07115A5C28920E9C8D29492F6D00B29049EDC7E"),
            new NodeInfo("tmux.ru",               33445, "7467AFA626D3246343170B309BA5BDC975DF3924FC9D7A5917FBFA9F5CD5CD38"),
            new NodeInfo("37.48.122.22",          33445, "1B5A8AB25FFFB66620A531C4646B47F0F32B74C547B30AF8BD8266CA50A3AB59"),
            new NodeInfo("tox.novg.net",          33445, "D527E5847F8330D628DAB1814F0A422F6DC9D0A300E6C357634EE2DA88C35463"),
            new NodeInfo("95.31.18.227",          33445, "257744DBF57BE3E117FE05D145B5F806089428D4DCE4E3D0D50616AA16D9417E"),
            new NodeInfo("185.14.30.213",         443,   "2555763C8C460495B14157D234DD56B86300A2395554BCAE4621AC345B8C1B1B"),
            new NodeInfo("198.199.98.108",        33445, "BEF0CFB37AF874BD17B9A8F9FE64C75521DB95A37D33C5BDB00E9CF58659C04F"),
            new NodeInfo("52.53.185.100",         33445, "A04F5FE1D006871588C8EC163676458C1EC75B20B4A147433D271E1E85DAF839"),
            new NodeInfo("tox.kurnevsky.net",     33445, "82EF82BA33445A1F91A7DB27189ECFC0C013E06E3DA71F588ED692BED625EC23"),
            new NodeInfo("tox.yikifish.com",      33445, "8EF12E275BA9CD7D56625D4950F2058B06D5905D0650A1FE76AF18DB986DF760"),
        }; /* https://nodes.tox.chat */

        public static List<NodeInfo> Load(string dataDir, out string source)
        {
            string path = Path.Combine(dataDir, "nodes.json");
            if (File.Exists(path))
            {
                try
                {
                    object root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                        .DeserializeObject(File.ReadAllText(path));

                    IEnumerable items;
                    var dict = root as Dictionary<string, object>;
                    if (dict != null)
                    {
                        object n;
                        items = dict.TryGetValue("nodes", out n) ? n as IEnumerable : null;
                    }
                    else items = root as IEnumerable;

                    var list = new List<NodeInfo>();
                    if (items != null)
                    {
                        foreach (object o in items)
                        {
                            var e = o as Dictionary<string, object>;
                            if (e == null) continue;
                            string host = Str(e, "ipv4") ?? Str(e, "host");
                            string key = Str(e, "public_key") ?? Str(e, "key");
                            object pv;
                            if (host == null || key == null || host.Length == 0 || host == "NONE") continue;
                            if (!e.TryGetValue("port", out pv)) continue;
                            int port = Convert.ToInt32(pv);
                            if (port < 1 || port > 65535 || !IsKey(key)) continue;
                            list.Add(new NodeInfo(host, (ushort)port, key));
                        }
                    }
                    if (list.Count > 0)
                    {
                        source = path;
                        return list;
                    }
                }
                catch (Exception)
                {
                }
            }
            source = "built-in list";
            return new List<NodeInfo>(Defaults);
        }

        private static string Str(Dictionary<string, object> e, string name)
        {
            object v;
            return e.TryGetValue(name, out v) ? v as string : null;
        }

        private static bool IsKey(string s)
        {
            if (s.Length != Native.PublicKeySize * 2) return false;
            foreach (char c in s)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
    }
}
