using Lucene.Net.Support;
using Lucene.Net.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lucene.Net.Search
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

    using AtomicReaderContext = Lucene.Net.Index.AtomicReaderContext;
    using BinaryDocValues = Lucene.Net.Index.BinaryDocValues;
    using IBits = Lucene.Net.Util.IBits;
    using BytesRef = Lucene.Net.Util.BytesRef;
    using SortedDocValues = Lucene.Net.Index.SortedDocValues;

    /// <summary>
    /// Expert: a FieldComparer compares hits so as to determine their
    /// sort order when collecting the top results with {@link
    /// TopFieldCollector}.  The concrete public FieldComparer
    /// classes here correspond to the SortField types.
    ///
    /// <p>this API is designed to achieve high performance
    /// sorting, by exposing a tight interaction with {@link
    /// FieldValueHitQueue} as it visits hits.  Whenever a hit is
    /// competitive, it's enrolled into a virtual slot, which is
    /// an int ranging from 0 to numHits-1.  The {@link
    /// FieldComparer} is made aware of segment transitions
    /// during searching in case any internal state it's tracking
    /// needs to be recomputed during these transitions.</p>
    ///
    /// <p>A comparer must define these functions:</p>
    ///
    /// <ul>
    ///
    ///  <li> <seealso cref="#compare"/> Compare a hit at 'slot a'
    ///       with hit 'slot b'.
    ///
    ///  <li> <seealso cref="#setBottom"/> this method is called by
    ///       <seealso cref="FieldValueHitQueue"/> to notify the
    ///       FieldComparer of the current weakest ("bottom")
    ///       slot.  Note that this slot may not hold the weakest
    ///       value according to your comparer, in cases where
    ///       your comparer is not the primary one (ie, is only
    ///       used to break ties from the comparers before it).
    ///
    ///  <li> <seealso cref="#compareBottom"/> Compare a new hit (docID)
    ///       against the "weakest" (bottom) entry in the queue.
    ///
    ///  <li> <seealso cref="#setTopValue"/> this method is called by
    ///       <seealso cref="TopFieldCollector"/> to notify the
    ///       FieldComparer of the top most value, which is
    ///       used by future calls to <seealso cref="#compareTop"/>.
    ///
    ///  <li> <seealso cref="#compareBottom"/> Compare a new hit (docID)
    ///       against the "weakest" (bottom) entry in the queue.
    ///
    ///  <li> <seealso cref="#compareTop"/> Compare a new hit (docID)
    ///       against the top value previously set by a call to
    ///       <seealso cref="#setTopValue"/>.
    ///
    ///  <li> <seealso cref="#copy"/> Installs a new hit into the
    ///       priority queue.  The <seealso cref="FieldValueHitQueue"/>
    ///       calls this method when a new hit is competitive.
    ///
    ///  <li> <seealso cref="#setNextReader(AtomicReaderContext)"/> Invoked
    ///       when the search is switching to the next segment.
    ///       You may need to update internal state of the
    ///       comparer, for example retrieving new values from
    ///       the <seealso cref="IFieldCache"/>.
    ///
    ///  <li> <seealso cref="#value"/> Return the sort value stored in
    ///       the specified slot.  this is only called at the end
    ///       of the search, in order to populate {@link
    ///       FieldDoc#fields} when returning the top results.
    /// </ul>
    ///
    /// @lucene.experimental
    /// </summary>
#if FEATURE_SERIALIZABLE
    [Serializable]
