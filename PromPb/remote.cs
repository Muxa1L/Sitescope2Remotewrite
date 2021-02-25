using ProtoBuf;
using System;
using System.Collections.Generic;

namespace Sitescope2RemoteWrite.PromPb
{
    [ProtoContract]
    public class WriteRequest
    {
        [ProtoMember(1)]
        List<TimeSeries> timeseries;

        public WriteRequest()
        {
            timeseries = new List<TimeSeries>();
        }

        public void AddTimeSerie(TimeSeries timeserie)
        {
            this.timeseries.Add(timeserie);
        }
    }

    [ProtoContract]
    public class TimeSeries : ICloneable
    {
        [ProtoMember(1)]
        List<Label> labels;
        [ProtoMember(2)]
        List<Sample> samples;

        public TimeSeries()
        {
            labels = new List<Label>();
            samples = new List<Sample>();
        }

        public void AddLabel(string name, string value)
        {
            labels.Add(new Label(name, value));
        }

        public void SetLabels(List<Label> labels)
        {
            this.labels = labels;
        }

        public void AddSample(long timestamp, double value)
        {
            samples.Add(new Sample(timestamp, value));
        }

        public object Clone()
        {
            var result = new TimeSeries();
            foreach (var label in this.labels)
                result.labels.Add((Label)label.Clone());
            return result;
        }
    }

    [ProtoContract]
    public class Sample
    {
        [ProtoMember(1)]
        double value;
        [ProtoMember(2)]
        long timestamp;

        public Sample(long _timestamp, double _value)
        {
            this.timestamp = _timestamp;
            this.value = _value;
        }
    }

    [ProtoContract]
    public class Label : ICloneable
    {
        [ProtoMember(1)]
        string name;
        [ProtoMember(2)]
        string value;

        public Label(string _name, string _value)
        {
            this.name = _name;
            this.value = _value;
        }

        public object Clone()
        {
            return new Label(this.name, this.value);
        }
    }
}