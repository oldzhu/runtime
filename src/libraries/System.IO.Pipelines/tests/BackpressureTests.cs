// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Xunit;

namespace System.IO.Pipelines.Tests
{
    public class BackpressureTests : IDisposable
    {
        private const int PauseWriterThreshold = 64;
        private const int ResumeWriterThreshold = 32;

        public BackpressureTests()
        {
            _pool = new TestMemoryPool();
            _pipe = new Pipe(new PipeOptions(_pool, resumeWriterThreshold: ResumeWriterThreshold, pauseWriterThreshold: PauseWriterThreshold, readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false));
        }

        public void Dispose()
        {
            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
            _pool?.Dispose();
        }

        private readonly TestMemoryPool _pool;

        private readonly Pipe _pipe;

        [Fact]
        public void FlushAsyncAwaitableCompletesWhenReaderAdvancesUnderLow()
        {
            PipeWriter writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            ValueTask<FlushResult> flushAsync = writableBuffer.FlushAsync();

            Assert.False(flushAsync.IsCompleted);

            ReadResult result = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
            SequencePosition consumed = result.Buffer.GetPosition(33);
            _pipe.Reader.AdvanceTo(consumed, consumed);

            Assert.True(flushAsync.IsCompleted);
            FlushResult flushResult = flushAsync.GetAwaiter().GetResult();
            Assert.False(flushResult.IsCompleted);
        }

        [Fact]
        public void FlushAsyncAwaitableCompletesWhenReaderAdvancesExaminedUnderLow()
        {
            PipeWriter writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            ValueTask<FlushResult> flushAsync = writableBuffer.FlushAsync();

            Assert.False(flushAsync.IsCompleted);

            ReadResult result = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
            SequencePosition examined = result.Buffer.GetPosition(33);
            _pipe.Reader.AdvanceTo(result.Buffer.Start, examined);

            Assert.True(flushAsync.IsCompleted);
            FlushResult flushResult = flushAsync.GetAwaiter().GetResult();
            Assert.False(flushResult.IsCompleted);
        }

        [Fact]
        public async Task CanBufferPastPauseThresholdButGetPausedEachTime()
        {
            const int loops = 5;

            async Task WriteLoopAsync()
            {
                for (int i = 0; i < loops; i++)
                {
                    _pipe.Writer.WriteEmpty(PauseWriterThreshold);

                    ValueTask<FlushResult> flushTask = _pipe.Writer.FlushAsync();

                    Assert.False(flushTask.IsCompleted);

                    await flushTask;
                }

                _pipe.Writer.Complete();
            }

            Task writingTask = WriteLoopAsync();

            while (true)
            {
                ReadResult result = await _pipe.Reader.ReadAsync();

                if (result.IsCompleted)
                {
                    _pipe.Reader.AdvanceTo(result.Buffer.End);

                    Assert.Equal(PauseWriterThreshold * loops, result.Buffer.Length);
                    break;
                }

                _pipe.Reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            }

            await writingTask;
        }

        [Fact]
        public void FlushAsyncAwaitableDoesNotCompletesWhenReaderAdvancesUnderHight()
        {
            PipeWriter writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            ValueTask<FlushResult> flushAsync = writableBuffer.FlushAsync();

            Assert.False(flushAsync.IsCompleted);

            ReadResult result = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
            SequencePosition consumed = result.Buffer.GetPosition(ResumeWriterThreshold);
            _pipe.Reader.AdvanceTo(consumed, consumed);

            Assert.False(flushAsync.IsCompleted);
        }

        [Fact]
        public void FlushAsyncAwaitableResetsOnCommit()
        {
            PipeWriter writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            ValueTask<FlushResult> flushAsync = writableBuffer.FlushAsync();

            Assert.False(flushAsync.IsCompleted);

            ReadResult result = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
            SequencePosition consumed = result.Buffer.GetPosition(33);
            _pipe.Reader.AdvanceTo(consumed, consumed);

            Assert.True(flushAsync.IsCompleted);
            FlushResult flushResult = flushAsync.GetAwaiter().GetResult();
            Assert.False(flushResult.IsCompleted);

            writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            flushAsync = writableBuffer.FlushAsync();

            Assert.False(flushAsync.IsCompleted);
        }

