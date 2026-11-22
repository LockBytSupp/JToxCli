using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace JTox
{
    internal enum LineKind { In, Out, Sys }

    internal sealed class Line
    {
        public DateTime Time;
        public LineKind Kind;
        public string Text = "";
    }

    internal sealed class FriendInfo
    {
        public uint Number;
        public string Pk = "";
        public string Name = "";
        public string StatusMessage = "";
        public string Alias = "";
        public bool Online;
        public int UserStatus;
        public int Unread;
        public string DisplayName
        {
            get
            {
                return Alias.Length > 0 ? Alias : Name;
            }
        }
    }

    internal sealed class RequestInfo
    {
        public string Pk = "";
        public string Message = "";
    }

    internal sealed class ToxClient
    {
        public readonly object Lock = new object();
        public volatile bool Dirty = true;

        public List<FriendInfo> Friends = new List<FriendInfo>();
        public List<RequestInfo> Requests = new List<RequestInfo>();
        public List<Line> SysLog = new List<Line>();
        public string Address = "";
        public string SelfName = "";
        public int Connection;
        public bool Ready;

        public string Notice = "";
        public DateTime NoticeTime = DateTime.MinValue;

        public volatile string SelectedPk;

        private readonly Dictionary<string, List<Line>> _chats = new Dictionary<string, List<Line>>();
        private readonly string _saveFile;
        private readonly string _aliasFile;
        private readonly string _histDir;
        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>();
        private readonly List<NodeInfo> _nodes;
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private readonly Random _rng = new Random();

        private IntPtr _tox;
        private Thread _thread;
        private volatile bool _running;

        private SelfConnectionCb _cbSelf;
        private FriendRequestCb _cbReq;
        private FriendMessageCb _cbMsg;
        private FriendConnectionCb _cbConn;
        private FriendNameCb _cbName;
        private FriendStatusCb _cbFriendStatus;
        private FriendStatusMessageCb _cbFriendStatusMsg;

        public ToxClient(string dataDir, List<NodeInfo> nodes)
        {
            Directory.CreateDirectory(dataDir);
            _histDir = Path.Combine(dataDir, "history");
            Directory.CreateDirectory(_histDir);
            _saveFile = Path.Combine(dataDir, "profile.tox");
            _aliasFile = Path.Combine(dataDir, "aliases.txt");
            _nodes = nodes;
            LoadAliases();
        }

        private void LoadAliases()
        {
            try
            {
                if (File.Exists(_aliasFile))
                {
                    foreach (string line in File.ReadAllLines(_aliasFile))
                    {
                        int idx = line.IndexOf('=');
                        if (idx > 0)
                        {
                            string pk = line.Substring(0, idx).Trim();
                            string alias = line.Substring(idx + 1).Trim();
                            if (pk.Length > 0 && alias.Length > 0) _aliases[pk] = alias;
                        }
                    }
                }
            }
            catch (Exception) { }
        }

        private void SaveAliases()
        {
            try
            {
                var lines = new List<string>();
                foreach (var kv in _aliases)
                    lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(_aliasFile, lines);
            }
            catch (Exception) { }
        }

        public void SetAlias(string pk, string alias)
        {
            Submit(() =>
            {
                if (alias.Length == 0)
                {
                    _aliases.Remove(pk);
                    Tell("Alias removed.");
                }
                else
                {
                    _aliases[pk] = alias;
                    Tell("Alias set to: " + alias);
                }
                SaveAliases();
                RefreshFriends();
            });
        }

        public string GetFriendInfoText(int index)
        {
            lock (Lock)
            {
                if (index < 0 || index >= Friends.Count) return "No such friend.";
                FriendInfo f = Friends[index];
                int connStatus;
                Native.tox_friend_get_connection_status(_tox, f.Number, out connStatus);
                string connStr = connStatus == 0 ? "Offline" : connStatus == 1 ? "Online (TCP)" : "Online (UDP)";
                string userStatusStr = f.UserStatus == 1 ? "Away" : f.UserStatus == 2 ? "Busy / DNB" : "Online";
                return "Friend Info [" + (index + 1) + "]:\n" +
                       "  Real Name: " + f.Name + "\n" +
                       "  Alias:     " + (f.Alias.Length > 0 ? f.Alias : "(none)") + "\n" +
                       "  Status:    " + userStatusStr + " - " + (f.StatusMessage.Length > 0 ? f.StatusMessage : "(none)") + "\n" +
                       "  Connection: " + connStr + "\n" +
                       "  Public Key: " + f.Pk;
            }
        }


        public void Start()
        {
            _running = true;
            _thread = new Thread(Loop) { Name = "tox" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            if (_thread != null) _thread.Join(5000);
        }

        public void Submit(Action a)
        {
            _queue.Enqueue(a);
        }

        public void Tell(string text)
        {
            lock (Lock)
            {
                Notice = text;
                NoticeTime = DateTime.Now;
                SysLog.Add(new Line { Time = DateTime.Now, Kind = LineKind.Sys, Text = text });
                if (SysLog.Count > 500) SysLog.RemoveRange(0, SysLog.Count - 500);
            }
            Dirty = true;
        }

        public List<Line> GetChatLocked(string pk)
        {
            List<Line> list;
            if (!_chats.TryGetValue(pk, out list))
            {
                list = LoadHistory(pk);
                _chats[pk] = list;
            }
            return list;
        }

        public void ClearUnread(string pk)
        {
            lock (Lock)
            {
                foreach (FriendInfo f in Friends)
                    if (f.Pk == pk) f.Unread = 0;
            }
            Dirty = true;
        }

        public void SetName(string name)
        {
            Submit(() =>
            {
                byte[] b = Encoding.UTF8.GetBytes(name);
                if (b.Length > 128) { Tell("Name too long (max 128 bytes)"); return; }
                Native.tox_self_set_name(_tox, b, Native.UP(b.Length), out _);
                lock (Lock) SelfName = name;
                Save();
                Tell("Name set to: " + name);
            });
        }

        public void SetStatus(string arg)
        {
            Submit(() =>
            {
                string m = arg.Trim().ToLowerInvariant();
                if (m == "ok" || m == "online")
                {
                    Native.tox_self_set_status(_tox, 0);
                    Save();
                    Tell("Status presence set to: Online");
                }
                else if (m == "afk" || m == "away")
                {
                    Native.tox_self_set_status(_tox, 1);
                    Save();
                    Tell("Status presence set to: Away");
                }
                else if (m == "dnb" || m == "busy")
                {
                    Native.tox_self_set_status(_tox, 2);
                    Save();
                    Tell("Status presence set to: Busy / DNB");
                }
                else if (arg.Length > 0)
                {
                    byte[] b = Encoding.UTF8.GetBytes(arg);
                    if (b.Length > 1024) { Tell("Status message too long"); return; }
                    Native.tox_self_set_status_message(_tox, b, Native.UP(b.Length), out _);
                    Save();
                    Tell("Status message set to: " + arg);
                }
                else
                {
                    Tell("Usage: /status ok | afk | dnb | <status message>");
                }
            });
        }

        public void AddFriend(string toxId, string message)
        {
            Submit(() =>
            {
                byte[] addr;
                try { addr = Hex.Decode(toxId); }
                catch (FormatException) { Tell("Tox ID must be hex (76 or 64 characters)."); return; }

                int err;
                if (addr.Length == Native.AddressSize)
                {
                    byte[] msg = Encoding.UTF8.GetBytes(message.Length == 0 ? "Hi, I'd like to add you on jTox" : message);
                    Native.tox_friend_add(_tox, addr, msg, Native.UP(msg.Length), out err);
                }
                else if (addr.Length == Native.PublicKeySize)
                {
                    Native.tox_friend_add_norequest(_tox, addr, out err);
                }
                else
                {
                    Tell("Tox ID must be 76 characters (Full Tox ID) or 64 characters (Public Key). Got " + toxId.Length + ".");
                    return;
                }

                if (err != 0)
                {
                    Tell("Could not add friend: " + FriendAddError(err));
                    return;
                }
                Save();
                RefreshFriends();
                Tell("Friend added successfully.");
            });
        }

        public void AcceptRequest(string pk)
        {
            Submit(() =>
            {
                byte[] key = Hex.Decode(pk);
                int err;
                Native.tox_friend_add_norequest(_tox, key, out err);
                lock (Lock) Requests.RemoveAll(r => r.Pk == pk);
                if (err != 0) { Tell("Could not accept request (error " + err + ")"); return; }
                Save();
                RefreshFriends();
                Tell("Friend added.");
            });
        }

        public void DeclineRequest(string pk)
        {
            lock (Lock) Requests.RemoveAll(r => r.Pk == pk);
            Dirty = true;
        }

        public void DeleteFriend(string pk)
        {
            Submit(() =>
            {
                uint n = NumberFor(pk);
                if (n == uint.MaxValue) return;
                Native.tox_friend_delete(_tox, n, out _);
                lock (Lock) _chats.Remove(pk);
                try { File.Delete(HistoryPath(pk)); } catch (IOException) { }
                Save();
                RefreshFriends();
                Tell("Friend deleted.");
            });
        }

        public void SendMessage(string pk, string text)
        {
            Submit(() =>
            {
                uint n = NumberFor(pk);
                if (n == uint.MaxValue) { Tell("Unknown friend."); return; }

                foreach (string chunk in Chunk(text, 1200))
                {
                    byte[] b = Encoding.UTF8.GetBytes(chunk);
                    int err;
                    Native.tox_friend_send_message(_tox, n, 0, b, Native.UP(b.Length), out err);
                    if (err != 0)
                    {
                        Tell("Not sent: " + SendError(err));
                        return;
                    }
                    AddLine(pk, LineKind.Out, chunk);
                }
            });
        }


        private void Loop()
        {
            try
            {
                if (!CreateTox()) { _running = false; return; }

                byte[] nameBuf = new byte[Native.ToInt(Native.tox_self_get_name_size(_tox))];
                if (nameBuf.Length > 0) Native.tox_self_get_name(_tox, nameBuf);
                string name = Encoding.UTF8.GetString(nameBuf);
                if (name.Length == 0)
                {
                    name = "jTox user";
                    byte[] nb = Encoding.UTF8.GetBytes(name);
                    Native.tox_self_set_name(_tox, nb, Native.UP(nb.Length), out _);
                    byte[] sm = Encoding.UTF8.GetBytes("Toxing on jTox");
                    Native.tox_self_set_status_message(_tox, sm, Native.UP(sm.Length), out _);
                }

                byte[] addr = new byte[Native.AddressSize];
                Native.tox_self_get_address(_tox, addr);
                lock (Lock)
                {
                    SelfName = name;
                    Address = Hex.Encode(addr);
                    Ready = true;
                }
                Save();
                RefreshFriends();
                Bootstrap(_nodes);
                Tell("Bootstrapping with " + _nodes.Count + " nodes...");

                DateTime lastSave = DateTime.UtcNow;
                DateTime lastBootstrap = DateTime.UtcNow;
                while (_running)
                {
                    Action job;
                    while (_queue.TryDequeue(out job))
                    {
                        try { job(); }
                        catch (Exception e) { Tell("Internal error: " + e.Message); }
                    }

                    Native.tox_iterate(_tox, IntPtr.Zero);

                    DateTime now = DateTime.UtcNow;
                    if (Connection == 0 && (now - lastBootstrap).TotalSeconds > 20)
                    {
                        Bootstrap(_nodes.OrderBy(x => _rng.Next()).Take(6).ToList());
                        lastBootstrap = now;
                    }
                    if ((now - lastSave).TotalSeconds > 30)
                    {
                        Save();
                        lastSave = now;
                    }
                    Thread.Sleep((int)Math.Max(10u, Native.tox_iteration_interval(_tox)));
                }

                Save();
            }
            catch (Exception e)
            {
                Tell("Tox thread crashed: " + e.Message);
            }
            finally
            {
                if (_tox != IntPtr.Zero)
                {
                    Native.tox_kill(_tox);
                    _tox = IntPtr.Zero;
                }
            }
        }

        private bool CreateTox()
        {
            byte[] save = File.Exists(_saveFile) ? File.ReadAllBytes(_saveFile) : null;

            int oerr;
            IntPtr opts = Native.tox_options_new(out oerr);
            if (opts == IntPtr.Zero) { Tell("tox_options_new failed"); return false; }

            IntPtr mem = IntPtr.Zero;
            try
            {
                if (save != null && save.Length > 0)
                {
                    mem = Marshal.AllocHGlobal(save.Length);
                    Marshal.Copy(save, 0, mem, save.Length);
                    Native.tox_options_set_savedata_type(opts, Native.SavedataTypeToxSave);
                    Native.tox_options_set_savedata_data(opts, mem, Native.UP(save.Length));
                }
                int err;
                _tox = Native.tox_new(opts, out err);
                if (_tox == IntPtr.Zero)
                {
                    Tell("tox_new failed (error " + err + "). A corrupt profile.tox can cause this.");
                    return false;
                }
            }
            finally
            {
                Native.tox_options_free(opts);
                if (mem != IntPtr.Zero) Marshal.FreeHGlobal(mem);
            }

            _cbSelf = OnSelfConnection;
            _cbReq = OnFriendRequest;
            _cbMsg = OnFriendMessage;
            _cbConn = OnFriendConnection;
            _cbName = OnFriendName;
            _cbFriendStatus = OnFriendStatus;
            _cbFriendStatusMsg = OnFriendStatusMessage;
            Native.tox_callback_self_connection_status(_tox, _cbSelf);
            Native.tox_callback_friend_request(_tox, _cbReq);
            Native.tox_callback_friend_message(_tox, _cbMsg);
            Native.tox_callback_friend_connection_status(_tox, _cbConn);
            Native.tox_callback_friend_name(_tox, _cbName);
            Native.tox_callback_friend_status(_tox, _cbFriendStatus);
            Native.tox_callback_friend_status_message(_tox, _cbFriendStatusMsg);
            return true;
        }

        private void Bootstrap(IEnumerable<NodeInfo> nodes)
        {
            foreach (NodeInfo n in nodes)
            {
                byte[] key;
                try { key = Hex.Decode(n.Key); }
                catch (FormatException) { continue; }
                if (key.Length != Native.PublicKeySize) continue;
                int err;
                Native.tox_bootstrap(_tox, n.Host, n.Port, key, out err);
                Native.tox_add_tcp_relay(_tox, n.Host, n.Port, key, out err);
            }
        }


        private void OnSelfConnection(IntPtr tox, int status, IntPtr user)
        {
            lock (Lock) Connection = status;
            Tell(status == 0 ? "Disconnected from the Tox network."
                : status == 1 ? "Connected to the Tox network (TCP)."
                : "Connected to the Tox network (UDP).");
        }

        private void OnFriendRequest(IntPtr tox, IntPtr pk, IntPtr msg, UIntPtr len, IntPtr user)
        {
            byte[] key = new byte[Native.PublicKeySize];
            Marshal.Copy(pk, key, 0, key.Length);
            string pkHex = Hex.Encode(key);
            string text = ReadUtf8(msg, len);
            lock (Lock)
            {
                Requests.RemoveAll(r => r.Pk == pkHex);
                Requests.Add(new RequestInfo { Pk = pkHex, Message = text });
            }
            Tell("Friend request from " + pkHex.Substring(0, 8) + ": \"" + text + "\"  (see Requests, /accept N)");
        }

        private void OnFriendMessage(IntPtr tox, uint fn, int type, IntPtr msg, UIntPtr len, IntPtr user)
        {
            string pk = PkFor(fn);
            if (pk == null) return;
            string text = ReadUtf8(msg, len);
            if (type == 1) text = "* " + text;
            AddLine(pk, LineKind.In, text);
            if (SelectedPk != pk)
            {
                lock (Lock)
                {
                    foreach (FriendInfo f in Friends)
                        if (f.Pk == pk) f.Unread++;
                }
            }
            Dirty = true;
        }

        private void OnFriendConnection(IntPtr tox, uint fn, int status, IntPtr user)
        {
            RefreshFriends();
        }

        private void OnFriendName(IntPtr tox, uint fn, IntPtr name, UIntPtr len, IntPtr user)
        {
            RefreshFriends();
            Save();
        }

        private void OnFriendStatus(IntPtr tox, uint fn, int status, IntPtr user)
        {
            string pk = PkFor(fn);
            lock (Lock)
            {
                FriendInfo f = Friends.Find(x => x.Number == fn || (pk != null && x.Pk == pk));
                if (f != null)
                {
                    f.UserStatus = (status >= 0 && status <= 2) ? status : 0;
                }
            }
            Dirty = true;
        }

        private void OnFriendStatusMessage(IntPtr tox, uint fn, IntPtr msg, UIntPtr len, IntPtr user)
        {
            RefreshFriends();
        }


        private uint[] FriendNumbers()
        {
            int count = Native.ToInt(Native.tox_self_get_friend_list_size(_tox));
            uint[] numbers = new uint[count];
            if (count > 0) Native.tox_self_get_friend_list(_tox, numbers);
            return numbers;
        }

        private void RefreshFriends()
        {
            var fresh = new List<FriendInfo>();
            foreach (uint n in FriendNumbers())
            {
                string pk = PkFor(n);
                if (pk == null) continue;

                int err;
                int nameLen = Native.ToInt(Native.tox_friend_get_name_size(_tox, n, out err));
                string name = "";
                if (nameLen > 0)
                {
                    byte[] nb = new byte[nameLen];
                    if (Native.tox_friend_get_name(_tox, n, nb, out err)) name = Encoding.UTF8.GetString(nb);
                }
                if (name.Length == 0) name = pk.Substring(0, 8);

                int statusLen = Native.ToInt(Native.tox_friend_get_status_message_size(_tox, n, out err));
                string statusMsg = "";
                if (statusLen > 0)
                {
                    byte[] sb = new byte[statusLen];
                    if (Native.tox_friend_get_status_message(_tox, n, sb, out err)) statusMsg = Encoding.UTF8.GetString(sb);
                }

                string alias = "";
                _aliases.TryGetValue(pk, out alias);
                if (alias == null) alias = "";

                bool online = Native.tox_friend_get_connection_status(_tox, n, out err) != 0;
                int userStatus = 0;
                FriendInfo old = null;
                lock (Lock) { old = Friends.Find(o => o.Pk == pk); }
                if (old != null) userStatus = old.UserStatus;

                if (online)
                {
                    int stErr;
                    int st = Native.tox_friend_get_status(_tox, n, out stErr);
                    if (stErr == 0 && st >= 0 && st <= 2)
                    {
                        if (st != 0 || old == null) userStatus = st;
                    }
                }
                fresh.Add(new FriendInfo { Number = n, Pk = pk, Name = name, StatusMessage = statusMsg, Alias = alias, Online = online, UserStatus = userStatus });
            }
            fresh.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

            lock (Lock)
            {
                foreach (FriendInfo f in fresh)
                {
                    FriendInfo old = Friends.Find(o => o.Pk == f.Pk);
                    if (old != null)
                    {
                        f.Unread = old.Unread;
                        if (f.UserStatus == 0 && old.UserStatus != 0) f.UserStatus = old.UserStatus;
                    }
                }
                Friends = fresh;
            }
            Dirty = true;
        }

        private string PkFor(uint friendNumber)
        {
            byte[] pk = new byte[Native.PublicKeySize];
            int err;
            return Native.tox_friend_get_public_key(_tox, friendNumber, pk, out err) ? Hex.Encode(pk) : null;
        }

        private uint NumberFor(string pk)
        {
            foreach (uint n in FriendNumbers())
                if (PkFor(n) == pk) return n;
            return uint.MaxValue;
        }

        private void Save()
        {
            if (_tox == IntPtr.Zero) return;
            byte[] data = new byte[Native.ToInt(Native.tox_get_savedata_size(_tox))];
            Native.tox_get_savedata(_tox, data);
            string tmp = _saveFile + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, data);
                if (File.Exists(_saveFile)) File.Replace(tmp, _saveFile, null);
                else File.Move(tmp, _saveFile);
            }
            catch (IOException e)
            {
                Tell("Could not save profile: " + e.Message);
            }
        }

        private static string ReadUtf8(IntPtr p, UIntPtr len)
        {
            byte[] b = new byte[Native.ToInt(len)];
            if (b.Length > 0) Marshal.Copy(p, b, 0, b.Length);
            return Encoding.UTF8.GetString(b);
        }

        private static IEnumerable<string> Chunk(string text, int maxBytes)
        {
            var sb = new StringBuilder();
            int bytes = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int len = (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) ? 2 : 1;
                string unit = text.Substring(i, len);
                int n = Encoding.UTF8.GetByteCount(unit);
                if (bytes + n > maxBytes)
                {
                    yield return sb.ToString();
                    sb.Clear();
                    bytes = 0;
                }
                sb.Append(unit);
                bytes += n;
                i += len - 1;
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        private static string FriendAddError(int e)
        {
            switch (e)
            {
                case 2: return "message too long";
                case 3: return "no message";
                case 4: return "that's your own Tox ID";
                case 5: return "request already sent";
                case 6: return "bad checksum - the Tox ID was mistyped or truncated";
                case 7: return "friend already added but with a different nospam value";
                default: return "error " + e;
            }
        }

        private static string SendError(int e)
        {
            switch (e)
            {
                case 2: return "friend not found";
                case 3: return "friend is offline (Tox has no offline delivery)";
                case 4: return "send queue full, try again";
                case 5: return "message too long";
                case 6: return "empty message";
                default: return "error " + e;
            }
        }


        private string HistoryPath(string pk)
        {
            return Path.Combine(_histDir, pk + ".log");
        }

        private void AddLine(string pk, LineKind kind, string text)
        {
            var line = new Line { Time = DateTime.Now, Kind = kind, Text = text };
            lock (Lock) GetChatLocked(pk).Add(line);
            try
            {
                long ms = new DateTimeOffset(line.Time).ToUnixTimeMilliseconds();
                File.AppendAllText(HistoryPath(pk),
                    ms + "\t" + (kind == LineKind.Out ? "out" : "in") + "\t" + Escape(text) + "\n");
            }
            catch (IOException) { }
            Dirty = true;
        }

        private List<Line> LoadHistory(string pk)
        {
            var list = new List<Line>();
            string path = HistoryPath(pk);
            if (!File.Exists(path)) return list;
            try
            {
                string[] all = File.ReadAllLines(path);
                for (int i = Math.Max(0, all.Length - 500); i < all.Length; i++)
                {
                    string[] parts = all[i].Split(new[] { '\t' }, 3);
                    long ms;
                    if (parts.Length != 3 || !long.TryParse(parts[0], out ms)) continue;
                    list.Add(new Line
                    {
                        Time = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime,
                        Kind = parts[1] == "out" ? LineKind.Out : LineKind.In,
                        Text = Unescape(parts[2]),
                    });
                }
            }
            catch (IOException) { }
            return list;
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "");
        }

        private static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    sb.Append(s[i] == 'n' ? '\n' : s[i] == 't' ? '\t' : s[i]);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
