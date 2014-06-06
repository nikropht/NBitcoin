﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NBitcoin
{
	public abstract class ObjectStream<T> : IDisposable where T : class, IBitcoinSerializable, new()
	{
		public IEnumerable<T> Enumerate()
		{
			T o = null;
			while((o = ReadNext()) != null)
			{
				yield return o;
			}
		}

		public T ReadNext()
		{
			var result = ReadNextCore();
			if(result == null)
			{
				if(!EOF)
					throw new InvalidProgramException("EOF should be true if there is no object left in the stream");
			}
			return result;
		}

		public void WriteNext(T obj)
		{
			if(obj == null)
				throw new ArgumentNullException("obj");
			if(!EOF)
				throw new InvalidOperationException("EOF should be true before writing more");
			WriteNextCore(obj);
		}

		public abstract void Rewind();
		protected abstract void WriteNextCore(T obj);
		protected abstract T ReadNextCore();

		public abstract bool EOF
		{
			get;
		}

		#region IDisposable Members

		public virtual void Dispose()
		{

		}

		#endregion
	}
}