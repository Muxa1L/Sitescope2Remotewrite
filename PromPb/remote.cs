using System.Collections.Generic;
using ProtoBuf;

namespace Sitescope2RemoteWrite.PromPb{
    [ProtoContract]
    class WriteRequest {
        [ProtoMember(1)]
        List<TimeSeries> timeseries;
    }

    [ProtoContract]
    class TimeSeries{
        [ProtoMember(1)]
        List<Label> labels;
        [ProtoMember(2)]
        List<Sample> samples;
    }

    [ProtoContract]
    class Sample {
        [ProtoMember(1)]
        double value;
        [ProtoMember(2)]
        long timestamp;
    }

    [ProtoContract]
    class Label{
        [ProtoMember(1)]
        string name;
        [ProtoMember(2)]
        string value;
    }
}