using Microsoft.Extensions.Configuration;
using Sitescope2RemoteWrite.Models;
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
        public Regex Monitor;
        public Regex Counter;
        public Regex Value;
    }

    public class RegexProcess
    {
        private readonly List<PathRegexRule> PathRegexps;
        private readonly List<CounterRegexRule> CounterRegexps;
        private readonly Regex DoubleRegexp = new Regex("[\\d.]+", RegexOptions.Compiled);
        public RegexProcess (IConfiguration config)
        {
            PathRegexps = new List<PathRegexRule>();
            CounterRegexps = new List<CounterRegexRule>();
            foreach (var pathRex in config.GetSection("Paths").Get<List<Dictionary<string, string>>>())
            {
                pathRex.TryGetValue("regexp",   out string regex);
                pathRex.TryGetValue("defaults", out string defaults);
                PathRegexps.Add(new PathRegexRule(regex, defaults));
            }
            foreach (var cntrRex in config.GetSection("Counter").Get<List<Dictionary<string, string>>>())
            {
                cntrRex.TryGetValue("monitor", out string _monitor);
                cntrRex.TryGetValue("counter", out string _counter);
                cntrRex.TryGetValue("value",  out string _value);
                CounterRegexps.Add(new CounterRegexRule()
                {
                    Monitor = !string.IsNullOrEmpty(_monitor) ? new Regex(_monitor, RegexOptions.Compiled) : null,
                    Counter = !string.IsNullOrEmpty(_counter) ? new Regex(_counter, RegexOptions.Compiled) : null,
                    Value =   !string.IsNullOrEmpty(_value)   ? new Regex(_value,   RegexOptions.Compiled) : null
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

        public List<TimeSeries> ProcessCounters(TimeSeries baseTS, Monitor monitor)
        {
            var result = new List<TimeSeries>();
            var matchedCounters = new List<string>();
            foreach (var cntrRule in CounterRegexps)
            {
                bool monitorName = false;
                bool counterName = false;
                if (cntrRule.Monitor != null)
                {
                    if (cntrRule.Monitor.Match(monitor.name).Success)
                        monitorName = true;
                    else
                        continue;
                }
                if (cntrRule.Counter != null)
                {
                    foreach (var counter in monitor.Counters)
                    {
                        var cntrMatch = cntrRule.Counter.Match(counter.name);
                        if (cntrMatch.Success)
                        {
                            matchedCounters.Add(counter.name);
                            if (cntrRule.Value != null)
                            {
                                var valueMatches = cntrRule.Value.Matches(counter.value);
                                foreach(Match valueMatch in valueMatches)
                                {
                                    string name = counter.name;
                                    double value = double.NaN;
                                    if (valueMatch.Groups.Count > 1)
                                    {
                                        for (int i = 1; i < valueMatch.Groups.Count; i++)
                                        {
                                            var group = valueMatch.Groups[i];
                                            if (group.Name.Contains("__name__"))
                                            {
                                                name = group.Name.Replace("__name__", group.Value);
                                            }
                                            else if (group.Name == "value")
                                            {
                                                double.TryParse(group.Value, out value);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        //name = counter.name;
                                        double.TryParse(counter.value, out value);
                                    }
                                    if (!double.IsNaN(value))
                                    {
                                        TimeSeries timeSerie = (TimeSeries)baseTS.Clone();
                                        timeSerie.AddLabel("__name__", name);
                                        timeSerie.AddSample(monitor.timestamp, value);
                                        result.Add(timeSerie);
                                    }
                                    
                                }
                            }
                        }
                    }
                    
                }
            }
            foreach (var counter in monitor.Counters)
            {
                if (!matchedCounters.Contains(counter.name)){
                    var match = DoubleRegexp.Match(counter.value);
                    if (match.Success)
                    {
                        TimeSeries timeSerie = (TimeSeries)baseTS.Clone();
                        timeSerie.AddLabel("__name__", counter.name);
                        timeSerie.AddSample(monitor.timestamp, double.Parse(match.Value));
                        result.Add(timeSerie);
                    }
                    
                }
                
            }
            return result;
        }
    }
}
