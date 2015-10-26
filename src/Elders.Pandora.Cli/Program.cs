﻿using System;
using System.IO;
using System.Linq;
using Elders.Pandora.Box;
using Newtonsoft.Json;

namespace Elders.Pandora.Cli
{
    class Program
    {
        static int Main(string[] args)
        {
            string invokedVerb = string.Empty;
            object invokedVerbInstance = null;

            var options = new Options();

            if (args == null || args.Length == 0)
            {
                Console.WriteLine(options.GetUsage());
                return 0;
            }

            if (!CommandLine.Parser.Default.ParseArguments(
                args,
                options,
                (verb, subOptions) => { invokedVerb = verb; invokedVerbInstance = subOptions; }))
            {

                Console.WriteLine(options.GetUsage());
                return 0;
            }

            if (invokedVerb == "open")
            {
                var openOptions = (OpenOptions)invokedVerbInstance;

                string applicationName = openOptions.Application;
                string cluster = openOptions.Cluster;
                string machine = openOptions.Machine;
                string jarFile = openOptions.Jar ?? openOptions.Application + ".json";
                if (!File.Exists(jarFile)) throw new FileNotFoundException("Jar file is required.", jarFile);

                var jar = JsonConvert.DeserializeObject<Jar>(File.ReadAllText(jarFile));
                var box = Box.Box.Mistranslate(jar);
                if (box.Name != applicationName) throw new InvalidProgramException("Invalid grant");

                var cfg = new Pandora(box).Open(new PandoraOptions(cluster, machine, false));

                if (openOptions.Output == OpenOptions.EnvVarOutput)
                {

                    foreach (var setting in cfg.AsDictionary())
                    {
                        Environment.SetEnvironmentVariable(setting.Key, setting.Value, EnvironmentVariableTarget.Machine);
                    }
                }
                else
                {
                    var computedCfg = JsonConvert.SerializeObject(cfg.AsDictionary());
                    File.WriteAllText(openOptions.Output, computedCfg);
                }

            }

            return 0;
        }
    }
}