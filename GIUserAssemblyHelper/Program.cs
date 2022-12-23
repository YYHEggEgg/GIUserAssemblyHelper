using YYHEggEgg.HexAPI;
using YYHEggEgg.HexAPI.OutputHelper;
using System.Diagnostics;
using System.Text;

namespace GIUserAssemblyHelper
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Type the UserAssembly.dll path");
            string path = Console.ReadLine();

            FileHexObject fileHexObject = new FileHexObject(path, false, 102400, 1);

            foreach (var index in fileHexObject.Find(Encoding.Default.GetBytes("<RSAKeyV")))
            {
                Console.WriteLine($"Found: {HexHelper.GetHexFormat(index)}");

                string output = "";
                for (Int64 i = index; !output.Contains("</RSAKeyValue>"); )
                {
                    // Read 8 bytes
                    output += Encoding.Default.GetString(fileHexObject.GetRange(i, 8));
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

                // Cut off invisible chars at the end
                int end = output.LastIndexOf("</RSAKeyValue>") + 14;
                output = output.Substring(0, end);
                Console.WriteLine(output);
            }

             Console.ReadLine();
        }
    }
}