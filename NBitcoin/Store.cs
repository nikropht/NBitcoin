﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NBitcoin
{
	public abstract class Store<TStoredItem, TItem>
		where TStoredItem : StoredItem<TItem>
		where TItem : IBitcoinSerializable
	{
		public int MaxFileSize
		{
			get;
			set;
		}

		string _FilePrefix;
		public string FilePrefix
		{
			get
			{
				return _FilePrefix;
			}
			set
			{
				_FilePrefix = value;
				_FileRegex = null;
			}
		}

		Regex _FileRegex;
		public Regex FileRegex
		{
			get
			{
				if(_FileRegex == null)
				{
					_FileRegex = new Regex(FilePrefix + "([0-9]{5,5}).dat");
				}
				return _FileRegex;
			}
		}

		private readonly DirectoryInfo _Folder;
		public DirectoryInfo Folder
		{
			get
			{
				return _Folder;
			}
		}

		public Store(string folder, Network network)
			: this(new DirectoryInfo(folder), network)
		{

		}
		public Store(DirectoryInfo folder, Network network)
		{
			if(folder == null)
				throw new ArgumentNullException("folder");
			if(network == null)
				throw new ArgumentNullException("network");
			_Folder = folder;
			_Network = network;
		}
		private readonly Network _Network;
		public Network Network
		{
			get
			{
				return _Network;
			}
		}

		public IEnumerable<TStoredItem> Enumerate(DiskBlockPosRange range)
		{
			if(range == null)
				range = DiskBlockPosRange.All;
			using(CreateLock(FileLockType.Read))
			{
				foreach(var b in EnumerateFolder(range))
				{
					if(b.Header.Magic == Network.Magic)
						yield return b;
				}
			}
		}



		public IEnumerable<TStoredItem> Enumerate(Stream stream, uint fileIndex = 0, DiskBlockPosRange range = null)
		{
			if(range == null)
				range = DiskBlockPosRange.All;

			if(fileIndex < range.Begin.File || range.End.File < fileIndex)
				yield break;
			if(range.Begin.File < fileIndex)
				range = new DiskBlockPosRange(DiskBlockPos.Begin.OfFile(fileIndex), range.End);
			if(range.End.File > fileIndex)
				range = new DiskBlockPosRange(range.Begin, DiskBlockPos.End.OfFile(fileIndex));

			stream.Position = range.Begin.Position;
			while(stream.Position < stream.Length)
			{
				yield return ReadStoredItem(stream, new DiskBlockPos(fileIndex, (uint)stream.Position));
				if(stream.Position >= range.End.Position)
					break;
			}
		}

		public IEnumerable<TStoredItem> EnumerateFile(FileInfo file, uint fileIndex = 0, DiskBlockPosRange range = null)
		{
			if(range == null)
				range = DiskBlockPosRange.All;
			using(var fs = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				fs.Position = range.Begin.Position;
				foreach(var block in Enumerate(fs, fileIndex, range))
				{
					yield return block;
				}
			}
		}

		public IEnumerable<TStoredItem> EnumerateFile(string fileName, uint fileIndex = 0, DiskBlockPosRange range = null)
		{
			if(range == null)
				range = DiskBlockPosRange.All;
			return EnumerateFile(new FileInfo(fileName), fileIndex, range);
		}


		public IEnumerable<TStoredItem> EnumerateFolder(DiskBlockPosRange range = null)
		{
			if(range == null)
				range = DiskBlockPosRange.All;
			foreach(var file in _Folder.GetFiles().OrderBy(f => f.Name))
			{
				var fileIndex = GetFileIndex(file.Name);
				if(fileIndex < 0)
					continue;
				foreach(var block in EnumerateFile(file, (uint)fileIndex, range))
				{
					yield return block;
				}
			}
		}

		private int GetFileIndex(string fileName)
		{
			var match = FileRegex.Match(fileName);
			if(!match.Success)
				return -1;
			return int.Parse(match.Groups[1].Value);
		}

		public void Write(TStoredItem stored)
		{
			var fileName = string.Format(FilePrefix + "{0:00000}.dat", stored.BlockPosition.File);
			using(var fs = new FileStream(Path.Combine(Folder.FullName, fileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
			{
				fs.Position = stored.BlockPosition.Position;
				stored.ReadWrite(fs, true);
			}
		}

		public FileInfo CreateFile(int file)
		{
			var fileName = string.Format(FilePrefix + "{0:00000}.dat", file);
			var filePath = Path.Combine(_Folder.FullName, fileName);
			File.Create(filePath).Close();
			return new FileInfo(filePath);
		}

		public DiskBlockPos SeekEnd()
		{
			var highestFile = _Folder.GetFiles().OrderBy(f => f.Name).Where(f => GetFileIndex(f.Name) != -1).LastOrDefault();
			if(highestFile == null)
				return new DiskBlockPos(0, 0);
			using(var fs = new FileStream(highestFile.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				var index = (uint)GetFileIndex(highestFile.Name);
				return new DiskBlockPos(index, (uint)fs.Length);
			}
		}

		private FileLock CreateLock(FileLockType fileLockType)
		{
			return new FileLock(Path.Combine(Folder.FullName, "StoreLock"), fileLockType);
		}


		protected DiskBlockPos SeekEnd(FileLock @lock)
		{
			var end = @lock.GetString();
			if(!string.IsNullOrEmpty(end))
				try
				{
					return DiskBlockPos.Parse(end);
				}
				catch(FormatException)
				{
					return SeekEnd();
				}
			else
				return SeekEnd();
		}

		public DiskBlockPos Append(TItem item)
		{
			using(var @lock = CreateLock(FileLockType.ReadWrite))
			{
				DiskBlockPos position = SeekEnd(@lock);
				if(position.Position > MaxFileSize)
					position = new DiskBlockPos(position.File + 1, 0);
				var stored = CreateStoredItem(item,position);
				Write(stored);
				position = new DiskBlockPos(position.File, position.Position + stored.GetStorageSize());
				@lock.SetString(position.ToString());
				return stored.BlockPosition;
			}
		}

		public void AppendAll(IEnumerable<TItem> items)
		{
			foreach(var item in items)
			{
				Append(item);
			}
		}

		protected abstract TStoredItem CreateStoredItem(TItem item, DiskBlockPos position);
		protected abstract TStoredItem ReadStoredItem(Stream stream, DiskBlockPos pos);
	}
}
