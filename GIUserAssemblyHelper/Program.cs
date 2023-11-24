using YYHEggEgg.HexAPI;
using YYHEggEgg.HexAPI.OutputHelper;
using System.Diagnostics;
using System.Text;
using YYHEggEgg.Logger;

namespace YYHEggEgg.GIUserAssemblyHelper;

internal class Program
{
    static void Main(string[] args)
    {
        Log.Initialize(new LoggerConfig(
            max_Output_Char_Count: -1,
            use_Console_Wrapper: false,
#if DEBUG
            global_Minimum_LogLevel: LogLevel.Verbose,
            console_Minimum_LogLevel: LogLevel.Debug,
#else
            global_Minimum_LogLevel: LogLevel.Debug,
            console_Minimum_LogLevel: LogLevel.Information,
#endif
            debug_LogWriter_AutoFlush: true,
            enable_Detailed_Time: false
        ));
        Log.Info("Type the UserAssembly.dll path");
        string? path = Console.ReadLine()?.Trim();
        // Dragging fix
        if (path != null && path.StartsWith('\"') && path.EndsWith('\"')) path = path.Substring(1, path.Length - 2);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Log.Erro($"Please enter a valid path!");
            Log.Erro($"Press Enter to exit.");
            Console.ReadLine();
            return;
        }

        List<string> outputs = new();
        List<List<Int64>> valid_data_chunk_collection = new();
        FileHexObject fileHexObject = new FileHexObject(path, false, 102400, 1);

        foreach (var index in fileHexObject.Find(Encoding.Default.GetBytes("<RSAKeyV")))
        {
            Log.Info($"Found: {HexHelper.GetHexFormat(index)}");
            List<Int64> valid_data_chunks = new();

            string output = "";
            for (Int64 i = index; !output.Contains("</RSA");)
            {
                // Read 8 bytes
                output += Encoding.Default.GetString(fileHexObject.GetRange(i, 8));
                valid_data_chunks.Add(i);
                i += 8;
                // The encoded (or interferent) string starts with H (0x48), and ends with º (0xBA)
                Debug.Assert(fileHexObject[i] == 0x48);
                for (Int64 j = i; j < fileHexObject.Length; j++)
                {
                    if (fileHexObject[j] == 0xBA)
                    {
                        i = j + 1;
                        break;
                    }
                }
            }
            var suffixpolicy = output.Length - output.LastIndexOf('<');
            if (suffixpolicy > 0) output += "</RSAKeyValue>".Substring(suffixpolicy);

            // Cut off invisible chars at the end
            int end = output.LastIndexOf("</RSAKeyValue>") + 14;
            output = output.Substring(0, end);
            Log.Info($"Found key (relative index: {outputs.Count}):");
            Log.Info(output);
            Log.Dbug($"Parsed as HEX: \n{Convert.ToHexString(Encoding.Default.GetBytes(output))}");
            valid_data_chunk_collection.Add(valid_data_chunks);
            outputs.Add(output);
        }

        Log.Info($"Do you want to perform Patch? Type 'y' to enter patch guide:");
        var input = Console.ReadLine();
        if (input?.Trim()?.ToLower() == "y")
        {
            Log.Info($"Previously detected {outputs.Count} keys.");
            int left_keys = outputs.Count;
            for (int i = 0; i < left_keys; i++)
            {
                Log.Info($"Paste the corresponding key (index: {i}) below: or press Ctrl+C to exit.");
                var corresponding = outputs[i];
                Log.Info($"Suggestion: This key is a {(corresponding.Contains("InverseQ") ? "Private" : "Public")} key. Type 'n' to skip so as not to patch it.");

                var newkey = Console.ReadLine()?.Trim() ?? string.Empty;
                if (newkey.ToLower() == "n")
                {
                    Log.Info($"OK, skip key {i}.");
                    continue;
                }
                if (corresponding.Length != newkey.Length)
                {
                    Log.Erro($"The new key should have the size equal to the previous one. (previous: {corresponding.Length} != patch: {newkey.Length})");
                    Log.Erro($"Please enter a correct key again.");
                    i--;
                    continue;
                }
                // Prepare for writing content
                Span<byte> newkeyBytes = new(Encoding.Default.GetBytes(newkey));
                Debug.Assert(newkeyBytes.Length == Encoding.Default.GetByteCount(corresponding));
                Queue<byte[]> writes = new();
                for (int j = 0; j < newkeyBytes.Length; j += 8)
                {
                    var chunk = newkeyBytes.Slice(j, Math.Min(newkey.Length - j, 8));
                    writes.Enqueue(chunk.ToArray());
                }

                // Get the offset and actual replace
                var valid_chunk_idxs = valid_data_chunk_collection[i];
                foreach (var offset in valid_chunk_idxs)
                {
                    var write = writes.Dequeue();
                    fileHexObject.OverWriteRange(offset, write, write.Length);
                }
                Log.Info($"Key Patch OK!");
            }
        }

        Log.Info($"Press Enter to exit.");
        Console.ReadLine();
        return;
    }
}
