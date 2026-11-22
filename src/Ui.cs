using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace JTox
{
    internal sealed class Ui
    {
        private const string Help =
            "Commands:\n" +
            "  /add <toxid> [message]   send a friend request\n" +
            "  /accept <n>              accept request [n] (see Requests list)\n" +
            "  /decline <n>             decline request [n]\n" +
            "  /chat <n>                open friend n (or press Tab / Shift+Tab)\n" +
            "  /del <n>                 delete friend n and its history\n" +
            "  /name <new name>         change your display name\n" +
            "  /status ok|afk|dnb [msg] set status (online, away, do not disturb)\n" +
            "  /alias <n> [nickname]    set local alias for friend n (or clear)\n" +
            "  /info <n>                show detailed profile/info for friend n\n" +
            "  /id                      show your Tox ID (give it to friends)\n" +
            "  /help                    this text\n" +
            "  /quit                    exit (also Ctrl+C)\n" +
            "Keys: Tab/Shift+Tab switch chat, Up/Down scroll chat (when input empty),\n" +
            "      PgUp/PgDn/Home/End scroll chat history, Left/Right edit, Esc clears input.\n" +
            "Anything not starting with / is sent to the open friend.";

        private static readonly char[] Space = { ' ' };

        private readonly ToxClient _c;
        private readonly StringBuilder _input = new StringBuilder();
        private int _cursorPos = 0;
        private readonly List<string> _history = new List<string>();
        private int _historyIdx = -1;
        private string _tempInput = "";
        private int _sel = -1;
        private int _scroll;
        private bool _quit;
        private int _lastW, _lastH;
        private DateTime _lastDraw = DateTime.MinValue;

        public Ui(ToxClient client)
        {
            _c = client;
        }

        public void Run()
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.TreatControlCAsInput = true;
            Console.CursorVisible = true;
            Console.Clear();

            _c.Start();
            Say(Help);

            while (!_quit)
            {
                bool changed = false;
                while (Console.KeyAvailable)
                {
                    HandleKey(Console.ReadKey(true));
                    changed = true;
                }

                bool resized = Console.WindowWidth != _lastW || Console.WindowHeight != _lastH;
                bool stale = (DateTime.Now - _lastDraw).TotalMilliseconds > 1000;
                if (changed || resized || stale || _c.Dirty)
                {
                    _c.Dirty = false;
                    if (resized) Console.Clear();
                    Draw();
                }
                else Thread.Sleep(20);
            }

            _c.Stop();
            Console.ResetColor();
            Console.Clear();
        }

        private int GetMaxScroll()
        {
            int rows = Math.Max(8, Console.WindowHeight) - 3;
            int total = 0;
            lock (_c.Lock)
            {
                if (_sel >= 0 && _sel < _c.Friends.Count)
                {
                    string pk = _c.Friends[_sel].Pk;
                    total = _c.GetChatLocked(pk).Count + 1;
                }
                else
                {
                    total = _c.SysLog.Count + 1;
                }
            }
            return Math.Max(0, total - rows);
        }

        private void HandleKey(ConsoleKeyInfo k)
        {
            if ((k.Modifiers & ConsoleModifiers.Control) != 0 && (k.Key == ConsoleKey.C || k.Key == ConsoleKey.Q))
            {
                _quit = true;
                return;
            }

            switch (k.Key)
            {
                case ConsoleKey.Enter:
                    string text = _input.ToString().Trim();
                    _input.Clear();
                    _cursorPos = 0;
                    _historyIdx = _history.Count;
                    if (text.Length > 0)
                    {
                        _history.Add(text);
                        _historyIdx = _history.Count;
                        Submit(text);
                    }
                    return;
                case ConsoleKey.Backspace:
                    if (_cursorPos > 0 && _input.Length > 0)
                    {
                        _input.Remove(_cursorPos - 1, 1);
                        _cursorPos--;
                    }
                    return;
                case ConsoleKey.Delete:
                    if (_cursorPos < _input.Length)
                    {
                        _input.Remove(_cursorPos, 1);
                    }
                    return;
                case ConsoleKey.LeftArrow:
                    if (_cursorPos > 0) _cursorPos--;
                    return;
                case ConsoleKey.RightArrow:
                    if (_cursorPos < _input.Length) _cursorPos++;
                    return;
                case ConsoleKey.Home:
                    if ((k.Modifiers & ConsoleModifiers.Control) != 0 || _input.Length == 0)
                    {
                        _scroll = GetMaxScroll();
                    }
                    else
                    {
                        _cursorPos = 0;
                    }
                    return;
                case ConsoleKey.End:
                    if ((k.Modifiers & ConsoleModifiers.Control) != 0 || _input.Length == 0)
                    {
                        _scroll = 0;
                    }
                    else
                    {
                        _cursorPos = _input.Length;
                    }
                    return;
                case ConsoleKey.UpArrow:
                    if (_input.Length == 0)
                    {
                        int maxS = GetMaxScroll();
                        _scroll = Math.Min(maxS, _scroll + 1);
                    }
                    else if (_history.Count > 0 && _historyIdx > 0)
                    {
                        if (_historyIdx == _history.Count) _tempInput = _input.ToString();
                        _historyIdx--;
                        _input.Clear();
                        _input.Append(_history[_historyIdx]);
                        _cursorPos = _input.Length;
                    }
                    return;
                case ConsoleKey.DownArrow:
                    if (_input.Length == 0)
                    {
                        _scroll = Math.Max(0, _scroll - 1);
                    }
                    else if (_history.Count > 0 && _historyIdx < _history.Count)
                    {
                        _historyIdx++;
                        _input.Clear();
                        if (_historyIdx < _history.Count)
                        {
                            _input.Append(_history[_historyIdx]);
                        }
                        else
                        {
                            _input.Append(_tempInput);
                        }
                        _cursorPos = _input.Length;
                    }
                    return;
                case ConsoleKey.Escape:
                    _input.Clear();
                    _cursorPos = 0;
                    _historyIdx = _history.Count;
                    return;
                case ConsoleKey.Tab:
                    Cycle((k.Modifiers & ConsoleModifiers.Shift) != 0 ? -1 : 1);
                    return;
                case ConsoleKey.PageUp:
                    {
                        int maxS = GetMaxScroll();
                        _scroll = Math.Min(maxS, _scroll + 10);
                    }
                    return;
                case ConsoleKey.PageDown:
                    _scroll = Math.Max(0, _scroll - 10);
                    return;
            }

            if (!char.IsControl(k.KeyChar))
            {
                _input.Insert(_cursorPos, k.KeyChar);
                _cursorPos++;
            }
        }

        private void Cycle(int dir)
        {
            int count;
            lock (_c.Lock) count = _c.Friends.Count;
            int pos = _sel + 1;
            pos = ((pos + dir) % (count + 1) + (count + 1)) % (count + 1);
            Select(pos - 1);
        }

        private void Select(int index)
        {
            string pk = null;
            lock (_c.Lock)
            {
                if (index >= 0 && index < _c.Friends.Count) pk = _c.Friends[index].Pk;
                else index = -1;
            }
            _sel = index;
            _scroll = 0;
            _c.SelectedPk = pk;
            if (pk != null) _c.ClearUnread(pk);
        }

        private void Say(string text)
        {
            Select(-1);
            _c.Tell(text);
        }

        private void Submit(string text)
        {
            if (!text.StartsWith("/", StringComparison.Ordinal))
            {
                string current = CurrentPk();
                if (current == null) _c.Tell("No chat open. Press Tab or use /chat <n>, or /help.");
                else
                {
                    _scroll = 0;
                    _c.SendMessage(current, text);
                }
                return;
            }

            string[] p = text.Split(Space, 2, StringSplitOptions.RemoveEmptyEntries);
            string cmd = p[0].ToLowerInvariant();
            string arg = p.Length > 1 ? p[1].Trim() : "";

            switch (cmd)
            {
                case "/help":
                    Say(Help);
                    break;
                case "/quit":
                case "/exit":
                    _quit = true;
                    break;
                case "/id":
                    string id;
                    lock (_c.Lock) id = _c.Address;
                    Say("Your Tox ID:\n" + id);
                    break;
                case "/name":
                    if (arg.Length == 0) _c.Tell("Usage: /name <new name>");
                    else _c.SetName(arg);
                    break;
                case "/status":
                    _c.SetStatus(arg);
                    break;
                case "/alias":
                    {
                        string[] aa = arg.Split(Space, 2, StringSplitOptions.RemoveEmptyEntries);
                        int ai;
                        if (aa.Length == 0 || !int.TryParse(aa[0], out ai)) { _c.Tell("Usage: /alias <n> [nickname]"); break; }
                        string apk = null;
                        lock (_c.Lock)
                        {
                            if (ai >= 1 && ai <= _c.Friends.Count) apk = _c.Friends[ai - 1].Pk;
                        }
                        if (apk == null) { _c.Tell("No such friend."); break; }
                        string aliasName = aa.Length > 1 ? aa[1].Trim() : "";
                        _c.SetAlias(apk, aliasName);
                        break;
                    }
                case "/info":
                    {
                        int ii;
                        if (!int.TryParse(arg, out ii)) { _c.Tell("Usage: /info <n>"); break; }
                        string infoText = _c.GetFriendInfoText(ii - 1);
                        Say(infoText);
                        break;
                    }
                case "/add":
                    string[] a = arg.Split(Space, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (a.Length == 0) _c.Tell("Usage: /add <toxid> [message]");
                    else _c.AddFriend(a[0], a.Length > 1 ? a[1] : "");
                    break;
                case "/chat":
                    int ci;
                    if (TryFriendIndex(arg, out ci)) Select(ci);
                    break;
                case "/del":
                    int di;
                    if (TryFriendIndex(arg, out di))
                    {
                        string dpk;
                        lock (_c.Lock) dpk = _c.Friends[di].Pk;
                        Select(-1);
                        _c.DeleteFriend(dpk);
                    }
                    break;
                case "/accept":
                case "/decline":
                    int ri;
                    if (!int.TryParse(arg, out ri)) { _c.Tell("Usage: " + cmd + " <n>"); break; }
                    string rpk = null;
                    lock (_c.Lock)
                    {
                        if (ri >= 1 && ri <= _c.Requests.Count) rpk = _c.Requests[ri - 1].Pk;
                    }
                    if (rpk == null) _c.Tell("No such request.");
                    else if (cmd == "/accept") _c.AcceptRequest(rpk);
                    else _c.DeclineRequest(rpk);
                    break;
                default:
                    _c.Tell("Unknown command. Try /help");
                    break;
            }
        }

        private bool TryFriendIndex(string arg, out int index)
        {
            index = -1;
            int n;
            if (!int.TryParse(arg, out n)) { _c.Tell("Give a friend number from the list."); return false; }
            lock (_c.Lock)
            {
                if (n < 1 || n > _c.Friends.Count) { _c.Tell("No such friend."); return false; }
            }
            index = n - 1;
            return true;
        }

        private string CurrentPk()
        {
            lock (_c.Lock)
                return _sel >= 0 && _sel < _c.Friends.Count ? _c.Friends[_sel].Pk : null;
        }


        private sealed class Cell
        {
            public readonly string Text;
            public readonly ConsoleColor Fg;
            public readonly bool Highlight;

            public Cell(string text, ConsoleColor fg, bool highlight = false)
            {
                Text = text;
                Fg = fg;
                Highlight = highlight;
            }
        }

        private void Draw()
        {
            int W = Math.Max(30, Console.WindowWidth);
            int H = Math.Max(8, Console.WindowHeight);
            _lastW = Console.WindowWidth;
            _lastH = Console.WindowHeight;
            _lastDraw = DateTime.Now;

            int leftW = Math.Min(30, W / 3);
            int rightW = W - leftW - 2;
            int rows = H - 3;

            var left = new List<Cell>();
            var chat = new List<KeyValuePair<string, ConsoleColor>>();
            string title, notice;

            lock (_c.Lock)
            {
                if (_sel >= _c.Friends.Count) _sel = -1;

                left.Add(new Cell("FRIENDS", ConsoleColor.Yellow));
                left.Add(new Cell((_sel == -1 ? ">" : " ") + " Home / log", ConsoleColor.Gray, _sel == -1));
                for (int i = 0; i < _c.Friends.Count; i++)
                {
                    FriendInfo f = _c.Friends[i];
                    string unread = f.Unread > 0 ? " (" + f.Unread + ")" : "";
                    string icon;
                    ConsoleColor color;
                    GetStatusVisual(f, out icon, out color);
                    left.Add(new Cell((i == _sel ? ">" : " ") + (i + 1) + " " + icon + " " + f.DisplayName + unread, color, i == _sel));
                }
                if (_c.Requests.Count > 0)
                {
                    left.Add(new Cell("", ConsoleColor.Gray));
                    left.Add(new Cell("REQUESTS (/accept n)", ConsoleColor.Yellow));
                    for (int i = 0; i < _c.Requests.Count; i++)
                        left.Add(new Cell(" [" + (i + 1) + "] " + _c.Requests[i].Pk.Substring(0, 8), ConsoleColor.Magenta));
                }

                string state = !_c.Ready ? "starting..." : _c.Connection == 0 ? "OFFLINE (connecting...)"
                    : _c.Connection == 1 ? "online (TCP)" : "online (UDP)";
                title = " jTox CLI | " + _c.SelfName + " | " + state + " | /help";

                List<Line> lines;
                string heading;
                string friendName = "?";
                if (_sel >= 0)
                {
                    lines = _c.GetChatLocked(_c.Friends[_sel].Pk);
                    heading = _c.Friends[_sel].DisplayName;
                    friendName = heading;
                }
                else
                {
                    lines = _c.SysLog;
                    heading = "Home";
                }
                chat.Add(new KeyValuePair<string, ConsoleColor>("── " + heading + " ──", ConsoleColor.Yellow));
                foreach (Line l in lines)
                {
                    string who = l.Kind == LineKind.Out ? "me" : l.Kind == LineKind.In ? friendName : "*";
                    ConsoleColor col = l.Kind == LineKind.Out ? ConsoleColor.Green
                        : l.Kind == LineKind.In ? ConsoleColor.Cyan : ConsoleColor.Gray;
                    string prefix = "[" + l.Time.ToString("HH:mm") + "] " + who + ": ";
                    foreach (string w in Wrap(prefix + l.Text, rightW))
                        chat.Add(new KeyValuePair<string, ConsoleColor>(w, col));
                }

                bool fresh = (DateTime.Now - _c.NoticeTime).TotalSeconds < 8;
                notice = fresh ? _c.Notice.Replace('\n', ' ') : "";
            }

            int total = chat.Count;
            int maxScroll = Math.Max(0, total - rows);
            _scroll = Math.Min(_scroll, maxScroll);
            int start = Math.Max(0, total - rows - _scroll);

            Console.SetCursorPosition(0, 0);
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(Fit(title, W - 1));
            Console.ResetColor();

            for (int r = 0; r < rows; r++)
            {
                Console.SetCursorPosition(0, 1 + r);
                Cell lc = r < left.Count ? left[r] : new Cell("", ConsoleColor.Gray);
                Console.ForegroundColor = lc.Fg;
                if (lc.Highlight) Console.BackgroundColor = ConsoleColor.DarkGray;
                Console.Write(Fit(lc.Text, leftW));
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("│");

                int idx = start + r;
                if (idx < total)
                {
                    Console.ForegroundColor = chat[idx].Value;
                    Console.Write(Fit(chat[idx].Key, rightW));
                }
                else Console.Write(new string(' ', rightW));
                Console.ResetColor();
            }

            Console.SetCursorPosition(0, H - 2);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(Fit(notice, W - 1));
            Console.ResetColor();

            string input = _input.ToString();
            int maxRoom = W - 3;
            string shown = input;
            int cursorCol = 2 + _cursorPos;
            if (shown.Length > maxRoom)
            {
                int startIdx = Math.Max(0, Math.Min(_cursorPos - maxRoom / 2, input.Length - maxRoom));
                shown = input.Substring(startIdx, Math.Min(maxRoom, input.Length - startIdx));
                cursorCol = 2 + (_cursorPos - startIdx);
            }
            Console.SetCursorPosition(0, H - 1);
            Console.Write(Fit("> " + shown, W - 1));
            Console.SetCursorPosition(Math.Min(W - 2, cursorCol), H - 1);
        }

        private static void GetStatusVisual(FriendInfo f, out string icon, out ConsoleColor color)
        {
            if (!f.Online)
            {
                icon = "○";
                color = ConsoleColor.DarkGray;
            }
            else if (f.UserStatus == 2)
            {
                icon = "⊗";
                color = ConsoleColor.Red;
            }
            else if (f.UserStatus == 1)
            {
                icon = "◍";
                color = ConsoleColor.DarkYellow;
            }
            else
            {
                icon = "●";
                color = ConsoleColor.Green;
            }
        }

        private static string Fit(string s, int width)
        {
            if (width <= 0) return "";
            if (s.Length > width) return s.Substring(0, width - 1) + "…";
            return s.PadRight(width);
        }

        private static IEnumerable<string> Wrap(string text, int width)
        {
            width = Math.Max(10, width);
            foreach (string para in text.Split('\n'))
            {
                string rest = para;
                if (rest.Length == 0) { yield return ""; continue; }
                while (rest.Length > width)
                {
                    int cut = rest.LastIndexOf(' ', width - 1, width);
                    if (cut <= 0) cut = width;
                    yield return rest.Substring(0, cut);
                    rest = rest.Substring(cut).TrimStart(' ');
                }
                yield return rest;
            }
        }
    }
}
