using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgingMonarch
{

	public class SerialPortIdleException : Exception
	{
		readonly int _IdleTimeout;
		public SerialPortIdleException(int idleTimeout)
		{
			_IdleTimeout = idleTimeout;
		}

		public override string Message
		{
			get
			{
				return String.Format("The serial host had no activity in {0} seconds.", _IdleTimeout);
			}
		}
	}

    public class SerialPortRestartException : Exception
    {
		readonly SerialError _Error;
		readonly string _PortName;

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
	    DateTime _PendingTimeout = DateTime.MinValue;

        readonly byte[] _SerialBuffer = new byte[0xFF];

        // set once at construction, no need to lock
        readonly Thread _WorkerThread;
		readonly Action<string> _ProcessData;
		readonly string _PortName;
		readonly int _BaudRate;
		readonly Parity _Parity;
		readonly int _DataBits;
		readonly StopBits _StopBits;
		readonly Action<Exception> _ErrorLogger;
	    readonly int _IdleSeconds;

		readonly object _Lock = new object();

        public SerialHost(
            Action<string> processData,
            string portName = "COM1",
            int baudRate = 9600,
            Parity parity = Parity.None,
            int dataBits = 8,
            StopBits stopBits = StopBits.One,
            Action<Exception> errorLogger = null,
			int idleSeconds = 0 // no idle timeout
			)
        {
            _PortName = portName;
            _BaudRate = baudRate;
            _Parity = parity;
            _DataBits = dataBits;
            _StopBits = stopBits;
            _ErrorLogger = errorLogger;
            _ProcessData = processData;
	        _IdleSeconds = idleSeconds;

            _WorkerThread = new Thread(new ThreadStart(Run));
            _WorkerThread.Start();
        }

		public void Write(string text)
		{

			try
			{
				lock (_Lock)
				{
					if (_Run)
					{
						VerifySerial();
						_SerialPort.Write(text);
					}
				}
			}
			catch (Exception exc)
			{
				LogError(exc);
			}
		}


	    public void WriteLine(string text = "")
		{
			Write(text + Environment.NewLine);
		}

        public void Dispose()
        {
            _Run = false;
            Task.Factory.StartNew(async () =>
            {
	            if (!_WorkerThread.Join(5000))
				    _WorkerThread.Abort();

                await Close();
	        });
        }

		public void DiscardInBuffer()
		{
			lock (_Lock)
			{
				if (_SerialPort != null && _SerialPort.IsOpen)
					_SerialPort.DiscardInBuffer();
			}
		}

        async Task Close()
        {
            SerialPort closeMe = null;
            //Close Serial port
            lock (_Lock)
            {
                if (_SerialPort != null)
                {
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
                        GC.ReRegisterForFinalize(closeMe.BaseStream);
                    }
                    catch { }

                    try
                    {
                        if (closeMe.IsOpen)
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
					if (_IdleSeconds != 0 && _PendingTimeout != DateTime.MinValue && DateTime.Now > _PendingTimeout)
					{
						LogError(new SerialPortIdleException(_IdleSeconds));
						_PendingTimeout = DateTime.MinValue;
					}

					var sb = new StringBuilder();
					lock (_Lock)
					{
						if (!_Run)
							return;

						VerifySerial();
						
						try
						{
							int bytesReceived;
							while ((bytesReceived = _SerialPort.Read(_SerialBuffer, 0, _SerialBuffer.Length)) != 0)
								sb.Append(Encoding.ASCII.GetString(_SerialBuffer, 0, bytesReceived));
						}
						catch (TimeoutException)
						{
						}
					}

					if (sb.Length > 0)
					{
						_PendingTimeout = DateTime.Now.AddSeconds(_IdleSeconds);
						_ProcessData(sb.ToString());
					}
					else
					{
						if (_Run)
							Thread.Sleep(100);
					}
                }
                catch (Exception exc)
                {
                    LogError(exc);
					if (_Run)
						Thread.Sleep(5000);
                }				
            }
        }

        private void VerifySerial()
        {
            if (_SerialPort == null)
            {
                _SerialPort = new SerialPort(_PortName, _BaudRate, _Parity, _DataBits, _StopBits);                
                _SerialPort.ErrorReceived += SerialPortErrorReceived;
                _SerialPort.ReadTimeout = 1;
            }

            if (!_SerialPort.IsOpen)
            {
                _SerialPort.Open();
				GC.SuppressFinalize(_SerialPort.BaseStream);
            }
        }

        private async void SerialPortErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            if (_Run)
            {
                LogError(new SerialPortRestartException(e.EventType, _PortName));
                await Close();
            }
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
