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
        internal const int sceneIndex = 0;
        internal const int screenshotIndex = 1;
        internal static readonly string outDir = Path.Combine(Directory.GetCurrentDirectory(), "MultipleMaids Converter");
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
            else if (mode == Mode.Success)
            {
                Console.WriteLine("Conversion successful");
            }
            else
            {
                Console.WriteLine("What?");
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
            int index = 1;
            string[] sceneData = new string[2];

            IniFile MMIni = new IniFile();

            MMIni.CreateSection("config");

            foreach (string png in pngs)
            {
                if (!File.Exists(png))
                {
                    Log(png, "Does not exist! Skipping.");
                    continue;
                }

                if (Path.GetExtension(png).ToLowerInvariant() != ".png")
                {
                    Log(png, "Not a png file! Skipping.");
                    continue;
                }

                Log(png, "Converting");

                sceneData = ConvertPngToScene(png);

                if (sceneData == null)
                {
                    Log(png, " Unable to convert!");
                }
                else
                {
                    Log(png, " Converted successfully.");
                    MMIni["scene"][$"s{index}"].Value = sceneData[sceneIndex];
                    MMIni["scene"][$"ss{index}"].Value = sceneData[screenshotIndex];
                    index += 1;
                }
            }

            // Round up to the nearest hundred
            int sceneMax = (int)Math.Ceiling((double)(index - 1) / 100) * 100;

            MMIni["config"]["scene_max"].Value = sceneMax.ToString();

            MMIni.Save(Path.Combine(outDir, "MultipleMaids.ini"));

            return index == 1 ? Mode.Error : Mode.Success;
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

        private static string[] ConvertPngToScene(string png)
        {

            string[] sceneData = new string[2];

            using (FileStream fileStream = File.OpenRead(png))
            {
                byte[] headerBuffer = new byte[pngHeader.Length];

                fileStream.Read(headerBuffer, 0, pngHeader.Length);

                if (!BytesEqual(headerBuffer, pngHeader))
                {
                    Log(png, "Not a png file! Skipping.");
                    return null;
                }

                using (MemoryStream screenshotStream = new MemoryStream())
                {
                    fileStream.Position = 0;
                    byte[] buffer = new byte[pngEnd.Length];
                    long position = 0;

                    while (true)
                    {
                        int bytesRead = fileStream.Read(buffer, 0, buffer.Length);

                        if (bytesRead != pngEnd.Length)
                        {
                            return null;
                        }

                        if (BytesEqual(buffer, pngEnd))
                        {
                            // Write IEND bytes
                            screenshotStream.Write(buffer, 0, 4);
                            // Read and then write CRC
                            fileStream.Read(buffer, 0, 4);
                            screenshotStream.Write(buffer, 0, 4);
                            break;
                        }
                        else
                        {
                            screenshotStream.Write(buffer, 0, 1);
                        }
                        fileStream.Position = ++position;
                    }
                    sceneData[screenshotIndex] = Convert.ToBase64String(screenshotStream.ToArray());
                }

                using (MemoryStream sceneStream = LZMA.Decompress(fileStream))
                {
                    sceneData[sceneIndex] = Encoding.UTF8.GetString(sceneStream.ToArray());
                }
            }
            return sceneData;
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
            else if (index == 9999)
            {
                Log(index, " Quick save found! Skipping.");
                return;
            }

            if (String.IsNullOrEmpty(sceneString))
            {
                Log(index, " No scene found! Skipping.");
                return;
            }
            else
            {
                Log(index, " Found scene.");
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
                Log(index, " Found screenshot.");
                screenshotBuffer = Convert.FromBase64String(screenshotString);
            }

            Log(index, "Converting");

            String dateString = $"{DateTime.Parse(sceneString.Split(',')[0]):yyyyMMddHHmm}";

            string savePngFilename = $"s{index}_{dateString}.png";
            string outPath = Path.Combine(outDir, savePngFilename);

            using (FileStream stream = File.Create(outPath))
            {
                stream.Write(screenshotBuffer, 0, screenshotBuffer.Length);
                stream.Write(sceneBuffer, 0, sceneBuffer.Length);
            }

            Log(index, $" Outputted to {outPath}");

            Log(index, " Conversion successful.");
        }

        private static void Log(int index, string message)
        {
            Console.WriteLine($"s{index}: {message}");
        }

        private static void Log(string file, string message)
        {
            Console.WriteLine($"{Path.GetFileName(file)}: {message}");
        }
    }
}
