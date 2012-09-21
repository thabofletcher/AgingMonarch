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
                Console.WriteLine(text);
            });
        }
    }
}
