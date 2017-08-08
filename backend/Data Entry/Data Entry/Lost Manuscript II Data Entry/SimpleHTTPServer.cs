using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Threading;
using System.Diagnostics;

using Newtonsoft.Json.Linq;
using Dialogue_Data_Entry;

// See: https://gist.github.com/aksakalli/9191056
class SimpleHTTPServer
{
	private static IDictionary<string, string> _mimeTypeMappings = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase) {
		#region extension to MIME type list
		{".asf", "video/x-ms-asf"},
		{".asx", "video/x-ms-asf"},
		{".avi", "video/x-msvideo"},
		{".bin", "application/octet-stream"},
		{".cco", "application/x-cocoa"},
		{".crt", "application/x-x509-ca-cert"},
		{".css", "text/css"},
		{".deb", "application/octet-stream"},
		{".der", "application/x-x509-ca-cert"},
		{".dll", "application/octet-stream"},
		{".dmg", "application/octet-stream"},
		{".ear", "application/java-archive"},
		{".eot", "application/octet-stream"},
		{".exe", "application/octet-stream"},
		{".flv", "video/x-flv"},
		{".gif", "image/gif"},
		{".hqx", "application/mac-binhex40"},
		{".htc", "text/x-component"},
		{".htm", "text/html"},
		{".html", "text/html"},
		{".ico", "image/x-icon"},
		{".img", "application/octet-stream"},
		{".iso", "application/octet-stream"},
		{".jar", "application/java-archive"},
		{".jardiff", "application/x-java-archive-diff"},
		{".jng", "image/x-jng"},
		{".jnlp", "application/x-java-jnlp-file"},
		{".jpeg", "image/jpeg"},
		{".jpg", "image/jpeg"},
		{".js", "application/x-javascript"},
		{".mml", "text/mathml"},
		{".mng", "video/x-mng"},
		{".mov", "video/quicktime"},
		{".mp3", "audio/mpeg"},
		{".mpeg", "video/mpeg"},
		{".mpg", "video/mpeg"},
		{".msi", "application/octet-stream"},
		{".msm", "application/octet-stream"},
		{".msp", "application/octet-stream"},
		{".pdb", "application/x-pilot"},
		{".pdf", "application/pdf"},
		{".pem", "application/x-x509-ca-cert"},
		{".pl", "application/x-perl"},
		{".pm", "application/x-perl"},
		{".png", "image/png"},
		{".prc", "application/x-pilot"},
		{".ra", "audio/x-realaudio"},
		{".rar", "application/x-rar-compressed"},
		{".rpm", "application/x-redhat-package-manager"},
		{".rss", "text/xml"},
		{".run", "application/x-makeself"},
		{".sea", "application/x-sea"},
		{".shtml", "text/html"},
		{".sit", "application/x-stuffit"},
		{".swf", "application/x-shockwave-flash"},
		{".tcl", "application/x-tcl"},
		{".tk", "application/x-tcl"},
		{".txt", "text/plain"},
		{".war", "application/java-archive"},
		{".wbmp", "image/vnd.wap.wbmp"},
		{".wmv", "video/x-ms-wmv"},
		{".xml", "text/xml"},
		{".xpi", "application/x-xpinstall"},
		{".zip", "application/zip"},
		#endregion
	};
	private Thread _serverThread;
	private string _rootDirectory;
	private HttpListener _listener;
	private int _port;

	private static bool initialized = false;

	private QueryHandler handler;

	private Action<string> chatBoxCallback; //for writing messages to console
	private Action<string> form1loadcallback; //for triggering a file load


	public int Port
	{
		get { return _port; }
		private set { }
	}

	/// <summary>
	/// Construct server with given port.
	/// </summary>
	/// <param name="path">Directory path to serve.</param>
	/// <param name="port">Port of the server.</param>
	public SimpleHTTPServer(string path, int port, QueryHandler handler, Action<string> chatBoxCallback, Action<string> form1loadcallback)
	{
		this.handler = handler;
		this.Initialize(path, port);
		this.chatBoxCallback = chatBoxCallback;
		this.form1loadcallback = form1loadcallback;
	}

	/// <summary>
	/// Stop server and dispose all functions.
	/// </summary>
	public void Stop()
	{
		_serverThread.Abort();
		_listener.Stop();
		initialized = false;
	}

	private void Listen()
	{
		if (initialized) {
			chatBoxCallback("Server already running!\n");
			return;
		}

		initialized = true;
		string listener_prefix = "http://*:" + _port.ToString() + "/";
		if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
			//Give permission to the listener's URL
			//Full argument is: netsh http add urlacl url=[listener_prefix here] user=DOMAIN\user
			string arguments = "http add urlacl url=" + listener_prefix + " user=DOMAIN\\user";
			ProcessStartInfo start_info = new ProcessStartInfo("netsh", arguments);
			start_info.RedirectStandardOutput = true;
			start_info.UseShellExecute = false;
			start_info.CreateNoWindow = true;
			Process.Start(start_info);
		}

		try {
			_listener = new HttpListener();
			_listener.Prefixes.Add("http://*:" + _port.ToString() + "/");
			_listener.Start();

		}
		catch (System.Net.HttpListenerException) {
			_listener = new HttpListener();
			_listener.Prefixes.Add("http://localhost:" + _port.ToString() + "/");
			_listener.Start();
			chatBoxCallback("Error with listener: You must give permission to listen on port " + _port + 
				". You can either run this in administrator mode or run the following in command prompt: \"netsh http add urlacl url=" +
				listener_prefix + " user=DOMAIN\\user\". Running on localhost instead.\n");
		}
		
		while (true)
		{
			try
			{
				HttpListenerContext context = _listener.GetContext();
				HTTPProcess(context);
			}
			catch (Exception ex)
			{
				break;
			}
		}
	}

	private void HTTPProcess(HttpListenerContext context)
	{

		string filename = context.Request.Url.AbsolutePath;

		//string body = new StreamReader(context.Request.InputStream).ReadToEnd();

		string body = "";
		using (Stream receiveStream = context.Request.InputStream) {
			using (StreamReader readStream = new StreamReader(receiveStream, Encoding.UTF8)) {
				body = readStream.ReadToEnd();
			}
		}

		Console.WriteLine("body: " + body);
		if (string.IsNullOrEmpty(body)) {
			body = "{}";
		}

		byte[] b;
		dynamic data = JObject.Parse(body);

		string response = "{}";

		switch (filename) {
			case "/chronology":
				// Get the data from the HTTP stream
				
				chatBoxCallback("<CHRONOLOGY REQUEST: " + context.Request.RemoteEndPoint + ">\n");

				string query = "CHRONOLOGY:" + data.id + ":" + data.turns;

                chatBoxCallback("query: " + query + "\n");

				response = handler.ParseInputJSON(query);

				//dynamic response = new JObject();
				//split and ignore last empty one
				//response.sequence = new JArray(result.Split(new string[] { "::" }, StringSplitOptions.None).Reverse().Skip(1).Reverse().ToArray());

				//write response
				b = Encoding.UTF8.GetBytes(response.ToString());
				context.Response.StatusCode = (int)HttpStatusCode.OK;
				context.Response.KeepAlive = false;
				context.Response.ContentLength64 = b.Length;
				context.Response.OutputStream.Write(b, 0, b.Length);
				context.Response.OutputStream.Close();

				break;
            case "/add_to_chronology":
                // Get the data from the HTTP stream

                chatBoxCallback("<ADD TO CHRONOLOGY REQUEST: " + context.Request.RemoteEndPoint + ">\n");

                query = "ADD_TO_CHRONOLOGY:" + data.id + ":" + data.turns;
                response = handler.ParseInputJSON(query);

                //dynamic response = new JObject();
                //split and ignore last empty one
                //response.sequence = new JArray(result.Split(new string[] { "::" }, StringSplitOptions.None).Reverse().Skip(1).Reverse().ToArray());

                //write response
                b = Encoding.UTF8.GetBytes(response.ToString());
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.KeepAlive = false;
                context.Response.ContentLength64 = b.Length;
                context.Response.OutputStream.Write(b, 0, b.Length);
                context.Response.OutputStream.Close();
                break;
            case "/chronology/reset":
				chatBoxCallback("<NARRATION RESET: " + context.Request.RemoteEndPoint + ">\n");
				handler.ParseInputJSON("RESTART_NARRATION");
				b = Encoding.UTF8.GetBytes("null");
				context.Response.StatusCode = (int)HttpStatusCode.OK;
				context.Response.KeepAlive = false;
				context.Response.ContentLength64 = b.Length;
				context.Response.OutputStream.Write(b, 0, b.Length);
				context.Response.OutputStream.Close();
				break;

			case "/getgraph":

				response = handler.ParseInput("GET_GRAPH");

				//write response
				b = Encoding.UTF8.GetBytes(response.ToString());
				context.Response.StatusCode = (int)HttpStatusCode.OK;
				context.Response.KeepAlive = false;
				context.Response.ContentLength64 = b.Length;
				context.Response.OutputStream.Write(b, 0, b.Length);
				context.Response.OutputStream.Close();

				break;

			case "/test":
				response = handler.ParseInputJSON("test_sequence");

				//write response
				b = Encoding.UTF8.GetBytes(response.ToString());
				context.Response.StatusCode = (int)HttpStatusCode.OK;
				context.Response.KeepAlive = false;
				context.Response.ContentLength64 = b.Length;
				context.Response.OutputStream.Write(b, 0, b.Length);
				context.Response.OutputStream.Close();

				break;


			case "/load_xml":
                
                chatBoxCallback("<LOAD XML REQUEST: " + context.Request.RemoteEndPoint + ">\n");
                query = "load_xml:" + data.url;
                response = handler.ParseInputJSON(query);
                string command_file_name = data.url;
                this.form1loadcallback(command_file_name);
                //handler.parent_form1.OpenXML(command_file_name);

                b = Encoding.UTF8.GetBytes(response.ToString());
                context.Response.StatusCode = (int)HttpStatusCode.OK;
				context.Response.KeepAlive = false;
				context.Response.ContentLength64 = b.Length;
				context.Response.OutputStream.Write(b, 0, b.Length);
				context.Response.OutputStream.Close();


				//handler.ParseInputJSON("load_xml:" + data.file);
				//Console.WriteLine("aaaaaaaaaaaaaaa");
				//handler.parent_form1.OpenXML(data.file);
				//this.form1loadcallback(data.url);//data.file);
				//Console.WriteLine("wqewqewqewqe");



				
				break;


			case "/":

				//maybe list the options here?

				break;
		}
	}

	private void Initialize(string path, int port)
	{
		this._rootDirectory = path;
		this._port = port;
		_serverThread = new Thread(this.Listen);
		_serverThread.Start();
	}


}