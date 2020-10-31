using Microsoft.Extensions.Configuration;
using Sitescope2RemoteWrite.PromPb;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sitescope2RemoteWrite.Processing
{
    public class PathRegexRule
    {
        //public string name;
        public Regex regex;
        public Dictionary<string, string> defaults;
        public PathRegexRule(string regex, string defaults)
        {
            //this.defaults = new Dictionary<string, string>();
            if (!String.IsNullOrEmpty(defaults))
            {
                this.defaults = defaults.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(part => part.Split('='))
                                        .ToDictionary(split => split[0], split => split[1]);
            }
            this.regex = new Regex(regex, RegexOptions.Compiled);
        }
    }

    public class CounterRegexRule
    {
        public Regex MonitorName;
        public Regex CounterName;
        public Regex ValueRegexp;
    }

    public class RegexProcess
    {
        private readonly List<PathRegexRule> PathRegexps;
        private readonly List<CounterRegexRule> CounterRegexps;
        public RegexProcess (IConfiguration config)
        {
            PathRegexps = new List<PathRegexRule>();
            CounterRegexps = new List<CounterRegexRule>();
            foreach (var pathRex in config.GetSection("PathsProcessing").Get<List<Dictionary<string, string>>>())
            {
                pathRex.TryGetValue("regexp", out string regex);
                pathRex.TryGetValue("defaults", out string defaults);
                PathRegexps.Add(new PathRegexRule(regex, defaults));
            }
            foreach (var cntrRex in config.GetSection("CounterProcessing").Get<List<Dictionary<string, string>>>())
            {
                cntrRex.TryGetValue("monitorName", out string monitorName);
                cntrRex.TryGetValue("counterName", out string counterName);
                cntrRex.TryGetValue("valueRegexp", out string valueRegexp);
                CounterRegexps.Add(new CounterRegexRule()
                {
                    MonitorName = !string.IsNullOrEmpty(monitorName) ? new Regex(monitorName, RegexOptions.Compiled) : null,
                    CounterName = !string.IsNullOrEmpty(counterName) ? new Regex(counterName, RegexOptions.Compiled) : null,
                    ValueRegexp = !string.IsNullOrEmpty(valueRegexp) ? new Regex(valueRegexp, RegexOptions.Compiled) : null
                });
            }
        }

        public void AddLabelsFromPath(string path, ref TimeSeries timeSeries)
        {
            foreach (var pathRex in PathRegexps)
            {
                var match = pathRex.regex.Match(path);
                if (match.Success)
                {
                    for (int i = 1; i < match.Groups.Count; i++) //(Group group in match.Groups)
                    {
                        Group group = match.Groups[i];
                        if (!String.IsNullOrEmpty(group.Name))
                        {
                            var val = group.Value;
                            if (String.IsNullOrEmpty(val))
                            {
                                pathRex.defaults.TryGetValue(group.Name, out val); 
                            }
                            timeSeries.AddLabel(group.Name, val);
                        }
                    }
                    break;
                }
            }
        }
    }
}