        [Fact]
        public void FlushAsyncReturnsCompletedIfReaderCompletes()
        {
            PipeWriter writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            ValueTask<FlushResult> flushAsync = writableBuffer.FlushAsync();

            Assert.False(flushAsync.IsCompleted);

            _pipe.Reader.Complete();

            Assert.True(flushAsync.IsCompleted);
            FlushResult flushResult = flushAsync.GetAwaiter().GetResult();
            Assert.True(flushResult.IsCompleted);

            writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            flushAsync = writableBuffer.FlushAsync();
            flushResult = flushAsync.GetAwaiter().GetResult();

            Assert.True(flushResult.IsCompleted);
            Assert.True(flushAsync.IsCompleted);
        }

        [Fact]
        public async Task FlushAsyncReturnsCompletedIfReaderCompletesWithoutAdvance()
        {
            PipeWriter writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            ValueTask<FlushResult> flushAsync = writableBuffer.FlushAsync();

            Assert.False(flushAsync.IsCompleted);

            ReadResult result = await _pipe.Reader.ReadAsync();
            _pipe.Reader.Complete();

            Assert.True(flushAsync.IsCompleted);
            FlushResult flushResult = flushAsync.GetAwaiter().GetResult();
            Assert.True(flushResult.IsCompleted);

            writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            flushAsync = writableBuffer.FlushAsync();
            flushResult = flushAsync.GetAwaiter().GetResult();

            Assert.True(flushResult.IsCompleted);
            Assert.True(flushAsync.IsCompleted);
        }

        [Fact]
        public void FlushAsyncReturnsCompletedTaskWhenSizeLessThenLimit()
        {
            PipeWriter writableBuffer = _pipe.Writer.WriteEmpty(ResumeWriterThreshold);
            ValueTask<FlushResult> flushAsync = writableBuffer.FlushAsync();
            Assert.True(flushAsync.IsCompleted);
            FlushResult flushResult = flushAsync.GetAwaiter().GetResult();
            Assert.False(flushResult.IsCompleted);
        }

        [Fact]
        public void FlushAsyncReturnsNonCompletedSizeWhenCommitOverTheLimit()
        {
            PipeWriter writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            ValueTask<FlushResult> flushAsync = writableBuffer.FlushAsync();
            Assert.False(flushAsync.IsCompleted);
        }

        [Fact]
        public async Task ReadAtLeastAsyncUnblocksWriterIfMinimumlowerThanResumeThreshold()
        {
            PipeWriter writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            ValueTask<FlushResult> flushAsync = writableBuffer.FlushAsync();
            Assert.False(flushAsync.IsCompleted);

            ValueTask<ReadResult> readAsync = _pipe.Reader.ReadAtLeastAsync(PauseWriterThreshold * 3);

            Assert.False(readAsync.IsCompleted);

            // This should unblock the flush
            Assert.True(flushAsync.IsCompleted);

            for (int i = 0; i < 2; i++)
            {
                writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
                flushAsync = writableBuffer.FlushAsync();
                Assert.True(flushAsync.IsCompleted);
            }

            var result = await readAsync;
            Assert.Equal(PauseWriterThreshold * 3, result.Buffer.Length);
            _pipe.Reader.AdvanceTo(result.Buffer.End);
        }

        [Fact]
        public async Task FlushAsyncThrowsIfReaderCompletedWithException()
        {
            _pipe.Reader.Complete(new InvalidOperationException("Reader failed"));

            PipeWriter writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            InvalidOperationException invalidOperationException =
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await writableBuffer.FlushAsync());
            Assert.Equal("Reader failed", invalidOperationException.Message);
            invalidOperationException = await Assert.ThrowsAsync<InvalidOperationException>(async () => await writableBuffer.FlushAsync());
            Assert.Equal("Reader failed", invalidOperationException.Message);
        }

        [Fact]
        public void FlushAsyncAwaitableDoesNotCompleteWhenReaderUnexamines()
        {
            PipeWriter writableBuffer = _pipe.Writer.WriteEmpty(PauseWriterThreshold);
            ValueTask<FlushResult> flushAsync = writableBuffer.FlushAsync();

            ReadResult result = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
            SequencePosition examined = result.Buffer.GetPosition(2);
            // Examine 2, don't advance consumed
            _pipe.Reader.AdvanceTo(result.Buffer.Start, examined);

            Assert.False(flushAsync.IsCompleted);

            result = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
            // Examine 1 which is less than the previous examined index of 2
            examined = result.Buffer.GetPosition(1);
            _pipe.Reader.AdvanceTo(result.Buffer.Start, examined);

            Assert.False(flushAsync.IsCompleted);

            // Just make sure we can still release backpressure
            result = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
            examined = result.Buffer.GetPosition(ResumeWriterThreshold + 1);
            _pipe.Reader.AdvanceTo(examined, examined);

            Assert.True(flushAsync.IsCompleted);
        }
    }
}
