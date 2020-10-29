using ProtoBuf;
using System.Collections.Generic;

namespace Sitescope2RemoteWrite.PromPb
{
    [ProtoContract]
    public class WriteRequest
    {
        public WriteRequest()
        {
            timeseries = new List<TimeSeries>();
        }
        [ProtoMember(1)]
        List<TimeSeries> timeseries;
    }

    [ProtoContract]
    public class TimeSeries
    {
        public TimeSeries()
        {
            labels = new List<Label>();
            samples = new List<Sample>();
        }
        [ProtoMember(1)]
        List<Label> labels;
        [ProtoMember(2)]
        List<Sample> samples;
    }

    [ProtoContract]
    public class Sample
    {
        [ProtoMember(1)]
        double value;
        [ProtoMember(2)]
        long timestamp;
    }

    [ProtoContract]
    public class Label
    {
        [ProtoMember(1)]
        string name;
        [ProtoMember(2)]
        string value;
    }
}