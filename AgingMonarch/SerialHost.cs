using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgingMonarch
{
    public class SerialPortRestartException : Exception
    {
        SerialError _Error;
        string _PortName;

        public SerialPortRestartException(SerialError error, string portName)
        {
            _Error = error;
            _PortName = portName;
        }

        public override string Message
        {
            get
            {
                return String.Format("The serial host on {0} will be restarted due to: {1}", _PortName, _Error);
            }
        }
    }

    public class SerialHost : IDisposable
    {
        private volatile bool _Run = true;
        SerialPort _SerialPort = null;
        bool _SerialPortReady = true;
        byte[] _SerialBuffer = new byte[0xFF];

        // set once at construction, no need to lock
        Thread _WorkerThread;
        Action<string> _ProcessData;
        string _PortName;
        int _BaudRate;
        Parity _Parity;
        int _DataBits;
        StopBits _StopBits;
        Action<Exception> _ErrorLogger;

        object _Lock = new object();

        public SerialHost(
            Action<string> processData,
            string portName = "COM1",
            int baudRate = 9600,
            Parity parity = Parity.None,
            int dataBits = 8,
            StopBits stopBits = StopBits.One,
            Action<Exception> errorLogger = null)
        {
            _PortName = portName;
            _BaudRate = baudRate;
            _Parity = parity;
            _DataBits = dataBits;
            _StopBits = stopBits;
            _ErrorLogger = errorLogger;
            _ProcessData = processData;

            _WorkerThread = new Thread(new ThreadStart(Run));
            _WorkerThread.Start();
        }

        public void WriteLine(string line = "")
        {
            if (_Run)
            {
                try
                {
                    lock (_Lock)
                    {
                        VerifySerial();
                        _SerialPort.WriteLine(line);
                    }
                }
                catch (Exception exc)
                {
                    LogError(exc);
                }
            }
        }

        public void Dispose()
        {
            _Run = false;
            Close();
            Task.Factory.StartNew(() => _WorkerThread.Join());
        }

        async Task Close()
        {
            SerialPort closeMe = null;
            //Close Serial port
            lock (_Lock)
            {
                if (_SerialPort != null)
                {
                    // TODO : does this work?
                    closeMe = _SerialPort;
                    _SerialPort = null;
                }
            }

            await Task.Factory.StartNew(() =>
            {
                if (closeMe != null)
                {
                    try
                    {
                        closeMe.Close();
                    }
                    catch { }
                }
            });
        }

        private void Run()
        {
            while (_Run)
            {
                try
                {
                    string text;
                    lock (_Lock)
                    {
                        VerifySerial();
                        int bytesReceived = _SerialPort.Read(_SerialBuffer, 0, _SerialBuffer.Length);
                        text = Encoding.ASCII.GetString(_SerialBuffer, 0, bytesReceived);
                    }

                    _ProcessData(text);
                }
                catch (TimeoutException) { }
                catch (Exception exc)
                {
                    LogError(exc);
                    Thread.Sleep(5000);
                }
            }
        }

        private void VerifySerial()
        {
            if (_SerialPort == null)
            {
                _SerialPort = new SerialPort(_PortName, _BaudRate, _Parity, _DataBits, _StopBits);
                _SerialPort.PinChanged += serialInterface_PinChanged;
                _SerialPort.ErrorReceived += serialInterface_ErrorReceived;
                _SerialPort.ReadTimeout = 1000;
            }

            if (!_SerialPort.IsOpen)
            {
                _SerialPort.Open();
            }
        }

        private void serialInterface_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            if (_Run)
            {
                LogError(new SerialPortRestartException(e.EventType, _PortName));
                Close();
            }
        }

        private void serialInterface_PinChanged(object sender, SerialPinChangedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void LogError(Exception exc)
        {
            if (_ErrorLogger != null)
            {
                _ErrorLogger(exc);
            }
        }
    }
}