#endif
    public abstract class FieldComparer<T> : FieldComparer
    {
        /// <summary>
        /// Compare hit at slot1 with hit at slot2.
        /// </summary>
        /// <param name="slot1"> first slot to compare </param>
        /// <param name="slot2"> second slot to compare </param>
        /// <returns> any N < 0 if slot2's value is sorted after
        /// slot1, any N > 0 if the slot2's value is sorted before
        /// slot1 and 0 if they are equal </returns>
        public abstract override int Compare(int slot1, int slot2);

        /// <summary>
        /// Set the bottom slot, ie the "weakest" (sorted last)
        /// entry in the queue.  When <seealso cref="#compareBottom"/> is
        /// called, you should compare against this slot.  this
        /// will always be called before <seealso cref="#compareBottom"/>.
        /// </summary>
        /// <param name="slot"> the currently weakest (sorted last) slot in the queue </param>
        public abstract override void SetBottom(int slot);

        /// <summary>
        /// Record the top value, for future calls to {@link
        /// #compareTop}.  this is only called for searches that
        /// use searchAfter (deep paging), and is called before any
        /// calls to <seealso cref="#setNextReader"/>.
        /// </summary>
        public abstract override void SetTopValue(object value);

        /// <summary>
        /// Compare the bottom of the queue with this doc.  this will
        /// only invoked after setBottom has been called.  this
        /// should return the same result as {@link
        /// #compare(int,int)}} as if bottom were slot1 and the new
        /// document were slot 2.
        ///
        /// <p>For a search that hits many results, this method
        /// will be the hotspot (invoked by far the most
        /// frequently).</p>
        /// </summary>
        /// <param name="doc"> that was hit </param>
        /// <returns> any N < 0 if the doc's value is sorted after
        /// the bottom entry (not competitive), any N > 0 if the
        /// doc's value is sorted before the bottom entry and 0 if
        /// they are equal. </returns>
        public abstract override int CompareBottom(int doc);

        /// <summary>
        /// Compare the top value with this doc.  this will
        /// only invoked after setTopValue has been called.  this
        /// should return the same result as {@link
        /// #compare(int,int)}} as if topValue were slot1 and the new
        /// document were slot 2.  this is only called for searches that
        /// use searchAfter (deep paging).
        /// </summary>
        /// <param name="doc"> that was hit </param>
        /// <returns> any N < 0 if the doc's value is sorted after
        /// the bottom entry (not competitive), any N > 0 if the
        /// doc's value is sorted before the bottom entry and 0 if
        /// they are equal. </returns>
        public abstract override int CompareTop(int doc);

        /// <summary>
        /// this method is called when a new hit is competitive.
        /// You should copy any state associated with this document
        /// that will be required for future comparisons, into the
        /// specified slot.
        /// </summary>
        /// <param name="slot"> which slot to copy the hit to </param>
        /// <param name="doc"> docID relative to current reader </param>
        public abstract override void Copy(int slot, int doc);

        /// <summary>
        /// Set a new <seealso cref="AtomicReaderContext"/>. All subsequent docIDs are relative to
        /// the current reader (you must add docBase if you need to
        /// map it to a top-level docID).
        /// </summary>
        /// <param name="context"> current reader context </param>
        /// <returns> the comparer to use for this segment; most
        ///   comparers can just return "this" to reuse the same
        ///   comparer across segments </returns>
        /// <exception cref="IOException"> if there is a low-level IO error </exception>
        public abstract override FieldComparer SetNextReader(AtomicReaderContext context);

        /// <summary>
        /// Returns -1 if first is less than second.  Default
        ///  impl to assume the type implements Comparable and
        ///  invoke .compareTo; be sure to override this method if
        ///  your FieldComparer's type isn't a Comparable or
        ///  if your values may sometimes be null
        /// </summary>
        public virtual int CompareValues(T first, T second)
        {
            if (object.ReferenceEquals(first, default(T)))
            {
                return object.ReferenceEquals(second, default(T)) ? 0 : -1;
            }
            else if (object.ReferenceEquals(second, default(T)))
            {
                return 1;
            }
            else
            {
                // LUCENENET NOTE: We need to compare using NaturalComparer<T>
                // to ensure that if comparing strings we do so in Ordinal sort order
                return ArrayUtil.GetNaturalComparer<T>().Compare(first, second);
            }
        }

        public override int CompareValues(object first, object second)
        {
            return CompareValues((T)first, (T)second);
        }
    }

    // .NET Port: Using a non-generic class here so that we avoid having to use the
    // type parameter to access these nested types. Also moving non-generic methods here for casting without generics.
#if FEATURE_SERIALIZABLE
    [Serializable]
