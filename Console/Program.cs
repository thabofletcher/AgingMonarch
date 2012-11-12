using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgingMonarch;

namespace ConsoleDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            SerialHost host = new SerialHost((text) =>
            {
				byte[] received = Encoding.ASCII.GetBytes(text);
				foreach (byte r in received)
				{
					Console.Write(String.Format("{0:x2}, ", r));
				}                
            });

			Console.Read();
			host.Dispose();
        }
    }
}
