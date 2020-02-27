﻿using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace Sylvan.IO
{
	// An ArrayPool implementation that pools buffers of a single, fixed size.
#warning Super-naive implementation doesn't have a limit on size of pool; aka memory leak.
	sealed class FixedArrayPool<T> : ArrayPool<T>
	{
		ConcurrentBag<T[]> set;
		int size;

		public FixedArrayPool(int size)
		{
			this.size = size;
			this.set = new ConcurrentBag<T[]>();
		}

		public override T[] Rent(int minimumLength)
		{
			if (minimumLength != size) throw new ArgumentOutOfRangeException(nameof(minimumLength));

			return
				set.TryTake(out T[] array)
				? array
				: new T[size];
		}

		public override void Return(T[] array, bool clearArray = false)
		{
			if (array == null || array.Length != size)
			{
				return;
			}

			if (clearArray)
				Array.Clear(array, 0, array.Length);
			set.Add(array);
		}
	}

	/// <summary>
	/// A factory class for creating <see cref="BlockMemoryStream"/> instances.
	/// </summary>
	/// <remarks>
	/// This factory exists to allow tuning the parameters of the constructed <see cref="BlockMemoryStream"/>,
	/// while using a shared buffer pool.
	/// </remarks>
	public sealed class BlockMemoryStreamFactory
	{
		const int DefaultBlockShift = 12; //4k
		const int DefaultInitialBufferCount = 8;

		/// <summary>
		/// A default factory instance used by the default <see cref="BlockMemoryStream"/> constructor.
		/// </summary>
		public static readonly BlockMemoryStreamFactory Default = new BlockMemoryStreamFactory(new FixedArrayPool<byte>(1 << DefaultBlockShift), DefaultBlockShift, DefaultInitialBufferCount);

		readonly ArrayPool<byte> bufferPool;

		public BlockMemoryStreamFactory(
			ArrayPool<byte> arrayPool,
			int blockShift = DefaultBlockShift,
			int initialBufferCount = DefaultInitialBufferCount
		)
		{
			// 256 page size minimum.
			if (blockShift < 8) throw new ArgumentOutOfRangeException(nameof(blockShift));

			this.BlockShift = blockShift;
			this.BlockSize = 1 << blockShift;
			this.InitialBufferCount = initialBufferCount;
			this.bufferPool = arrayPool;
		}

		public int BlockShift { get; private set; }
		public int BlockSize { get; private set; }
		public int InitialBufferCount { get; private set; }

		public BlockMemoryStream Create()
		{
			return new BlockMemoryStream(this);
		}

		public void Return(byte[] buffer)
		{
			bufferPool.Return(buffer);
		}

		public byte[] Rent()
		{
			return bufferPool.Rent(BlockSize);
		}
	}

	/// <summary>
	/// A memory-backed <see cref="Stream"/> implementation.
	/// </summary>
	/// <remarks>
	/// This class uses pooled buffers to reduce allocations, and memory clearing
	/// that are present with <see cref="MemoryStream"/>.
	/// </remarks>
	public sealed class BlockMemoryStream : Stream
	{
		readonly BlockMemoryStreamFactory factory;

		long length;
		long position;

		byte[]?[] blocks;

		/// <summary>
		/// Creates a BlockMemoryStream using the default <see cref="BlockMemoryStreamFactory"/
		/// </summary>
		public BlockMemoryStream() : this(BlockMemoryStreamFactory.Default)
		{
		}

		public BlockMemoryStream(BlockMemoryStreamFactory factory)
		{
			this.factory = factory;
			this.blocks = new byte[]?[factory.InitialBufferCount];
		}

		public override bool CanRead => true;

		public override bool CanSeek => true;

		public override bool CanWrite => true;

		public override long Length => length;

		public override long Position
		{
			get
			{
				return position;
			}
			set
			{
				this.Seek(value, SeekOrigin.Begin);
			}
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (count < 0)
				throw new ArgumentOutOfRangeException(nameof(count));
			if (offset + count > buffer.Length)
				throw new ArgumentOutOfRangeException();


			var shift = factory.BlockShift;
			var blockMask = ~(~0 << shift);
			var blockSize = factory.BlockSize;


			var avail = this.length - this.position;
			var c = (int)(avail < count ? avail : count);
			var len = c;
			var pos = this.position;
			while (c > 0)
			{
				var blockIdx = pos >> shift;
				var curBlock = blocks[blockIdx];
				var blockOffset = (int)(pos & blockMask);
				var blockRem = blockSize - blockOffset;
				Debug.Assert(blockRem >= 0);
				var cl = blockRem < c ? blockRem : c;
				if (curBlock == null)
				{
					Array.Clear(buffer, offset, cl);
				}
				else
				{
					Buffer.BlockCopy(curBlock, blockOffset, buffer, offset, cl);
				}

				pos = pos + c;
				offset += cl;
				c -= cl;
			}

			this.position = pos;
			return len;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			long pos = 0;
			switch (origin)
			{
				case SeekOrigin.Begin:
					pos = offset;
					break;
				case SeekOrigin.Current:
					pos = this.position + offset;
					break;
				case SeekOrigin.End:
					pos = this.length + offset;
					break;
			}
			if (pos < 0 || pos > this.length)
				throw new ArgumentOutOfRangeException(nameof(offset));
			this.position = pos;
			return pos;
		}

		public override void SetLength(long value)
		{
			if (value < 0) throw new ArgumentOutOfRangeException();

			if (value < this.length)
			{
				long blocks = length >> factory.BlockShift;
				long newBlocks = value >> factory.BlockShift;

				// if the stream shrunk, return any unused blocks
				for (long i = newBlocks; i <= blocks && i < this.blocks.Length; i++)
				{
					var buffer = this.blocks[i];
					if (buffer != null)
					{
						this.blocks[i] = null;
						this.factory.Return(buffer);
					}
					this.length = value;
				}
			}

			this.length = value;
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			if (count < 0)
				throw new ArgumentOutOfRangeException();
			if (offset + count > buffer.Length)
				throw new ArgumentOutOfRangeException();

			var shift = factory.BlockShift;
			var blockMask = ~(~0 << shift);
			var blockSize = factory.BlockSize;

			var endLength = this.position + count;
			var reqBlockCount = (endLength + (int)blockMask) >> shift;

			var blocks = this.blocks;
			if (reqBlockCount > blocks.Length)
			{
				var newBlockCount = blocks.Length;
				while (newBlockCount < reqBlockCount)
				{
					newBlockCount <<= 1;
				}

				var newBuffers = new byte[]?[newBlockCount];
				Array.Copy(blocks, 0, newBuffers, 0, blocks.Length);
				this.blocks = newBuffers;
			}

			blocks = this.blocks;
			var pos = this.position;
			while (count > 0)
			{
				var blockIdx = pos >> shift;
				var curBlock = blocks[blockIdx];
				if (curBlock == null)
				{
					curBlock = factory.Rent();
					blocks[blockIdx] = curBlock;
				}
				var blockOffset = (int)(pos & blockMask);
				var blockRem = blockSize - blockOffset;
				Debug.Assert(blockRem >= 0);
				var c = blockRem < count ? blockRem : count;
				Buffer.BlockCopy(buffer, offset, curBlock, blockOffset, c);
				count -= c;
				pos = pos + c;
				offset += c;
			}
			this.position = (long)pos;
			if (this.position > this.length)
				this.length = this.position;
		}

		protected override void Dispose(bool disposing)
		{
			foreach (var block in this.blocks)
			{
				if (block != null)
					factory.Return(block);
			}
		}
	}
}
