using System;
using System.IO;
using System.Runtime.InteropServices;

namespace JTox
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SelfConnectionCb(IntPtr tox, int status, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FriendRequestCb(IntPtr tox, IntPtr publicKey, IntPtr message, UIntPtr length, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FriendMessageCb(IntPtr tox, uint friendNumber, int type, IntPtr message, UIntPtr length, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FriendConnectionCb(IntPtr tox, uint friendNumber, int status, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FriendNameCb(IntPtr tox, uint friendNumber, IntPtr name, UIntPtr length, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FriendStatusCb(IntPtr tox, uint friendNumber, int status, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FriendStatusMessageCb(IntPtr tox, uint friendNumber, IntPtr message, UIntPtr length, IntPtr user);

    internal static class Native
    {
        private const string Lib = "toxcore";

        public const int PublicKeySize = 32;
        public const int AddressSize = 38;
        public const int SavedataTypeToxSave = 1;

        public static UIntPtr UP(int n) { return new UIntPtr((uint)n); }
        public static int ToInt(UIntPtr p) { return (int)p.ToUInt32(); }

        public static void Init()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            string target = Path.Combine(dir, "toxcore.dll");
            if (File.Exists(target)) return;
            foreach (string alt in new[] { "libtoxcore.dll", "tox.dll" })
            {
                string p = Path.Combine(dir, alt);
                if (!File.Exists(p)) continue;
                try { File.Copy(p, target); } catch (IOException) { }
                break;
            }
        }

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint tox_version_major();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint tox_version_minor();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint tox_version_patch();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr tox_options_new(out int error);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_options_free(IntPtr options);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_options_set_savedata_type(IntPtr options, int type);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_options_set_savedata_data(IntPtr options, IntPtr data, UIntPtr length);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr tox_new(IntPtr options, out int error);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_kill(IntPtr tox);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_iterate(IntPtr tox, IntPtr userData);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint tox_iteration_interval(IntPtr tox);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr tox_get_savedata_size(IntPtr tox);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_get_savedata(IntPtr tox, byte[] data);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool tox_bootstrap(IntPtr tox, [MarshalAs(UnmanagedType.LPStr)] string host, ushort port, byte[] publicKey, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool tox_add_tcp_relay(IntPtr tox, [MarshalAs(UnmanagedType.LPStr)] string host, ushort port, byte[] publicKey, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int tox_self_get_connection_status(IntPtr tox);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_self_get_address(IntPtr tox, byte[] address);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool tox_self_set_name(IntPtr tox, byte[] name, UIntPtr length, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr tox_self_get_name_size(IntPtr tox);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_self_get_name(IntPtr tox, byte[] name);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool tox_self_set_status_message(IntPtr tox, byte[] message, UIntPtr length, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_self_set_status(IntPtr tox, int status);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint tox_friend_add(IntPtr tox, byte[] address, byte[] message, UIntPtr length, out int error);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint tox_friend_add_norequest(IntPtr tox, byte[] publicKey, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool tox_friend_delete(IntPtr tox, uint friendNumber, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr tox_self_get_friend_list_size(IntPtr tox);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_self_get_friend_list(IntPtr tox, uint[] list);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool tox_friend_get_public_key(IntPtr tox, uint friendNumber, byte[] publicKey, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr tox_friend_get_name_size(IntPtr tox, uint friendNumber, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool tox_friend_get_name(IntPtr tox, uint friendNumber, byte[] name, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int tox_friend_get_connection_status(IntPtr tox, uint friendNumber, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int tox_friend_get_status(IntPtr tox, uint friendNumber, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern UIntPtr tox_friend_get_status_message_size(IntPtr tox, uint friendNumber, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool tox_friend_get_status_message(IntPtr tox, uint friendNumber, byte[] message, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint tox_friend_send_message(IntPtr tox, uint friendNumber, int type, byte[] message, UIntPtr length, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_callback_self_connection_status(IntPtr tox, SelfConnectionCb callback);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_callback_friend_request(IntPtr tox, FriendRequestCb callback);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_callback_friend_message(IntPtr tox, FriendMessageCb callback);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_callback_friend_connection_status(IntPtr tox, FriendConnectionCb callback);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_callback_friend_name(IntPtr tox, FriendNameCb callback);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_callback_friend_status(IntPtr tox, FriendStatusCb callback);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void tox_callback_friend_status_message(IntPtr tox, FriendStatusMessageCb callback);
    }
}
