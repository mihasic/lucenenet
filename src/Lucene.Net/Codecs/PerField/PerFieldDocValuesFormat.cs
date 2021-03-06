using Lucene.Net.Support;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace Lucene.Net.Codecs.PerField
{
    /*
     * Licensed to the Apache Software Foundation (ASF) under one or more
     * contributor license agreements.  See the NOTICE file distributed with
     * this work for additional information regarding copyright ownership.
     * The ASF licenses this file to You under the Apache License, Version 2.0
     * (the "License"); you may not use this file except in compliance with
     * the License.  You may obtain a copy of the License at
     *
     *     http://www.apache.org/licenses/LICENSE-2.0
     *
     * Unless required by applicable law or agreed to in writing, software
     * distributed under the License is distributed on an "AS IS" BASIS,
     * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
     * See the License for the specific language governing permissions and
     * limitations under the License.
     */

    using BinaryDocValues = Lucene.Net.Index.BinaryDocValues;
    using IBits = Lucene.Net.Util.IBits;
    using BytesRef = Lucene.Net.Util.BytesRef;
    using FieldInfo = Lucene.Net.Index.FieldInfo;
    using IOUtils = Lucene.Net.Util.IOUtils;
    using NumericDocValues = Lucene.Net.Index.NumericDocValues;
    using RamUsageEstimator = Lucene.Net.Util.RamUsageEstimator;
    using SegmentReadState = Lucene.Net.Index.SegmentReadState;
    using SegmentWriteState = Lucene.Net.Index.SegmentWriteState;
    using SortedDocValues = Lucene.Net.Index.SortedDocValues;
    using SortedSetDocValues = Lucene.Net.Index.SortedSetDocValues;

    /// <summary>
    /// Enables per field docvalues support.
    /// <p>
    /// Note, when extending this class, the name (<seealso cref="#getName"/>) is
    /// written into the index. In order for the field to be read, the
    /// name must resolve to your implementation via <seealso cref="#forName(String)"/>.
    /// this method uses Java's
    /// <seealso cref="ServiceLoader Service Provider Interface"/> to resolve format names.
    /// <p>
    /// Files written by each docvalues format have an additional suffix containing the
    /// format name. For example, in a per-field configuration instead of <tt>_1.dat</tt>
    /// filenames would look like <tt>_1_Lucene40_0.dat</tt>. </summary>
    /// <seealso cref= ServiceLoader
    /// @lucene.experimental </seealso>
    [DocValuesFormatName("PerFieldDV40")]
    public abstract class PerFieldDocValuesFormat : DocValuesFormat
    {
        // LUCENENET specific: Removing this static variable, since name is now determined by the DocValuesFormatNameAttribute.
        ///// <summary>
        ///// Name of this <seealso cref="PostingsFormat"/>. </summary>
        //public static readonly string PER_FIELD_NAME = "PerFieldDV40";

        /// <summary>
        /// <seealso cref="FieldInfo"/> attribute name used to store the
        ///  format name for each field.
        /// </summary>
        public static readonly string PER_FIELD_FORMAT_KEY = typeof(PerFieldDocValuesFormat).Name + ".format";

        /// <summary>
        /// <seealso cref="FieldInfo"/> attribute name used to store the
        ///  segment suffix name for each field.
        /// </summary>
        public static readonly string PER_FIELD_SUFFIX_KEY = typeof(PerFieldDocValuesFormat).Name + ".suffix";

        /// <summary>
        /// Sole constructor. </summary>
        public PerFieldDocValuesFormat()
            : base()
        {
        }

        public override sealed DocValuesConsumer FieldsConsumer(SegmentWriteState state)
        {
            return new FieldsWriter(this, state);
        }

        internal class ConsumerAndSuffix : IDisposable
        {
            internal DocValuesConsumer Consumer { get; set; }
            internal int Suffix { get; set; }

            public void Dispose()
            {
                Consumer.Dispose();
            }
        }

        private class FieldsWriter : DocValuesConsumer
        {
            private readonly PerFieldDocValuesFormat outerInstance;

            internal readonly IDictionary<DocValuesFormat, ConsumerAndSuffix> formats = new Dictionary<DocValuesFormat, ConsumerAndSuffix>();
            internal readonly IDictionary<string, int?> suffixes = new Dictionary<string, int?>();

            internal readonly SegmentWriteState segmentWriteState;

            public FieldsWriter(PerFieldDocValuesFormat outerInstance, SegmentWriteState state)
            {
                this.outerInstance = outerInstance;
                segmentWriteState = state;
            }

            public override void AddNumericField(FieldInfo field, IEnumerable<long?> values)
            {
                GetInstance(field).AddNumericField(field, values);
            }

            public override void AddBinaryField(FieldInfo field, IEnumerable<BytesRef> values)
            {
                GetInstance(field).AddBinaryField(field, values);
            }

            public override void AddSortedField(FieldInfo field, IEnumerable<BytesRef> values, IEnumerable<long?> docToOrd)
            {
                GetInstance(field).AddSortedField(field, values, docToOrd);
            }

            public override void AddSortedSetField(FieldInfo field, IEnumerable<BytesRef> values, IEnumerable<long?> docToOrdCount, IEnumerable<long?> ords)
            {
                GetInstance(field).AddSortedSetField(field, values, docToOrdCount, ords);
            }

            internal virtual DocValuesConsumer GetInstance(FieldInfo field)
            {
                DocValuesFormat format = null;
                if (field.DocValuesGen != -1)
                {
                    string formatName = field.GetAttribute(PER_FIELD_FORMAT_KEY);
                    // this means the field never existed in that segment, yet is applied updates
                    if (formatName != null)
                    {
                        format = DocValuesFormat.ForName(formatName);
                    }
                }
                if (format == null)
                {
                    format = outerInstance.GetDocValuesFormatForField(field.Name);
                }
                if (format == null)
                {
                    throw new InvalidOperationException("invalid null DocValuesFormat for field=\"" + field.Name + "\"");
                }
                string formatName_ = format.Name;

                string previousValue = field.PutAttribute(PER_FIELD_FORMAT_KEY, formatName_);
                Debug.Assert(field.DocValuesGen != -1 || previousValue == null, "formatName=" + formatName_ + " prevValue=" + previousValue);

                int? suffix = null;

                ConsumerAndSuffix consumer;
                if (!formats.TryGetValue(format, out consumer) || consumer == null)
                {
                    // First time we are seeing this format; create a new instance

                    if (field.DocValuesGen != -1)
                    {
                        string suffixAtt = field.GetAttribute(PER_FIELD_SUFFIX_KEY);
                        // even when dvGen is != -1, it can still be a new field, that never
                        // existed in the segment, and therefore doesn't have the recorded
                        // attributes yet.
                        if (suffixAtt != null)
                        {
                            suffix = Convert.ToInt32(suffixAtt, CultureInfo.InvariantCulture);
                        }
                    }

                    if (suffix == null)
                    {
                        // bump the suffix
                        if (!suffixes.TryGetValue(formatName_, out suffix) || suffix == null)
                        {
                            suffix = 0;
                        }
                        else
                        {
                            suffix = suffix + 1;
                        }
                    }
                    suffixes[formatName_] = suffix;

                    string segmentSuffix = GetFullSegmentSuffix(segmentWriteState.SegmentSuffix, GetSuffix(formatName_, Convert.ToString(suffix, CultureInfo.InvariantCulture)));
                    consumer = new ConsumerAndSuffix();
                    consumer.Consumer = format.FieldsConsumer(new SegmentWriteState(segmentWriteState, segmentSuffix));
                    consumer.Suffix = suffix.Value; // LUCENENET NOTE: At this point suffix cannot be null
                    formats[format] = consumer;
                }
                else
                {
                    // we've already seen this format, so just grab its suffix
                    Debug.Assert(suffixes.ContainsKey(formatName_));
                    suffix = consumer.Suffix;
                }

                previousValue = field.PutAttribute(PER_FIELD_SUFFIX_KEY, Convert.ToString(suffix, CultureInfo.InvariantCulture));
                Debug.Assert(field.DocValuesGen != -1 || previousValue == null, "suffix=" + Convert.ToString(suffix, CultureInfo.InvariantCulture) + " prevValue=" + previousValue);

                // TODO: we should only provide the "slice" of FIS
                // that this DVF actually sees ...
                return consumer.Consumer;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    // Close all subs
                    IOUtils.Close(formats.Values);
                }
            }
        }

        internal static string GetSuffix(string formatName, string suffix)
        {
            return formatName + "_" + suffix;
        }

        internal static string GetFullSegmentSuffix(string outerSegmentSuffix, string segmentSuffix)
        {
            if (outerSegmentSuffix.Length == 0)
            {
                return segmentSuffix;
            }
            else
            {
                return outerSegmentSuffix + "_" + segmentSuffix;
            }
        }

        private class FieldsReader : DocValuesProducer
        {
            private readonly PerFieldDocValuesFormat outerInstance;

            // LUCENENET specific: Use StringComparer.Ordinal to get the same ordering as Java
            internal readonly IDictionary<string, DocValuesProducer> fields = new SortedDictionary<string, DocValuesProducer>(StringComparer.Ordinal);
            internal readonly IDictionary<string, DocValuesProducer> formats = new Dictionary<string, DocValuesProducer>();

            public FieldsReader(PerFieldDocValuesFormat outerInstance, SegmentReadState readState)
            {
                this.outerInstance = outerInstance;

                // Read _X.per and init each format:
                bool success = false;
                try
                {
                    // Read field name -> format name
                    foreach (FieldInfo fi in readState.FieldInfos)
                    {
                        if (fi.HasDocValues)
                        {
                            string fieldName = fi.Name;
                            string formatName = fi.GetAttribute(PER_FIELD_FORMAT_KEY);
                            if (formatName != null)
                            {
                                // null formatName means the field is in fieldInfos, but has no docvalues!
                                string suffix = fi.GetAttribute(PER_FIELD_SUFFIX_KEY);
                                Debug.Assert(suffix != null);
                                DocValuesFormat format = DocValuesFormat.ForName(formatName);
                                string segmentSuffix = GetFullSegmentSuffix(readState.SegmentSuffix, GetSuffix(formatName, suffix));
                                if (!formats.ContainsKey(segmentSuffix))
                                {
                                    formats[segmentSuffix] = format.FieldsProducer(new SegmentReadState(readState, segmentSuffix));
                                }
                                fields[fieldName] = formats[segmentSuffix];
                            }
                        }
                    }
                    success = true;
                }
                finally
                {
                    if (!success)
                    {
                        IOUtils.CloseWhileHandlingException(formats.Values);
                    }
                }
            }

            internal FieldsReader(PerFieldDocValuesFormat outerInstance, FieldsReader other)
            {
                this.outerInstance = outerInstance;

                IDictionary<DocValuesProducer, DocValuesProducer> oldToNew = new IdentityHashMap<DocValuesProducer, DocValuesProducer>();
                // First clone all formats
                foreach (KeyValuePair<string, DocValuesProducer> ent in other.formats)
                {
                    DocValuesProducer values = ent.Value;
                    formats[ent.Key] = values;
                    oldToNew[ent.Value] = values;
                }

                // Then rebuild fields:
                foreach (KeyValuePair<string, DocValuesProducer> ent in other.fields)
                {
                    DocValuesProducer producer;
                    oldToNew.TryGetValue(ent.Value, out producer);
                    Debug.Assert(producer != null);
                    fields[ent.Key] = producer;
                }
            }

            public override NumericDocValues GetNumeric(FieldInfo field)
            {
                DocValuesProducer producer;
                if (fields.TryGetValue(field.Name, out producer) && producer != null)
                {
                    return producer.GetNumeric(field);
                }
                return null;
            }

            public override BinaryDocValues GetBinary(FieldInfo field)
            {
                DocValuesProducer producer;
                if (fields.TryGetValue(field.Name, out producer) && producer != null)
                {
                    return producer.GetBinary(field);
                }
                return null;
            }

            public override SortedDocValues GetSorted(FieldInfo field)
            {
                DocValuesProducer producer;
                if (fields.TryGetValue(field.Name, out producer) && producer != null)
                {
                    return producer.GetSorted(field);
                }
                return null;
            }

            public override SortedSetDocValues GetSortedSet(FieldInfo field)
            {
                DocValuesProducer producer;
                if (fields.TryGetValue(field.Name, out producer) && producer != null)
                {
                    return producer.GetSortedSet(field);
                }
                return null;
            }

            public override IBits GetDocsWithField(FieldInfo field)
            {
                DocValuesProducer producer;
                if (fields.TryGetValue(field.Name, out producer) && producer != null)
                {
                    return producer.GetDocsWithField(field);
                }
                return null;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    IOUtils.Close(formats.Values);
                }
            }

            public object Clone()
            {
                return new FieldsReader(outerInstance, this);
            }

            public override long RamBytesUsed()
            {
                long size = 0;
                foreach (KeyValuePair<string, DocValuesProducer> entry in formats)
                {
                    size += (entry.Key.Length * RamUsageEstimator.NUM_BYTES_CHAR) 
                        + entry.Value.RamBytesUsed();
                }
                return size;
            }

            public override void CheckIntegrity()
            {
                foreach (DocValuesProducer format in formats.Values)
                {
                    format.CheckIntegrity();
                }
            }
        }

        public override sealed DocValuesProducer FieldsProducer(SegmentReadState state)
        {
            return new FieldsReader(this, state);
        }

        /// <summary>
        /// Returns the doc values format that should be used for writing
        /// new segments of <code>field</code>.
        /// <p>
        /// The field to format mapping is written to the index, so
        /// this method is only invoked when writing, not when reading.
        /// </summary>
        public abstract DocValuesFormat GetDocValuesFormatForField(string field);
    }
}