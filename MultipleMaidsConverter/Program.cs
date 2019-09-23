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
        internal static readonly string sceneDir = Path.Combine(outDir, "MultipleMaidsScene");
        internal static readonly string kankyouDir = Path.Combine(outDir, "MultipleMaidsKankyou");
        internal static readonly byte[] defaultImage = MultipleMaidsConverter.Properties.Resources.defaultImage;
        internal static readonly byte[] pngHeader = { 137, 80, 78, 71, 13, 10, 26, 10 };
        internal static readonly byte[] pngEnd = Encoding.ASCII.GetBytes("IEND");
        internal static readonly byte[] kankyoHeader = Encoding.ASCII.GetBytes("KANKYO");
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
            int sceneIndex = 1;
            int kankyouIndex = 10000;
            string[] sceneData = new string[2];
            bool kankyou = false;

            IniFile MMIni = new IniFile();

            MMIni.CreateSection("config");
            MMIni.CreateSection("scene");
            MMIni.CreateSection("kankyo");

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

                sceneData = ConvertPngToScene(png, out kankyou);

                if (sceneData == null)
                {
                    Log(png, " Unable to convert!");
                }
                else
                {
                    Log(png, " Converted successfully.");
                    if (kankyou)
                    {
                        MMIni["scene"][$"s{kankyouIndex}"].Value = sceneData[Program.sceneIndex];
                        MMIni["scene"][$"ss{kankyouIndex}"].Value = sceneData[screenshotIndex];
                        MMIni["kankyo"][$"kankyo{kankyouIndex - 9999}"].Value = $"kankyo{kankyouIndex - 9999}";
                        kankyouIndex += 1;
                    }
                    else
                    {
                        MMIni["scene"][$"s{sceneIndex}"].Value = sceneData[Program.sceneIndex];
                        MMIni["scene"][$"ss{sceneIndex}"].Value = sceneData[screenshotIndex];
                        sceneIndex += 1;
                    }
                }
            }

            // Round up to the nearest hundred
            int sceneMax = (int)Math.Ceiling((double)(sceneIndex - 1) / 100) * 100;
            int kankyoMax = (int)Math.Ceiling((double)(kankyouIndex - 10000) / 10) * 10;
            if (kankyoMax < 20) kankyoMax = 20;

            MMIni["config"]["scene_max"].Value = sceneMax.ToString();
            MMIni["config"]["kankyo_max"].Value = kankyoMax.ToString();

            MMIni.Save(Path.Combine(outDir, "MultipleMaids.ini"));

            return sceneIndex == 1 ? Mode.Error : Mode.Success;
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
            int j = 0;
            int b;

            while ((b = stream.ReadByte()) != -1)
            {
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

        private static string[] ConvertPngToScene(string png, out bool kankyou)
        {
            string[] sceneData = new string[2];
            kankyou = false;

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

                byte[] kankyoBuffer = new byte[kankyoHeader.Length];

                fileStream.Read(kankyoBuffer, 0, kankyoBuffer.Length);

                if (BytesEqual(kankyoBuffer, kankyoHeader))
                {
                    kankyou = true;
                }
                else
                {
                    fileStream.Position -= kankyoHeader.Length;
                }

                using (MemoryStream sceneStream = LZMA.Decompress(fileStream))
                {
                    sceneData[sceneIndex] = Encoding.Unicode.GetString(sceneStream.ToArray());
                }

                using (MemoryStream screenshotStream = new MemoryStream())
                {
                    fileStream.Position = 0;
                    byte[] buf = new byte[4096];

                    while (length > 0)
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

            if (!Directory.Exists(sceneDir))
                Directory.CreateDirectory(sceneDir);

            if (!Directory.Exists(kankyouDir))
                Directory.CreateDirectory(kankyouDir);

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
            bool kankyou = false;

            Log(index, "Converting");

            if (index == 9999)
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
                if (index >= 10000)
                {
                    kankyou = true;
                    Log(index, " Found kankyou.");
                }
                else
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
            string outPath = Path.Combine(kankyou ? kankyouDir : sceneDir, savePngFilename);

            using (BinaryWriter stream = new BinaryWriter(File.Create(outPath)))
            {
                stream.Write(screenshotBuffer);
                if (kankyou) stream.Write(kankyoHeader);
                stream.Write(sceneBuffer);
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
