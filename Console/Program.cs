
string hostname = "192.168.1.20";
int port = 23; // Telnet port or any TCP port
string username = "adminUser";
string password = "adminPassword";

try
{
    var TelnetClass = new TelnetClass();
    var telnetAgent = TelnetClass.Login(hostname, port);
    telnetAgent.ConnectionClosed += (ss, ee) => { Console.WriteLine("Closed"); };
    telnetAgent.MessageReceived += (ss, ee) => { Console.WriteLine($"{ee}"); };
    
    // Connect Socket
    await telnetAgent.Connect();
    Console.ReadLine();
    // Login
    await telnetAgent.Send(username);
    await telnetAgent.Send(password);

    // Running Commands
    await telnetAgent.Send("Ping 1.1.1.1"); // or some other command
    await telnetAgent.Send("Ping 8.8.8.8"); // or some other command

    // Stop Server
    TelnetClass.Logout(telnetAgent);
    telnetAgent.Disconnect();
}
catch (Exception ex)
{
    Console.WriteLine(ex.ToString());
}