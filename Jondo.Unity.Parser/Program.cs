using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Jondo.Unity.Parser
{
    class Program
    {
        static void Main(string[] args)
        {
            string dumpPath = @"C:\Jondo\Jondo Unity Emulator\Dofus3 Defuscated Data\dump.cs";
            string outDir = @"C:\Jondo\Jondo Unity Emulator\Jondo.Unity.Protocol\Messages";

            Directory.CreateDirectory(outDir);

            if (!File.Exists(dumpPath))
            {
                Console.WriteLine("dump.cs not found.");
                return;
            }

            Console.WriteLine("Reading dump.cs...");
            string[] lines = File.ReadAllLines(dumpPath);

            bool inTargetNamespace = false;
            string currentNamespace = "";
            string currentClass = "";
            List<string> classBuffer = new List<string>();

            int count = 0;

            foreach (string line in lines)
            {
                if (line.StartsWith("// Namespace:"))
                {
                    currentNamespace = line.Replace("// Namespace:", "").Trim();
                    if (currentNamespace.Contains("Ankama.Dofus.Protocol"))
                    {
                        inTargetNamespace = true;
                    }
                    else
                    {
                        inTargetNamespace = false;
                    }
                    continue;
                }

                if (inTargetNamespace)
                {
                    if (line.StartsWith("public class ") || line.StartsWith("public sealed class "))
                    {
                        var match = Regex.Match(line, @"class\s+(\w+)");
                        if (match.Success)
                        {
                            currentClass = match.Groups[1].Value;
                        }
                        classBuffer.Add($"namespace {currentNamespace}");
                        classBuffer.Add("{");
                        classBuffer.Add(line);
                    }
                    else if (!string.IsNullOrEmpty(currentClass))
                    {
                        classBuffer.Add(line);
                        if (line.StartsWith("}"))
                        {
                            if (classBuffer.Count > 5)
                            {
                                File.WriteAllLines(Path.Combine(outDir, currentClass + ".cs"), classBuffer);
                                count++;
                            }
                            currentClass = "";
                            classBuffer.Clear();
                        }
                    }
                }
            }

            Console.WriteLine($"Extraction complete. Extracted {count} classes to {outDir}");
        }
    }
}
