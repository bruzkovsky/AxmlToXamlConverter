using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace AxmlToXamlConverter
{
    class Program
    {
        private class Options
        {
            [Option('i', "input", HelpText = "specify the file or folder containing the SVGs to extract", Required = true)]
            public string Input { get; set; }

            [Option('o', "output", HelpText = "specify the folder the SVGs shall be extracted to", Required = true)]
            public string Output { get; set; }

            [Option('n', "namespace", HelpText = "specify the namespace of the page class", Required = true)]
            public string Namespace { get; set; }

            [Option('x', "overwrite", HelpText = "flag to overwrite existing output file", Required = false)]
            public bool Overwrite { get; set; }

            [HelpOption]
            public string GetUsage()
            {
                return HelpText.AutoBuild(this,
                  (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
            }
        }


        static void Main(string[] args)
        {
            var options = new Options();
            if (Parser.Default.ParseArguments(args, options))
            {
                if (!File.Exists(options.Input)/* && !Directory.Exists(options.Input)*/)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"No file or folder exists at input '{options.Input}'");
                    return;
                }

                var exporter = new Exporter();

                try
                {
                    exporter.Export(options.Input, options.Output, options.Namespace, options.Overwrite).Wait();
                }
                catch (InvalidOperationException e)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Export failed with message: {e.Message}");
                }
            }
            else
            {
                Console.WriteLine(options.GetUsage());
            }
        }
    }
}
