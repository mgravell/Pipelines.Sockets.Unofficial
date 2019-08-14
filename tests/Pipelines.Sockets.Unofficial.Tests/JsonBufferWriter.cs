using Pipelines.Sockets.Unofficial.Buffers;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Pipelines.Sockets.Unofficial.Tests
{
    public class JsonBufferWriter
    {
        [Fact]
        public void WriteToBuffer()
        {
            var metadata = new List<MetaData>();
            for (var i = 0; i < 25; i++)
            {
                var tags = new Dictionary<string, string>
                {
                    ["host"] = Environment.MachineName.ToLower(),
                    ["tag"] = "value"
                };

                metadata.Add(new MetaData("Metric " + i, "rate", tags, "rate"));
                metadata.Add(new MetaData("Metric " + i, "unit", tags, "unit"));
                metadata.Add(new MetaData("Metric " + i, "desc", tags, "description"));
            }

            var bufferWriter = BufferWriter<byte>.Create(blockSize: 320);
            using (var utfWriter = new Utf8JsonWriter(bufferWriter.Writer))
            {
                JsonSerializer.Serialize(utfWriter, metadata);
            }
        }

        public class MetaData
        {
            /// <summary>
            /// The metric name.
            /// </summary>
            public string Metric { get; }
            /// <summary>
            /// The type of metadata being sent. Should be one of "rate", "desc", or "unit".
            /// </summary>
            public string Name { get; }
            /// <summary>
            /// Tags associated with a metric.
            /// </summary>
            public IReadOnlyDictionary<string, string> Tags { get; }
            /// <summary>
            /// The value of the metadata. For example, if Name = "desc", then Value = "your description text here"
            /// </summary>
            public string Value { get; }

            /// <summary>
            /// Describes a piece of time series metadata which can be sent to Bosun.
            /// </summary>
            /// <param name="metric">The metric name. Make sure to use the fully-prefixed name.</param>
            /// <param name="name">The type of metadata being sent. Should be one of "rate", "desc", or "unit".</param>
            /// <param name="tags">Dictionary of tags keyed by name.</param>
            /// <param name="value">The value of the metadata. For example, if Name = "desc", then Value = "your description text here"</param>
            public MetaData(string metric, string name, IReadOnlyDictionary<string, string> tags, string value)
            {
                Metric = metric;
                Name = name;
                Tags = tags;
                Value = value;
            }
        }
    }
}