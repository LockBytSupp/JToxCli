using System;
using System.Collections.Generic;
using System.IO;

namespace JTox
{
    internal static class Program
    {
        // Usage: jtox [data-dir]
        // Run two copies with different data dirs on one machine to chat with yourself
        private static int Main(string[] args)
        {
            string dataDir = args.Length > 0
                ? args[0]
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "jtox-cli");

            Native.Init();
            try
            {
                uint major = Native.tox_version_major();
                uint minor = Native.tox_version_minor();
                if (major == 0 && minor < 2)
                {
                    Console.Error.WriteLine("toxcore " + major + "." + minor + " found; jTox needs 0.2.x or newer.");
                    return 1;
                }
            }
            catch (DllNotFoundException)
            {
                Console.Error.WriteLine("Could not find toxcore (toxcore.dll / libtoxcore.so).");
                Console.Error.WriteLine("Put a 64-bit toxcore.dll next to jtox.exe (see README.md).");
                return 1;
            }
            catch (BadImageFormatException)
            {
                Console.Error.WriteLine("toxcore.dll has the wrong bitness for this process (use a 64-bit dll on 64-bit Windows).");
                return 1;
            }
            catch (EntryPointNotFoundException e)
            {
                Console.Error.WriteLine("The toxcore library is too old or incompatible: " + e.Message);
                return 1;
            }

            if (Console.IsInputRedirected || Console.IsOutputRedirected)
            {
                Console.Error.WriteLine("jtox needs an interactive terminal.");
                return 1;
            }

            string source;
            List<NodeInfo> nodes = Nodes.Load(dataDir, out source);
            var client = new ToxClient(dataDir, nodes);
            client.Tell("Data dir: " + Path.GetFullPath(dataDir) + "  |  nodes: " + nodes.Count + " from " + source);

            new Ui(client).Run();
            return 0;
        }
    }
}
