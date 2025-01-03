using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;


public class TelnetClient : IDisposable
{
    private readonly int _port;
    private readonly string _host;
    private readonly TimeSpan _sendRate;
    private readonly SemaphoreSlim _sendRateLimit;
    private readonly CancellationTokenSource _internalCancellation;

    private TcpClient _tcpClient;
    private StreamReader _tcpReader;
    private StreamWriter _tcpWriter;

    public EventHandler<string> MessageReceived;
    public EventHandler ConnectionClosed;

    /// <summary>
    /// Simple telnet client
    /// </summary>
    /// <param name="host">Destination Hostname or IP</param>
    /// <param name="port">Destination TCP port number</param>
    /// <param name="sendRate">Minimum time span between sends. This is a throttle to prevent flooding the server.</param>
    /// <param name="token"></param>
    public TelnetClient(string host, int port, TimeSpan sendRate, CancellationToken token)
    {
        _host = host;
        _port = port;
        _sendRate = sendRate;
        _sendRateLimit = new SemaphoreSlim(1);
        _internalCancellation = new CancellationTokenSource();

        token.Register(() => _internalCancellation.Cancel());
    }

    /// <summary>
    /// Connect and wait for incoming messages. 
    /// When this task completes you are connected. 
    /// You cannot call this method twice; if you need to reconnect, dispose of this instance and create a new one.
    /// </summary>
    /// <returns></returns>
    public async Task Connect()
    {
        if (_tcpClient != null)
        {
            throw new NotSupportedException($"{nameof(Connect)} aborted: Reconnecting is not supported. You must dispose of this instance and instantiate a new TelnetClient");
        }

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(_host, _port);

        _tcpReader = new StreamReader(_tcpClient.GetStream());
        _tcpWriter = new StreamWriter(_tcpClient.GetStream()) { AutoFlush = true };

        // Fire-and-forget looping task that waits for messages to arrive
        WaitForMessage();
    }

    /// <summary>
    /// Connect via SOCKS4 proxy. See http://en.wikipedia.org/wiki/SOCKS#SOCKS4.
    /// When this task completes you are connected. 
    /// You cannot call this method twice; if you need to reconnect, dispose of this instance and create a new one.
    /// </summary>
    /// <param name="socks4ProxyHost"></param>
    /// <param name="socks4ProxyPort"></param>
    /// <param name="socks4ProxyUser"></param>
    /// <returns></returns>
    public async Task Connect(string socks4ProxyHost, int socks4ProxyPort, string socks4ProxyUser)
    {
        if (_tcpClient != null)
        {
            throw new NotSupportedException($"{nameof(Connect)} aborted: Reconnecting is not supported. You must dispose of this instance and instantiate a new TelnetClient");
        }

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(socks4ProxyHost, socks4ProxyPort);

        // Simple implementation of http://en.wikipedia.org/wiki/SOCKS#SOCKS4
        // Similar to http://biko.codeplex.com/
        byte[] hostAddress = Dns.GetHostAddresses(_host).First(a => a.AddressFamily == AddressFamily.InterNetwork).GetAddressBytes();
        byte[] hostPort = new byte[2]; // 16-bit number
        hostPort[0] = Convert.ToByte(_port / 256);
        hostPort[1] = Convert.ToByte(_port % 256);
        byte[] proxyUserId = Encoding.ASCII.GetBytes(socks4ProxyUser ?? string.Empty); // Can't pass in null

        // Request
        // - We build a "please connect me" packet to send to the proxy
        byte[] proxyRequest = new byte[9 + proxyUserId.Length];

        proxyRequest[0] = 4; // SOCKS4;
        proxyRequest[1] = 0x01; // Connect (we don't support Bind);

        hostPort.CopyTo(proxyRequest, 2);
        hostAddress.CopyTo(proxyRequest, 4);
        proxyUserId.CopyTo(proxyRequest, 8);

        proxyRequest[8 + proxyUserId.Length] = 0x00; // UserId terminator

        // Send proxy request
        // - Then we wait for an ack
        // - If successful, we can use the TelnetClient directly and traffic will be proxied
        await _tcpClient.GetStream().WriteAsync(proxyRequest, 0, proxyRequest.Length, _internalCancellation.Token);

        // Response
        // - First byte is null
        // - Second byte is our result code (we want 0x5a Request granted)
        // - Last 6 bytes should be ignored
        byte[] proxyResponse = new byte[8];

        // Wait for proxy response
        await _tcpClient.GetStream().ReadAsync(proxyResponse, 0, proxyResponse.Length, _internalCancellation.Token);

        if (proxyResponse[1] != 0x5a) // Request granted
        {
            switch (proxyResponse[1])
            {
                case 0x5b:
                    throw new InvalidOperationException("Proxy connect request rejected or failed");
                case 0x5c:
                    throw new InvalidOperationException("Proxy connect request failed because client is not running identd (or not reachable from the server)");
                case 0x5d:
                    throw new InvalidOperationException("Proxy connect request failed because client's identd could not confirm the user ID string in the request");
                default:
                    throw new InvalidOperationException("Proxy connect request failed, unknown error occured");
            }
        }

        _tcpReader = new StreamReader(_tcpClient.GetStream());
        _tcpWriter = new StreamWriter(_tcpClient.GetStream()) { AutoFlush = true };

        // Fire-and-forget looping task that waits for messages to arrive
        WaitForMessage();
    }

