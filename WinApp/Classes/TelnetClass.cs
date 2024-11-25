using System;
using System.Threading;
using TentacleSoftware.Telnet;

namespace Telnet
{
    class TelnetClass
    {
        public TelnetClient Login(string IP, int Port)
        {
            CancellationTokenSource _cancellationSource = new CancellationTokenSource();
            //CancellationToken token = _cancellationSource.Token;
            var _telnetClient = new TelnetClient(IP, Port, TimeSpan.FromSeconds(3), _cancellationSource.Token);
            return _telnetClient;//var going out
        }

        public string Logout(TelnetClient _telnetClient)
        {
            _telnetClient.Disconnect();
            _telnetClient.Dispose();
            return "DISCONNECTED!";//var going out
        }
    }
}
