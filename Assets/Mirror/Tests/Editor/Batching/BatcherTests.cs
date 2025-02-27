﻿using System;
using System.Linq;
using NUnit.Framework;

namespace Mirror.Tests.Batching
{
    public class BatcherTests
    {
        Batcher batcher;
        const int Threshold = 4;
        NetworkWriter writer;

        [SetUp]
        public void SetUp()
        {
            batcher = new Batcher(Threshold);
            writer = new NetworkWriter();
        }

        [Test]
        public void AddMessage()
        {
            byte[] message = {0x01, 0x02};
            batcher.AddMessage(new ArraySegment<byte>(message));
        }

        [Test]
        public void MakeNextBatch_OnlyAcceptsFreshWriter()
        {
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x01}));

            writer.WriteByte(0);
            Assert.Throws<ArgumentException>(() => {
                batcher.MakeNextBatch(writer);
            });
        }

        [Test]
        public void MakeNextBatch_NoMessage()
        {
            // make batch with no message
            bool result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(false));
        }

        [Test]
        public void MakeNextBatch_OneMessage()
        {
            // add message
            byte[] message = {0x01, 0x02};
            batcher.AddMessage(new ArraySegment<byte>(message));

            // make batch
            bool result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(writer.ToArray().SequenceEqual(message));
        }

        [Test]
        public void MakeNextBatch_MultipleMessages_AlmostFullBatch()
        {
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x01, 0x02}));
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x03}));

            // make batch
            bool result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(writer.ToArray().SequenceEqual(new byte[]{0x01, 0x02, 0x03}));

            // there should be no more batches to make
            Assert.That(batcher.MakeNextBatch(writer), Is.False);
        }

        [Test]
        public void MakeNextBatch_MultipleMessages_ExactlyFullBatch()
        {
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x01, 0x02}));
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x03, 0x04}));

            // make batch
            bool result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(writer.ToArray().SequenceEqual(new byte[]{0x01, 0x02, 0x03, 0x04}));

            // there should be no more batches to make
            Assert.That(batcher.MakeNextBatch(writer), Is.False);
        }

        [Test]
        public void MakeNextBatch_MultipleMessages_MoreThanOneBatch()
        {
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x01, 0x02}));
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x03, 0x04}));
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x05}));

            // first batch
            bool result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(writer.ToArray().SequenceEqual(new byte[]{0x01, 0x02, 0x03, 0x04}));

            // reset writer
            writer.Position = 0;

            // second batch
            result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(writer.ToArray().SequenceEqual(new byte[]{0x05}));
        }

        [Test]
        public void MakeNextBatch_MultipleMessages_Small_Giant_Small()
        {
            // small, too big to include in batch, small
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x01}));
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x02, 0x03, 0x04, 0x05}));
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x06, 0x07}));

            // first batch
            bool result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(writer.ToArray().SequenceEqual(new byte[]{0x01}));

            // reset writer
            writer.Position = 0;

            // second batch
            result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(writer.ToArray().SequenceEqual(new byte[]{0x02, 0x03, 0x04, 0x05}));

            // reset writer
            writer.Position = 0;

            // third batch
            result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(writer.ToArray().SequenceEqual(new byte[]{0x06, 0x07}));
        }

        // messages > threshold should simply be single batches.
        // those need to be supported too, for example:
        //   kcp prefers MTU sized batches
        //   but we still allow up to 144 KB max message size
        [Test]
        public void MakeNextBatch_LargerThanThreshold()
        {
            // make a larger than threshold message
            byte[] large = new byte[Threshold + 1];
            for (int i = 0; i < Threshold + 1; ++i)
                large[i] = (byte)i;
            batcher.AddMessage(new ArraySegment<byte>(large));

            // result should be only the large message
            bool result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(writer.ToArray().SequenceEqual(large));
        }

        // messages > threshold should simply be single batches.
        // those need to be supported too, for example:
        //   kcp prefers MTU sized batches
        //   but we still allow up to 144 KB max message size
        [Test]
        public void MakeNextBatch_LargerThanThreshold_BetweenSmallerMessages()
        {
            // make a larger than threshold message
            byte[] large = new byte[Threshold + 1];
            for (int i = 0; i < Threshold + 1; ++i)
                large[i] = (byte)i;

            // add two small, one large, two small messages.
            // to make sure everything around it is still batched,
            // and the large one is a separate batch.
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x01}));
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x02}));
            batcher.AddMessage(new ArraySegment<byte>(large));
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x03}));
            batcher.AddMessage(new ArraySegment<byte>(new byte[]{0x04}));

            // first batch should be the two small messages
            bool result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(writer.ToArray().SequenceEqual(new byte[]{0x01, 0x02}));

            // reset writer
            writer.Position = 0;

            // second batch should be only the large message
            result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(writer.ToArray().SequenceEqual(large));

            // reset writer
            writer.Position = 0;

            // third batch be the two small messages
            result = batcher.MakeNextBatch(writer);
            Assert.That(result, Is.EqualTo(true));
            Assert.That(writer.ToArray().SequenceEqual(new byte[]{0x03, 0x04}));
        }
    }
}
