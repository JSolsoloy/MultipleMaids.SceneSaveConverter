using System;
using System.Text;
using System.Collections.Generic;
using System.IO;
using ExIni;

namespace MultipleMaidsConverter
{
    class Program
    {
        internal enum Mode : byte { Png, Ini, Error, Success }
        internal static readonly string outDir = Path.Combine(Directory.GetCurrentDirectory(), "MMConverterOutput");
        internal static readonly byte[] defaultImage = MultipleMaidsConverter.Properties.Resources.defaultImage;
        internal static readonly byte[] pngHeader = { 137, 80, 78, 71, 13, 10, 26, 10 };
        internal static readonly byte[] pngEnd = Encoding.ASCII.GetBytes("IEND");

        private static void Main(string[] args)
        {
            Mode mode = GetConversionMode(args);

            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            if (mode == Mode.Png)
            {
                Console.WriteLine("Found pngs to convert");
                mode = ConvertPng(args);
            }
            else if (mode == Mode.Ini)
            {
                Console.WriteLine("Found ini to convert");
                mode = ConvertIni(args[0]);
            }
            else
            {
                Console.WriteLine("Usage: MMSaveSceneConvert (<MM ini file> | <MM Save PNGs>...)");
                Environment.Exit(1);
            }

            if (mode == Mode.Error)
            {
                Console.WriteLine("Conversion failed!");
                Environment.ExitCode = 1;
            }
            else
            {
                Console.WriteLine("Conversion successful");
            }

            Console.Write("Press any key key to exit...");
            Console.ReadLine();
        }

        private static Mode GetConversionMode(string[] args)
        {
            if (args.Length > 0)
            {
                if (Path.GetExtension(args[0]).ToLowerInvariant() == ".ini")
                {
                    return Mode.Ini;
                }
                else if (Array.Exists(args, arg => Path.GetExtension(arg).ToLowerInvariant() == ".png"))
                {
                    return Mode.Png;
                }
            }

            return Mode.Error;
        }

        private static Mode ConvertPng(string[] pngs)
        {
            Mode mode = Mode.Error;

            foreach (string png in pngs)
            {
                if (!File.Exists(png))
                {
                    Console.WriteLine($"{png} does not exist! Skipping");
                    continue;
                }

                if (Path.GetExtension(png).ToLowerInvariant() != ".png")
                {
                    Console.WriteLine($"{png} is not a png file! Skipping");
                    continue;
                }

                ConvertPngToScene(png);
            }

            return mode;
        }

        private static Mode ConvertPngToScene(string png)
        {
            // TODO: this stuff
            return Mode.Error;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static Mode ConvertIni(string ini)
        {
            if (!File.Exists(ini))
            {
                Console.WriteLine($"'{Path.GetFileName(ini)}' does not exist!");
                return Mode.Error;
            }

            IniFile MMIni = IniFile.FromFile(ini);

            if (!MMIni.HasSection("scene"))
            {
                Console.WriteLine($"'scene' section wasn't found! Is '{Path.GetFileName(ini)}' a MultipleMaids config?");
                return Mode.Error;
            }

            IniSection MMScene = MMIni["scene"];

            HashSet<int> saveSceneEntries = new HashSet<int>();

            foreach (IniKey iniKey in MMScene.Keys)
            {
                string key = iniKey.Key;

                if (key[0] == 's')
                {
                    int index = Int32.Parse(key.Substring(1 + (key[1] == 's' ? 1 : 0)));
                    if (!saveSceneEntries.Contains(index))
                    {
                        Log(index, "Converting");
                        saveSceneEntries.Add(index);
                        ConvertSceneToPng(index, MMScene);
                    }
                }
                else
                {
                    Console.WriteLine($"Invalid key found! Is '{Path.GetFileName(ini)}' a MultipleMaids config?");
                    return Mode.Error;
                }
            }

            return Mode.Success;
        }

        private static void ConvertSceneToPng(int index, IniSection MMScene)
        {
            byte[] sceneBuffer;
            byte[] screenshotBuffer = defaultImage;
            string sceneString = MMScene.GetKey($"s{index}")?.RawValue;
            string screenshotString = MMScene.GetKey($"ss{index}")?.RawValue;

            if (index >= 10000)
            {
                Log(index, " Kankyo found! Skipping.");
                return;
            }

            if (String.IsNullOrEmpty(sceneString))
            {
                Log(index, " No scene found! Skipping.");
                return;
            }
            else
            {
                Log(index, " Found scene");
                using (MemoryStream stream = new MemoryStream(Encoding.ASCII.GetBytes(sceneString)))
                {
                    sceneBuffer = LZMA.Compress(stream);
                }
            }

            if (String.IsNullOrEmpty(screenshotString))
            {
                Log(index, " No screenshot found!");
            }
            else
            {
                Log(index, " Found screenshot");
                screenshotBuffer = Convert.FromBase64String(screenshotString);
            }

            String dateString = $"{DateTime.Parse(sceneString.Split(',')[0]):yyyyMMddHHmm}";

            string savePngFilename = $"s{index}_{dateString}.png";
            string outPath = Path.Combine(outDir, savePngFilename);

            using (FileStream stream = File.Create(outPath))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(screenshotBuffer);
                writer.Write(sceneBuffer);

            }

            Log(index, $" Outputted to {outPath}");

            Log(index, " Conversion successful");
        }

        private static void Log(int index, string message)
        {
            Console.WriteLine($"s{index}: {message}");
        }
    }
}