#endif
    public abstract class FieldComparer
    {
        public abstract int CompareValues(object first, object second);

        //Set up abstract methods
        /// <summary>
        /// Compare hit at slot1 with hit at slot2.
        /// </summary>
        /// <param name="slot1"> first slot to compare </param>
        /// <param name="slot2"> second slot to compare </param>
        /// <returns> any N < 0 if slot2's value is sorted after
        /// slot1, any N > 0 if the slot2's value is sorted before
        /// slot1 and 0 if they are equal </returns>
        public abstract int Compare(int slot1, int slot2);

        /// <summary>
        /// Set the bottom slot, ie the "weakest" (sorted last)
        /// entry in the queue.  When <seealso cref="#compareBottom"/> is
        /// called, you should compare against this slot.  this
        /// will always be called before <seealso cref="#compareBottom"/>.
        /// </summary>
        /// <param name="slot"> the currently weakest (sorted last) slot in the queue </param>
        public abstract void SetBottom(int slot);

        /// <summary>
        /// Record the top value, for future calls to {@link
        /// #compareTop}.  this is only called for searches that
        /// use searchAfter (deep paging), and is called before any
        /// calls to <seealso cref="#setNextReader"/>.
        /// </summary>
        public abstract void SetTopValue(object value);

        /// <summary>
        /// Compare the bottom of the queue with this doc.  this will
        /// only invoked after setBottom has been called.  this
        /// should return the same result as {@link
        /// #compare(int,int)}} as if bottom were slot1 and the new
        /// document were slot 2.
        ///
        /// <p>For a search that hits many results, this method
        /// will be the hotspot (invoked by far the most
        /// frequently).</p>
        /// </summary>
        /// <param name="doc"> that was hit </param>
        /// <returns> any N < 0 if the doc's value is sorted after
        /// the bottom entry (not competitive), any N > 0 if the
        /// doc's value is sorted before the bottom entry and 0 if
        /// they are equal. </returns>
        public abstract int CompareBottom(int doc);

        /// <summary>
        /// Compare the top value with this doc.  this will
        /// only invoked after setTopValue has been called.  this
        /// should return the same result as {@link
        /// #compare(int,int)}} as if topValue were slot1 and the new
        /// document were slot 2.  this is only called for searches that
        /// use searchAfter (deep paging).
        /// </summary>
        /// <param name="doc"> that was hit </param>
        /// <returns> any N < 0 if the doc's value is sorted after
        /// the bottom entry (not competitive), any N > 0 if the
        /// doc's value is sorted before the bottom entry and 0 if
        /// they are equal. </returns>
        public abstract int CompareTop(int doc);

        /// <summary>
        /// this method is called when a new hit is competitive.
        /// You should copy any state associated with this document
        /// that will be required for future comparisons, into the
        /// specified slot.
        /// </summary>
        /// <param name="slot"> which slot to copy the hit to </param>
        /// <param name="doc"> docID relative to current reader </param>
        public abstract void Copy(int slot, int doc);

        /// <summary>
        /// Set a new <seealso cref="AtomicReaderContext"/>. All subsequent docIDs are relative to
        /// the current reader (you must add docBase if you need to
        /// map it to a top-level docID).
        /// </summary>
        /// <param name="context"> current reader context </param>
        /// <returns> the comparer to use for this segment; most
        ///   comparers can just return "this" to reuse the same
        ///   comparer across segments </returns>
        /// <exception cref="IOException"> if there is a low-level IO error </exception>
        public abstract FieldComparer SetNextReader(AtomicReaderContext context);

        /// <summary>
        /// Sets the Scorer to use in case a document's score is
        ///  needed.
        /// </summary>
        /// <param name="scorer"> Scorer instance that you should use to
        /// obtain the current hit's score, if necessary.  </param>
        public virtual void SetScorer(Scorer scorer)
        {
            // Empty implementation since most comparers don't need the score. this
            // can be overridden by those that need it.
        }

        /// <summary>
        /// Return the actual value in the slot.
        /// LUCENENET NOTE: This was value(int) in Lucene.
        /// </summary>
        /// <param name="slot"> the value </param>
        /// <returns> value in this slot </returns>
        public abstract IComparable this[int slot] { get; }


        internal static readonly IComparer<double> SIGNED_ZERO_COMPARER = new SignedZeroComparer();



        /// <summary>
        /// Base FieldComparer class for numeric types
        /// </summary>
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        public abstract class NumericComparer<T> : FieldComparer<T>
            where T : struct
        {
            protected readonly T? m_missingValue;
            protected readonly string m_field;
            protected IBits m_docsWithField;

            public NumericComparer(string field, T? missingValue)
            {
                this.m_field = field;
                this.m_missingValue = missingValue;
            }

            public override FieldComparer SetNextReader(AtomicReaderContext context)
            {
                if (m_missingValue != null)
                {
                    m_docsWithField = FieldCache.DEFAULT.GetDocsWithField((context.AtomicReader), m_field);
                    // optimization to remove unneeded checks on the bit interface:
                    if (m_docsWithField is Lucene.Net.Util.Bits.MatchAllBits)
                    {
                        m_docsWithField = null;
                    }
                }
                else
                {
                    m_docsWithField = null;
                }
                return this;
            }
        }

        /// <summary>
        /// Parses field's values as byte (using {@link
        ///  FieldCache#getBytes} and sorts by ascending value
        /// </summary>
        [Obsolete, CLSCompliant(false)] // LUCENENET NOTE: marking non-CLS compliant because of sbyte - it is obsolete, anyway
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        public sealed class ByteComparer : NumericComparer<sbyte>
        {
            private readonly sbyte[] values;
            private readonly FieldCache.IByteParser parser; 
            private FieldCache.Bytes currentReaderValues;
            private sbyte bottom;
            private sbyte topValue;

            internal ByteComparer(int numHits, string field, FieldCache.IParser parser, sbyte? missingValue)
                : base(field, missingValue)
            {
                values = new sbyte[numHits];
                this.parser = (FieldCache.IByteParser)parser;
            }

            public override int Compare(int slot1, int slot2)
            {
                // LUCENENET NOTE: Same logic as the Byte.compare() method in Java
                return values[slot1].CompareTo(values[slot2]);
            }

            public override int CompareBottom(int doc)
            {
                sbyte v2 = (sbyte)currentReaderValues.Get(doc);
                // Test for v2 == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && v2 == 0 && !m_docsWithField.Get(doc))
                {
                    v2 = m_missingValue.GetValueOrDefault();
                }
                // LUCENENET NOTE: Same logic as the Byte.compare() method in Java
                return bottom.CompareTo(v2);
            }

            public override void Copy(int slot, int doc)
            {
                sbyte v2 = (sbyte)currentReaderValues.Get(doc);
                // Test for v2 == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && v2 == 0 && !m_docsWithField.Get(doc))
                {
                    v2 = m_missingValue.GetValueOrDefault();
                }
                values[slot] = v2;
            }

            public override FieldComparer SetNextReader(AtomicReaderContext context)
            {
                // NOTE: must do this before calling super otherwise
                // we compute the docsWithField Bits twice!
                currentReaderValues = FieldCache.DEFAULT.GetBytes((context.AtomicReader), m_field, parser, m_missingValue != null);
                return base.SetNextReader(context);
            }

            public override void SetBottom(int slot)
            {
                this.bottom = values[slot];
            }

            public override void SetTopValue(object value)
            {
                topValue = (sbyte)value;
            }

            public override IComparable this[int slot]
            {
                get { return values[slot]; }
            }

            public override int CompareTop(int doc)
            {
                sbyte docValue = (sbyte)currentReaderValues.Get(doc);
                // Test for docValue == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && docValue == 0 && !m_docsWithField.Get(doc))
                {
                    docValue = m_missingValue.GetValueOrDefault();
                }
                // LUCENENET NOTE: Same logic as the Byte.compare() method in Java
                return topValue.CompareTo(docValue);
            }
        }

        /// <summary>
        /// Parses field's values as double (using {@link
        ///  FieldCache#getDoubles} and sorts by ascending value
        /// </summary>
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        public sealed class DoubleComparer : NumericComparer<double>
        {
            private readonly double[] values;
            private readonly FieldCache.IDoubleParser parser;
            private FieldCache.Doubles currentReaderValues;
            private double bottom;
            private double topValue;

            internal DoubleComparer(int numHits, string field, FieldCache.IParser parser, double? missingValue)
                : base(field, missingValue)
            {
                values = new double[numHits];
                this.parser = (FieldCache.IDoubleParser)parser;
            }

            public override int Compare(int slot1, int slot2)
            {
                double v1 = values[slot1];
                double v2 = values[slot2];

                int c = v1.CompareTo(v2);

                if (c == 0 && v1 == 0)
                {
                    // LUCENENET specific special case:
                    // In case of zero, we may have a "positive 0" or "negative 0"
                    // to tie-break.
                    c = SIGNED_ZERO_COMPARER.Compare(v1, v2);
                }

                return c;
            }

            public override int CompareBottom(int doc)
            {
                double v2 = currentReaderValues.Get(doc);
                // Test for v2 == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && v2 == 0.0 && !m_docsWithField.Get(doc))
                {
                    v2 = m_missingValue.GetValueOrDefault();
                }

                return bottom.CompareTo(v2);
            }

            public override void Copy(int slot, int doc)
            {
                double v2 = currentReaderValues.Get(doc);
                // Test for v2 == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && v2 == 0.0 && !m_docsWithField.Get(doc))
                {
                    v2 = m_missingValue.GetValueOrDefault();
                }

                values[slot] = v2;
            }

            public override FieldComparer SetNextReader(AtomicReaderContext context)
            {
                // NOTE: must do this before calling super otherwise
                // we compute the docsWithField Bits twice!
                currentReaderValues = FieldCache.DEFAULT.GetDoubles((context.AtomicReader), m_field, parser, m_missingValue != null);
                return base.SetNextReader(context);
            }

            public override void SetBottom(int slot)
            {
                this.bottom = values[slot];
            }

            public override void SetTopValue(object value)
            {
                topValue = (double)value;
            }

            public override IComparable this[int slot]
            {
                get { return values[slot]; }
            }

            public override int CompareTop(int doc)
            {
                double docValue = currentReaderValues.Get(doc);
                // Test for docValue == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && docValue == 0 && !m_docsWithField.Get(doc))
                {
                    docValue = m_missingValue.GetValueOrDefault();
                }
                return topValue.CompareTo(docValue);
            }
        }

        /// <summary>
        /// Parses field's values as float (using {@link
        ///  FieldCache#getFloats} and sorts by ascending value
        /// <para/>
        /// NOTE: This was FloatComparator in Lucene
        /// </summary>
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        public sealed class SingleComparer : NumericComparer<float>
        {
            private readonly float[] values;
            private readonly FieldCache.ISingleParser parser;
            private FieldCache.Singles currentReaderValues;
            private float bottom;
            private float topValue;

            internal SingleComparer(int numHits, string field, FieldCache.IParser parser, float? missingValue)
                : base(field, missingValue)
            {
                values = new float[numHits];
                this.parser = (FieldCache.ISingleParser)parser;
            }

            public override int Compare(int slot1, int slot2)
            {
                float v1 = values[slot1];
                float v2 = values[slot2];

                int c = v1.CompareTo(v2);

                if (c == 0 && v1 == 0)
                {
                    // LUCENENET specific special case:
                    // In case of zero, we may have a "positive 0" or "negative 0"
                    // to tie-break.
                    c = SIGNED_ZERO_COMPARER.Compare(v1, v2);
                }

                return c;
            }

            public override int CompareBottom(int doc)
            {
                // TODO: are there sneaky non-branch ways to compute sign of float?
                float v2 = currentReaderValues.Get(doc);
                // Test for v2 == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && v2 == 0 && !m_docsWithField.Get(doc))
                {
                    v2 = m_missingValue.GetValueOrDefault();
                }

                return bottom.CompareTo(v2);
            }

            public override void Copy(int slot, int doc)
            {
                float v2 = currentReaderValues.Get(doc);
                // Test for v2 == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && v2 == 0 && !m_docsWithField.Get(doc))
                {
                    v2 = m_missingValue.GetValueOrDefault();
                }

                values[slot] = v2;
            }

            public override FieldComparer SetNextReader(AtomicReaderContext context)
            {
                // NOTE: must do this before calling super otherwise
                // we compute the docsWithField Bits twice!
                currentReaderValues = FieldCache.DEFAULT.GetSingles((context.AtomicReader), m_field, parser, m_missingValue != null);
                return base.SetNextReader(context);
            }

            public override void SetBottom(int slot)
            {
                this.bottom = values[slot];
            }

            public override void SetTopValue(object value)
            {
                topValue = (float)value;
            }

            public override IComparable this[int slot]
            {
                get { return values[slot]; }
            }

            public override int CompareTop(int doc)
            {
                float docValue = currentReaderValues.Get(doc);
                // Test for docValue == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && docValue == 0 && !m_docsWithField.Get(doc))
                {
                    docValue = m_missingValue.GetValueOrDefault();
                }
                return topValue.CompareTo(docValue);
            }
        }

        /// <summary>
        /// Parses field's values as short (using {@link
        /// FieldCache#getShorts} and sorts by ascending value
        /// <para/>
        /// NOTE: This was ShortComparator in Lucene
        /// </summary>
        [Obsolete]
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        public sealed class Int16Comparer : NumericComparer<short>
        {
            private readonly short[] values;
            private readonly FieldCache.IInt16Parser parser;
            private FieldCache.Int16s currentReaderValues;
            private short bottom;
            private short topValue;

            internal Int16Comparer(int numHits, string field, FieldCache.IParser parser, short? missingValue)
                : base(field, missingValue)
            {
                values = new short[numHits];
                this.parser = (FieldCache.IInt16Parser)parser;
            }

            public override int Compare(int slot1, int slot2)
            {
                // LUCENENET NOTE: Same logic as the Byte.compare() method in Java
                return values[slot1].CompareTo(values[slot2]);
            }

            public override int CompareBottom(int doc)
            {
                short v2 = currentReaderValues.Get(doc);
                // Test for v2 == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && v2 == 0 && !m_docsWithField.Get(doc))
                {
                    v2 = m_missingValue.GetValueOrDefault();
                }

                // LUCENENET NOTE: Same logic as the Byte.compare() method in Java
                return bottom.CompareTo(v2);
            }

            public override void Copy(int slot, int doc)
            {
                short v2 = currentReaderValues.Get(doc);
                // Test for v2 == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && v2 == 0 && !m_docsWithField.Get(doc))
                {
                    v2 = m_missingValue.GetValueOrDefault();
                }

                values[slot] = v2;
            }

            public override FieldComparer SetNextReader(AtomicReaderContext context)
            {
                // NOTE: must do this before calling super otherwise
                // we compute the docsWithField Bits twice!
                currentReaderValues = FieldCache.DEFAULT.GetInt16s((context.AtomicReader), m_field, parser, m_missingValue != null);
                return base.SetNextReader(context);
            }

            public override void SetBottom(int slot)
            {
                this.bottom = values[slot];
            }

            public override void SetTopValue(object value)
            {
                topValue = (short)value;
            }

            public override IComparable this[int slot]
            {
                get { return values[slot]; }
            }

            public override int CompareTop(int doc)
            {
                short docValue = currentReaderValues.Get(doc);
                // Test for docValue == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && docValue == 0 && !m_docsWithField.Get(doc))
                {
                    docValue = m_missingValue.GetValueOrDefault();
                }
                return topValue.CompareTo(docValue);
            }
        }

        /// <summary>
        /// Parses field's values as int (using {@link
        /// FieldCache#getInts} and sorts by ascending value
        /// <para/>
        /// NOTE: This was IntComparator in Lucene
        /// </summary>
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        public sealed class Int32Comparer : NumericComparer<int>
        {
            private readonly int[] values;
            private readonly FieldCache.IInt32Parser parser;
            private FieldCache.Int32s currentReaderValues;
            private int bottom; // Value of bottom of queue
            private int topValue;

            internal Int32Comparer(int numHits, string field, FieldCache.IParser parser, int? missingValue)
                : base(field, missingValue)
            {
                values = new int[numHits];
                this.parser = (FieldCache.IInt32Parser)parser;
            }

            public override int Compare(int slot1, int slot2)
            {
                return values[slot1].CompareTo(values[slot2]);
            }

            public override int CompareBottom(int doc)
            {
                int v2 = currentReaderValues.Get(doc);
                // Test for v2 == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && v2 == 0 && !m_docsWithField.Get(doc))
                {
                    v2 = m_missingValue.GetValueOrDefault();
                }
                return bottom.CompareTo(v2);
            }

            public override void Copy(int slot, int doc)
            {
                int v2 = currentReaderValues.Get(doc);
                // Test for v2 == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && v2 == 0 && !m_docsWithField.Get(doc))
                {
                    v2 = m_missingValue.GetValueOrDefault();
                }

                values[slot] = v2;
            }

            public override FieldComparer SetNextReader(AtomicReaderContext context)
            {
                // NOTE: must do this before calling super otherwise
                // we compute the docsWithField Bits twice!
                currentReaderValues = FieldCache.DEFAULT.GetInt32s((context.AtomicReader), m_field, parser, m_missingValue != null);
                return base.SetNextReader(context);
            }

            public override void SetBottom(int slot)
            {
                this.bottom = values[slot];
            }

            public override void SetTopValue(object value)
            {
                topValue = (int)value;
            }

            public override IComparable this[int slot]
            {
                get { return values[slot]; }
            }

            public override int CompareTop(int doc)
            {
                int docValue = currentReaderValues.Get(doc);
                // Test for docValue == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && docValue == 0 && !m_docsWithField.Get(doc))
                {
                    docValue = m_missingValue.GetValueOrDefault();
                }
                return topValue.CompareTo(docValue);
            }
        }

        /// <summary>
        /// Parses field's values as long (using {@link
        /// FieldCache#getLongs} and sorts by ascending value
        /// <para/>
        /// NOTE: This was LongComparator in Lucene
        /// </summary>
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        public sealed class Int64Comparer : NumericComparer<long>
        {
            private readonly long[] values;
            private readonly FieldCache.IInt64Parser parser;
            private FieldCache.Int64s currentReaderValues;
            private long bottom;
            private long topValue;

            internal Int64Comparer(int numHits, string field, FieldCache.IParser parser, long? missingValue)
                : base(field, missingValue)
            {
                values = new long[numHits];
                this.parser = (FieldCache.IInt64Parser)parser;
            }

            public override int Compare(int slot1, int slot2)
            {
                // LUCENENET NOTE: Same logic as the Long.compare() method in Java
                return values[slot1].CompareTo(values[slot2]);
            }

            public override int CompareBottom(int doc)
            {
                // TODO: there are sneaky non-branch ways to compute
                // -1/+1/0 sign
                long v2 = currentReaderValues.Get(doc);
                // Test for v2 == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && v2 == 0 && !m_docsWithField.Get(doc))
                {
                    v2 = m_missingValue.GetValueOrDefault();
                }

                // LUCENENET NOTE: Same logic as the Long.compare() method in Java
                return bottom.CompareTo(v2);
            }

            public override void Copy(int slot, int doc)
            {
                long v2 = currentReaderValues.Get(doc);
                // Test for v2 == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && v2 == 0 && !m_docsWithField.Get(doc))
                {
                    v2 = m_missingValue.GetValueOrDefault();
                }

                values[slot] = v2;
            }

            public override FieldComparer SetNextReader(AtomicReaderContext context)
            {
                // NOTE: must do this before calling super otherwise
                // we compute the docsWithField Bits twice!
                currentReaderValues = FieldCache.DEFAULT.GetInt64s((context.AtomicReader), m_field, parser, m_missingValue != null);
                return base.SetNextReader(context);
            }

            public override void SetBottom(int slot)
            {
                this.bottom = values[slot];
            }

            public override void SetTopValue(object value)
            {
                topValue = (long)value;
            }

            public override IComparable this[int slot]
            {
                get { return values[slot]; }
            }

            public override int CompareTop(int doc)
            {
                long docValue = currentReaderValues.Get(doc);
                // Test for docValue == 0 to save Bits.get method call for
                // the common case (doc has value and value is non-zero):
                if (m_docsWithField != null && docValue == 0 && !m_docsWithField.Get(doc))
                {
                    docValue = m_missingValue.GetValueOrDefault();
                }
                return topValue.CompareTo(docValue);
            }
        }

        /// <summary>
        /// Sorts by descending relevance.  NOTE: if you are
        ///  sorting only by descending relevance and then
        ///  secondarily by ascending docID, performance is faster
        ///  using <seealso cref="TopScoreDocCollector"/> directly (which {@link
        ///  IndexSearcher#search} uses when no <seealso cref="Sort"/> is
        ///  specified).
        /// </summary>
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        public sealed class RelevanceComparer : FieldComparer<float>
        {
            private readonly float[] scores;
            private float bottom;
            private Scorer scorer;
            private float topValue;

            internal RelevanceComparer(int numHits)
            {
                scores = new float[numHits];
            }

            public override int Compare(int slot1, int slot2)
            {
                return scores[slot2].CompareTo(scores[slot1]);
            }

            public override int CompareBottom(int doc)
            {
                float score = scorer.GetScore();
                Debug.Assert(!float.IsNaN(score));
                return score.CompareTo(bottom);
            }

            public override void Copy(int slot, int doc)
            {
                scores[slot] = scorer.GetScore();
                Debug.Assert(!float.IsNaN(scores[slot]));
            }

            public override FieldComparer SetNextReader(AtomicReaderContext context)
            {
                return this;
            }

            public override void SetBottom(int slot)
            {
                this.bottom = scores[slot];
            }

            public override void SetTopValue(object value)
            {
                topValue = (float)value;
            }

            public override void SetScorer(Scorer scorer)
            {
                // wrap with a ScoreCachingWrappingScorer so that successive calls to
                // score() will not incur score computation over and
                // over again.
                if (!(scorer is ScoreCachingWrappingScorer))
                {
                    this.scorer = new ScoreCachingWrappingScorer(scorer);
                }
                else
                {
                    this.scorer = scorer;
                }
            }

            public override IComparable this[int slot]
            {
                get { return Convert.ToSingle(scores[slot]); }
            }

            // Override because we sort reverse of natural Float order:
            public override int CompareValues(float first, float second)
            {
                // Reversed intentionally because relevance by default
                // sorts descending:
                return second.CompareTo(first);
            }

            public override int CompareTop(int doc)
            {
                float docValue = scorer.GetScore();
                Debug.Assert(!float.IsNaN(docValue));
                return docValue.CompareTo(topValue);
            }
        }

        /// <summary>
        /// Sorts by ascending docID </summary>
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        public sealed class DocComparer : FieldComparer<int?>
        {
            private readonly int[] docIDs;
            private int docBase;
            private int bottom;
            private int topValue;

            internal DocComparer(int numHits)
            {
                docIDs = new int[numHits];
            }

            public override int Compare(int slot1, int slot2)
            {
                // No overflow risk because docIDs are non-negative
                return docIDs[slot1] - docIDs[slot2];
            }

            public override int CompareBottom(int doc)
            {
                // No overflow risk because docIDs are non-negative
                return bottom - (docBase + doc);
            }

            public override void Copy(int slot, int doc)
            {
                docIDs[slot] = docBase + doc;
            }

            public override FieldComparer SetNextReader(AtomicReaderContext context)
            {
                // TODO: can we "map" our docIDs to the current
                // reader? saves having to then subtract on every
                // compare call
                this.docBase = context.DocBase;
                return this;
            }

            public override void SetBottom(int slot)
            {
                this.bottom = docIDs[slot];
            }

            public override void SetTopValue(object value)
            {
                topValue = (int)value;
            }

            public override IComparable this[int slot]
            {
                get { return Convert.ToInt32(docIDs[slot]); }
            }

            public override int CompareTop(int doc)
            {
                int docValue = docBase + doc;
                // LUCENENET NOTE: Same logic as the Integer.compare() method in Java
                return topValue.CompareTo(docValue);
            }
        }

        /// <summary>
        /// Sorts by field's natural Term sort order, using
        ///  ordinals.  this is functionally equivalent to {@link
        ///  Lucene.Net.Search.FieldComparer.TermValComparer}, but it first resolves the string
        ///  to their relative ordinal positions (using the index
        ///  returned by <seealso cref="IFieldCache#getTermsIndex"/>), and
        ///  does most comparisons using the ordinals.  For medium
        ///  to large results, this comparer will be much faster
        ///  than <seealso cref="Lucene.Net.Search.FieldComparer.TermValComparer"/>.  For very small
        ///  result sets it may be slower.
        /// </summary>
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        public class TermOrdValComparer : FieldComparer<BytesRef>
        {
            /* Ords for each slot.
	            @lucene.internal */
            internal readonly int[] ords;

            /* Values for each slot.
	            @lucene.internal */
            internal readonly BytesRef[] values;

            /* Which reader last copied a value into the slot. When
	            we compare two slots, we just compare-by-ord if the
	            readerGen is the same; else we must compare the
	            values (slower).
	            @lucene.internal */
            internal readonly int[] readerGen;

            /* Gen of current reader we are on.
	            @lucene.internal */
            internal int currentReaderGen = -1;

            /* Current reader's doc ord/values.
	            @lucene.internal */
            internal SortedDocValues termsIndex;

            internal readonly string field;

            /* Bottom slot, or -1 if queue isn't full yet
	            @lucene.internal */
            internal int bottomSlot = -1;

            /* Bottom ord (same as ords[bottomSlot] once bottomSlot
	            is set).  Cached for faster compares.
	            @lucene.internal */
            internal int bottomOrd;

            /* True if current bottom slot matches the current
	            reader.
	            @lucene.internal */
            internal bool bottomSameReader;

            /* Bottom value (same as values[bottomSlot] once
	            bottomSlot is set).  Cached for faster compares.
	            @lucene.internal */
            internal BytesRef bottomValue;

            /// <summary>
            /// Set by setTopValue. </summary>
            internal BytesRef topValue;

            internal bool topSameReader;
            internal int topOrd;

            internal readonly BytesRef tempBR = new BytesRef();

            /// <summary>
            /// -1 if missing values are sorted first, 1 if they are
            ///  sorted last
            /// </summary>
            internal readonly int missingSortCmp;

            /// <summary>
            /// Which ordinal to use for a missing value. </summary>
            internal readonly int missingOrd;

            /// <summary>
            /// Creates this, sorting missing values first. </summary>
            public TermOrdValComparer(int numHits, string field)
                : this(numHits, field, false)
            {
            }

            /// <summary>
            /// Creates this, with control over how missing values
            ///  are sorted.  Pass sortMissingLast=true to put
            ///  missing values at the end.
            /// </summary>
            public TermOrdValComparer(int numHits, string field, bool sortMissingLast)
            {
                ords = new int[numHits];
                values = new BytesRef[numHits];
                readerGen = new int[numHits];
                this.field = field;
                if (sortMissingLast)
                {
                    missingSortCmp = 1;
                    missingOrd = int.MaxValue;
                }
                else
                {
                    missingSortCmp = -1;
                    missingOrd = -1;
                }
            }

            public override int Compare(int slot1, int slot2)
            {
                if (readerGen[slot1] == readerGen[slot2])
                {
                    return ords[slot1] - ords[slot2];
                }

                BytesRef val1 = values[slot1];
                BytesRef val2 = values[slot2];
                if (val1 == null)
                {
                    if (val2 == null)
                    {
                        return 0;
                    }
                    return missingSortCmp;
                }
                else if (val2 == null)
                {
                    return -missingSortCmp;
                }
                return val1.CompareTo(val2);
            }

            public override int CompareBottom(int doc)
            {
                Debug.Assert(bottomSlot != -1);
                int docOrd = termsIndex.GetOrd(doc);
                if (docOrd == -1)
                {
                    docOrd = missingOrd;
                }
                if (bottomSameReader)
                {
                    // ord is precisely comparable, even in the equal case
                    return bottomOrd - docOrd;
                }
                else if (bottomOrd >= docOrd)
                {
                    // the equals case always means bottom is > doc
                    // (because we set bottomOrd to the lower bound in
                    // setBottom):
                    return 1;
                }
                else
                {
                    return -1;
                }
            }

            public override void Copy(int slot, int doc)
            {
                int ord = termsIndex.GetOrd(doc);
                if (ord == -1)
                {
                    ord = missingOrd;
                    values[slot] = null;
                }
                else
                {
                    Debug.Assert(ord >= 0);
                    if (values[slot] == null)
                    {
                        values[slot] = new BytesRef();
                    }
                    termsIndex.LookupOrd(ord, values[slot]);
                }
                ords[slot] = ord;
                readerGen[slot] = currentReaderGen;
            }

            /// <summary>
            /// Retrieves the SortedDocValues for the field in this segment </summary>
            protected virtual SortedDocValues GetSortedDocValues(AtomicReaderContext context, string field)
            {
                return FieldCache.DEFAULT.GetTermsIndex((context.AtomicReader), field);
            }

            public override FieldComparer SetNextReader(AtomicReaderContext context)
            {
                termsIndex = GetSortedDocValues(context, field);
                currentReaderGen++;

                if (topValue != null)
                {
                    // Recompute topOrd/SameReader
                    int ord = termsIndex.LookupTerm(topValue);
                    if (ord >= 0)
                    {
                        topSameReader = true;
                        topOrd = ord;
                    }
                    else
                    {
                        topSameReader = false;
                        topOrd = -ord - 2;
                    }
                }
                else
                {
                    topOrd = missingOrd;
                    topSameReader = true;
                }
                //System.out.println("  setNextReader topOrd=" + topOrd + " topSameReader=" + topSameReader);

                if (bottomSlot != -1)
                {
                    // Recompute bottomOrd/SameReader
                    SetBottom(bottomSlot);
                }

                return this;
            }

            public override void SetBottom(int slot)
            {
                bottomSlot = slot;

                bottomValue = values[bottomSlot];
                if (currentReaderGen == readerGen[bottomSlot])
                {
                    bottomOrd = ords[bottomSlot];
                    bottomSameReader = true;
                }
                else
                {
                    if (bottomValue == null)
                    {
                        // missingOrd is null for all segments
                        Debug.Assert(ords[bottomSlot] == missingOrd);
                        bottomOrd = missingOrd;
                        bottomSameReader = true;
                        readerGen[bottomSlot] = currentReaderGen;
                    }
                    else
                    {
                        int ord = termsIndex.LookupTerm(bottomValue);
                        if (ord < 0)
                        {
                            bottomOrd = -ord - 2;
                            bottomSameReader = false;
                        }
                        else
                        {
                            bottomOrd = ord;
                            // exact value match
                            bottomSameReader = true;
                            readerGen[bottomSlot] = currentReaderGen;
                            ords[bottomSlot] = bottomOrd;
                        }
                    }
                }
            }

            public override void SetTopValue(object value)
            {
                // null is fine: it means the last doc of the prior
                // search was missing this value
                topValue = (BytesRef)value;
                //System.out.println("setTopValue " + topValue);
            }

            public override IComparable this[int slot]
            {
                get { return values[slot]; }
            }

            public override int CompareTop(int doc)
            {
                int ord = termsIndex.GetOrd(doc);
                if (ord == -1)
                {
                    ord = missingOrd;
                }

                if (topSameReader)
                {
                    // ord is precisely comparable, even in the equal
                    // case
                    //System.out.println("compareTop doc=" + doc + " ord=" + ord + " ret=" + (topOrd-ord));
                    return topOrd - ord;
                }
                else if (ord <= topOrd)
                {
                    // the equals case always means doc is < value
                    // (because we set lastOrd to the lower bound)
                    return 1;
                }
                else
                {
                    return -1;
                }
            }

            public override int CompareValues(BytesRef val1, BytesRef val2)
            {
                if (val1 == null)
                {
                    if (val2 == null)
                    {
                        return 0;
                    }
                    return missingSortCmp;
                }
                else if (val2 == null)
                {
                    return -missingSortCmp;
                }
                return val1.CompareTo(val2);
            }
        }

        /// <summary>
        /// Sorts by field's natural Term sort order.  All
        ///  comparisons are done using BytesRef.compareTo, which is
        ///  slow for medium to large result sets but possibly
        ///  very fast for very small results sets.
        /// </summary>
        // TODO: should we remove this?  who really uses it?
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        public sealed class TermValComparer : FieldComparer<BytesRef>
        {
            // sentinels, just used internally in this comparer
            private static readonly byte[] MISSING_BYTES = new byte[0];

            private static readonly byte[] NON_MISSING_BYTES = new byte[0];

            private BytesRef[] values;
            private BinaryDocValues docTerms;
            private IBits docsWithField;
            private readonly string field;
            private BytesRef bottom;
            private BytesRef topValue;
            private readonly BytesRef tempBR = new BytesRef();

            // TODO: add missing first/last support here?

            /// <summary>
            /// Sole constructor. </summary>
            internal TermValComparer(int numHits, string field)
            {
                values = new BytesRef[numHits];
                this.field = field;
            }

            public override int Compare(int slot1, int slot2)
            {
                BytesRef val1 = values[slot1];
                BytesRef val2 = values[slot2];
                if (val1.Bytes == MISSING_BYTES)
                {
                    if (val2.Bytes == MISSING_BYTES)
                    {
                        return 0;
                    }
                    return -1;
                }
                else if (val2.Bytes == MISSING_BYTES)
                {
                    return 1;
                }

                return val1.CompareTo(val2);
            }

            public override int CompareBottom(int doc)
            {
                docTerms.Get(doc, tempBR);
                SetMissingBytes(doc, tempBR);
                return CompareValues(bottom, tempBR);
            }

            public override void Copy(int slot, int doc)
            {
                if (values[slot] == null)
                {
                    values[slot] = new BytesRef();
                }
                docTerms.Get(doc, values[slot]);
                SetMissingBytes(doc, values[slot]);
            }

            public override FieldComparer SetNextReader(AtomicReaderContext context)
            {
                docTerms = FieldCache.DEFAULT.GetTerms((context.AtomicReader), field, true);
                docsWithField = FieldCache.DEFAULT.GetDocsWithField((context.AtomicReader), field);
                return this;
            }

            public override void SetBottom(int slot)
            {
                this.bottom = values[slot];
            }

            public override void SetTopValue(object value)
            {
                if (value == null)
                {
                    throw new System.ArgumentException("value cannot be null");
                }
                topValue = (BytesRef)value;
            }

            public override IComparable this[int slot]
            {
                get { return values[slot]; }
            }

            public override int CompareValues(BytesRef val1, BytesRef val2)
            {
                // missing always sorts first:
                if (val1.Bytes == MISSING_BYTES)
                {
                    if (val2.Bytes == MISSING_BYTES)
                    {
                        return 0;
                    }
                    return -1;
                }
                else if (val2.Bytes == MISSING_BYTES)
                {
                    return 1;
                }
                return val1.CompareTo(val2);
            }

            public override int CompareTop(int doc)
            {
                docTerms.Get(doc, tempBR);
                SetMissingBytes(doc, tempBR);
                return CompareValues(topValue, tempBR);
            }

            private void SetMissingBytes(int doc, BytesRef br)
            {
                if (br.Length == 0)
                {
                    br.Offset = 0;
                    if (docsWithField.Get(doc) == false)
                    {
                        br.Bytes = MISSING_BYTES;
                    }
                    else
                    {
                        br.Bytes = NON_MISSING_BYTES;
                    }
                }
            }
        }
    }
}