    public async Task Send(string message)
    {
        try
        {
            // Wait for any previous send commands to finish and release the semaphore
            // This throttles our commands
            await _sendRateLimit.WaitAsync(_internalCancellation.Token);

            // Send command + params
            await _tcpWriter.WriteLineAsync(message);

            // Block other commands until our timeout to prevent flooding
            await Task.Delay(_sendRate, _internalCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // We're waiting to release our semaphore, and someone cancelled the task on us (I'm looking at you, WaitForMessages...)
            // This happens if we've just sent something and then disconnect immediately
            Trace.TraceInformation($"{nameof(Send)} aborted: {nameof(_internalCancellation.IsCancellationRequested)} == true");
        }
        catch (ObjectDisposedException)
        {
            // This happens during ReadLineAsync() when we call Disconnect() and close the underlying stream
            // This is an expected exception during disconnection if we're in the middle of a send
            Trace.TraceInformation($"{nameof(Send)} failed: {nameof(_tcpWriter)} or {nameof(_tcpWriter.BaseStream)} disposed");
        }
        catch (IOException)
        {
            // This happens when we start WriteLineAsync() if the socket is disconnected unexpectedly
            Trace.TraceError($"{nameof(Send)} failed: Socket disconnected unexpectedly");
            throw;
        }
        catch (Exception error)
        {
            Trace.TraceError($"{nameof(Send)} failed: {error}");
            throw;
        }
        finally
        {
            // Exit our semaphore
            _sendRateLimit.Release();
        }
    }

    private async Task WaitForMessage()
    {
        try
        {
            while (true)
            {
                if (_internalCancellation.IsCancellationRequested)
                {
                    Console.WriteLine($"{nameof(WaitForMessage)} aborted: {nameof(_internalCancellation.IsCancellationRequested)} == true");
                    break;
                }

                string message;

                try
                {
                    if (!_tcpClient.Connected)
                    {
                        Console.WriteLine($"{nameof(WaitForMessage)} aborted: {nameof(_tcpClient)} is not connected");
                        break;
                    }

                    // Due to CR/LF platform differences, we sometimes get empty messages if the server sends us over-eager EOL markers
                    // Because ReadLine*() strips out the EOL characters, the message can end up empty (but not null!)
                    // I *think* this is a server implementation problem rather than our problem to solve
                    // So just handle empty messages in your consumer library
                    message = await _tcpReader.ReadLineAsync();

                    if (message == null)
                    {
                        Console.WriteLine($"{nameof(WaitForMessage)} aborted: {nameof(_tcpReader)} reached end of stream");
                        break;
                    }
                }
                catch (ObjectDisposedException)
                {
                    // This happens during ReadLineAsync() when we call Disconnect() and close the underlying stream
                    // This is an expected exception during disconnection
                    Console.WriteLine($"{nameof(WaitForMessage)} aborted: {nameof(_tcpReader)} or {nameof(_tcpReader.BaseStream)} disposed. This is expected after calling Disconnect()");
                    break;
                }
                catch (IOException)
                {
                    // This happens when we start ReadLineAsync() if the socket is disconnected unexpectedly
                    Console.WriteLine($"{nameof(WaitForMessage)} aborted: Socket disconnected unexpectedly");
                    break;
                }
                catch (Exception error)
                {
                    Console.WriteLine($"{nameof(WaitForMessage)} aborted: {error}");
                    break;
                }

                var msgLength = message.Length;

                //Console.WriteLine($"{nameof(WaitForMessage)} received: {message}");

                OnMessageReceived(message);
            }
        }
        finally
        {
            Console.WriteLine($"{nameof(WaitForMessage)} completed: Calling {nameof(Disconnect)}");
            Disconnect();
        }
    }

    /// <summary>
    /// Disconnecting will leave TelnetClient in an unusable state.
    /// </summary>
    public void Disconnect()
    {
        try
        {
            // Blow up any outstanding tasks
            _internalCancellation.Cancel();

            // Both reader and writer use the TcpClient.GetStream(), and closing them will close the underlying stream
            // So closing the stream for TcpClient is redundant
            // But it means we're triple sure!
            _tcpReader?.Close();
            _tcpWriter?.Close();
            _tcpClient?.Close();
        }
        catch (Exception error)
        {
            Trace.TraceError($"{nameof(Disconnect)} error: {error}");
        }
        finally
        {
            OnConnectionClosed();
        }
    }

    private void OnMessageReceived(string message)
    {
        EventHandler<string> messageReceived = MessageReceived;

        if (messageReceived != null)
        {
            messageReceived(this, message);
        }
    }

    private void OnConnectionClosed()
    {
        EventHandler connectionClosed = ConnectionClosed;

        if (connectionClosed != null)
        {
            connectionClosed(this, new EventArgs());
        }
    }

    private bool _disposed = false;


    public void Dispose()
    {
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Disconnect();
        }

        _disposed = true;
    }
}