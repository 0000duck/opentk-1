﻿//
// Copyright (C) 2009 the Open Toolkit (http://www.opentk.com)
// Copyright 2013 Xamarin Inc
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Mono.Options;

namespace CHeaderToXML
{
    class EnumTokenComparer : IEqualityComparer<XNode>
    {
        public bool Equals (XNode a, XNode b)
        {
            var a_attr = ((XElement)a).Attribute("name") ?? ((XElement)a).Attribute("token");
            var b_attr = ((XElement)b).Attribute("name") ?? ((XElement)b).Attribute("token");
            return a_attr.Value == b_attr.Value;
        }

        public int GetHashCode (XNode a)
        {
            XElement e = (XElement)a;
            if (e.Name == "enum" || e.Name == "token")
            {
                return ((XElement)a).Attribute("name").Value.GetHashCode();
            }
            else if (e.Name == "use")
            {
                return ((XElement)a).Attribute("token").Value.GetHashCode();
            }
            else
            {
                throw new InvalidOperationException(String.Format(
                    "Unknown element type: {0}", e));
            }
        }
    }

    enum HeaderType
    {
        Header,
        Spec
    }

    class EntryPoint
    {
        static void Main(string[] args)
        {
            try
            {
                bool showHelp = false;
                string prefix = "gl";
                string version = null;
                string path = null;
                HeaderType type = HeaderType.Header;
                OptionSet opts = new OptionSet
                {
                    { "p=", "The {PREFIX} to remove from parsed functions and constants.  " +
                        "Defaults to \"" + prefix + "\".",
                        v => prefix = v },
                    { "v:", "The {VERSION} of the headers being parsed.",
                        v => version = v },
                    { "t:", "The {TYPE} of the headers being parsed.",
                        v => type = (HeaderType)Enum.Parse(typeof(HeaderType), v, true) },
                    { "o:", "The {PATH} to the output file.",
                        v => path = v },
                    { "?|h|help", "Show this message and exit.",
                        v => showHelp = v != null },
                };
                var headers = opts.Parse(args);
                var app = Path.GetFileName(Environment.GetCommandLineArgs()[0]);
                if (showHelp)
                {
                    Console.WriteLine("usage: {0} -p:PREFIX -v:VERSION -t:TYPE HEADERS", app);
                    Console.WriteLine();
                    Console.WriteLine("Options:");
                    opts.WriteOptionDescriptions(Console.Out);
                    Console.WriteLine();
                    Console.WriteLine("HEADERS are the header files to parse into XML.");
                    return;
                }
                if (prefix == null)
                {
                    Console.WriteLine("{0}: missing required parameter -p.", app);
                    Console.WriteLine("Use '{0} --help' for usage.", app);
                    return;
                }
                Parser parser =
                    type == HeaderType.Header ? new ESCLParser { Prefix = prefix, Version = version } :
                    type == HeaderType.Spec ? new GLParser { Prefix = prefix, Version = version } :
                    (Parser)null;

                var sigs = headers.Select(h => parser.Parse(h)).ToList();

                // Merge any duplicate enum entries (in case an enum is declared
                // in multiple files with different entries in each file).
                var entries = MergeDuplicates(sigs);
                SortTokens(entries);

                var settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.Encoding = System.Text.Encoding.UTF8;

                TextWriter out_stream = null;
                if (path == null)
                {
                    out_stream = Console.Out;
                    Console.OutputEncoding = new System.Text.UTF8Encoding (false);
                }
                else
                {
                    out_stream = new StreamWriter(path, false);
                }

                using (var writer = XmlWriter.Create(out_stream, settings))
                {
                    new XElement("signatures",
                        new XElement("add",
                            entries.Values.OrderBy(s => s.Attribute("name").Value),  // only enums
                            sigs.SelectMany(s => s).Where(s => s.Name.LocalName == "function")    // only functions
                                 .OrderBy(s => s.Attribute("extension").Value)
                                 .ThenBy(s => s.Attribute("name").Value)
                    )).WriteTo(writer);
                    writer.Flush();
                    writer.Close();
                }

                out_stream.Dispose();
            }
            finally
            {
                Console.WriteLine();
                if (Debugger.IsAttached)
                {
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey(true);
                }
            }
        }

        private static void SortTokens(Dictionary<string, XElement> entries)
        {
            foreach (var e in entries)
            {
                if (e.Value.Name.LocalName != "enum")
                    continue;
                var tokens = e.Value.Elements()
                    .OrderBy(t => (string)t.Attribute("name"))
                    .ToList();
                e.Value.RemoveNodes();
                e.Value.Add(tokens);
            }
        }

        private static Dictionary<string, XElement> MergeDuplicates(IEnumerable<IEnumerable<XElement>> sigs)
        {
            var entries = new Dictionary<string, XElement>();
            foreach (var e in sigs.SelectMany(s => s).Where(s => s.Name.LocalName == "enum"))
            {
                var name = (string)e.Attribute("name");
                if (entries.ContainsKey(name) && e.Name.LocalName == "enum")
                {
                    var p = entries[name];
                    var curTokens = p.Nodes().ToList();
                    p.RemoveNodes();
                    p.Add(curTokens.Concat(e.Nodes()).Distinct(new EnumTokenComparer()));
                }
                else
                    entries.Add(name, e);
            }
            return entries;
        }
    }
}
