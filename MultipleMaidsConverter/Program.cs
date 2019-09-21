using System;
using System.Text;
using System.Collections.Generic;
using System.IO;
using ExIni;

namespace MultipleMaidsConverter
{
    class Program
    {
        internal enum Mode : byte { Png, Ini, Error, Success, Help }
        internal const int sceneIndex = 0;
        internal const int screenshotIndex = 1;
        internal static readonly string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MultipleMaids Converter");
        internal static readonly byte[] defaultImage = MultipleMaidsConverter.Properties.Resources.defaultImage;
        internal static readonly byte[] pngHeader = { 137, 80, 78, 71, 13, 10, 26, 10 };
        internal static readonly byte[] pngEnd = Encoding.ASCII.GetBytes("IEND");
        internal static readonly int[] border = { -1, 0, 0, 0, 0 };

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
                Environment.ExitCode = 1;
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

            return Mode.Help;
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

        private static long FindPngEnd(FileStream stream)
        {
            long bytesRead = 0;
            int j = 0;
            int b;

            while ((b = stream.ReadByte()) != -1)
            {
                bytesRead++;
                while (j >= 0 && b != pngEnd[j])
                {
                    j = border[j];
                }

                if (++j == pngEnd.Length)
                {
                    stream.Position += 4;
                    return stream.Position;
                }
            }
            return -1;
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

                long length = FindPngEnd(fileStream);

                if (length == -1)
                {
                    Log(png, "Not a png file! Skipping.");
                    return null;
                }

                using (MemoryStream sceneStream = LZMA.Decompress(fileStream))
                {
                    sceneData[sceneIndex] = Encoding.Unicode.GetString(sceneStream.ToArray());
                }

                using (MemoryStream screenshotStream = new MemoryStream())
                {
                    fileStream.Position = 0;
                    byte[] buf = new byte[4096];

                    while(length > 0)
                    {
                        int bytesRead = fileStream.Read(buf, 0, (int)Math.Min(length, buf.Length));

                        if (bytesRead == 0) break;

                        length -= bytesRead;

                        screenshotStream.Write(buf, 0, bytesRead);
                    }
                    sceneData[screenshotIndex] = Convert.ToBase64String(screenshotStream.ToArray());
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
                        Mode mode = ConvertSceneToPng(index, MMScene);
                        if (mode == Mode.Error)
                        {
                            Console.WriteLine($"Unexpected values found! Is '{Path.GetFileName(ini)}' a MultipleMaids config?");
                            return Mode.Error;
                        }
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

        private static Mode ConvertSceneToPng(int index, IniSection MMScene)
        {
            byte[] sceneBuffer;
            byte[] screenshotBuffer = defaultImage;
            string sceneString = MMScene.GetKey($"s{index}")?.RawValue;
            string screenshotString = MMScene.GetKey($"ss{index}")?.RawValue;

            Log(index, "Converting");

            if (index >= 10000)
            {
                Log(index, " Kankyo found! Skipping.");
                return Mode.Success;
            }
            else if (index == 9999)
            {
                Log(index, " Quick save found! Skipping.");
                return Mode.Success;
            }

            if (String.IsNullOrEmpty(sceneString))
            {
                Log(index, " No scene found! Skipping.");
                return Mode.Success;
            }
            else
            {
                Log(index, " Found scene.");
                using (MemoryStream stream = new MemoryStream(Encoding.Unicode.GetBytes(sceneString)))
                {
                    sceneBuffer = LZMA.Compress(stream);
                }
            }

            string[] sceneParameters = sceneString.Split(',');
            string lastParameter = sceneParameters[sceneParameters.Length - 1];
            DateTime dateSaved;

            if (lastParameter.LastIndexOf(';') != lastParameter.Length - 1)
            {
                return Mode.Error;
            }

            try
            {
                dateSaved = DateTime.Parse(sceneParameters[0]);
            }
            catch
            {
                return Mode.Error;
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

            string savePngFilename = $"s{index}_{dateSaved.ToString("yyyyMMddHHmm")}.png";
            string outPath = Path.Combine(outDir, savePngFilename);

            using (FileStream stream = File.Create(outPath))
            {
                stream.Write(screenshotBuffer, 0, screenshotBuffer.Length);
                stream.Write(sceneBuffer, 0, sceneBuffer.Length);
            }

            File.SetCreationTime(outPath, dateSaved);
            File.SetLastWriteTime(outPath, dateSaved);

            Log(index, $" Outputted to {outPath}");

            Log(index, " Conversion successful.");

            return Mode.Success;
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
