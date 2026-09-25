using System.Net;
using System.Text;

namespace Station.Testing;

public sealed class LocalHttpServer : IAsyncDisposable
{
    private readonly Dictionary<string, ResponseDefinition> responses =
        new Dictionary<string, ResponseDefinition>( StringComparer.OrdinalIgnoreCase );

    private readonly CancellationTokenSource
        cancellationTokenSource = new CancellationTokenSource();

    private readonly HttpListener listener = new HttpListener();
    private Task? serverTask;

    public LocalHttpServer()
    {
        Port = GetFreePort();
        listener.Prefixes.Add( $"http://127.0.0.1:{Port}/" );
    }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public void AddJson( string path, string json ) =>
        responses[NormalizePath( path )] = new ResponseDefinition(
            "application/json",
            Encoding.UTF8.GetBytes( json )
        );

    public void AddFile(
        string path,
        byte[] content,
        string contentType = "application/octet-stream"
    ) => responses[NormalizePath( path )] = new ResponseDefinition( contentType, content );

    public void Start()
    {
        listener.Start();
        serverTask = Task.Run( ServeAsync );
    }

    public async ValueTask DisposeAsync()
    {
        cancellationTokenSource.Cancel();
        if ( listener.IsListening )
            listener.Stop();

        if ( serverTask is not null )
            try
            {
                await serverTask;
            }
            catch ( HttpListenerException )
            {
            }
            catch ( OperationCanceledException )
            {
            }

        listener.Close();
        cancellationTokenSource.Dispose();
    }

    private async Task ServeAsync()
    {
        while ( !cancellationTokenSource.IsCancellationRequested )
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch ( HttpListenerException )
            {
                break;
            }
            catch ( ObjectDisposedException )
            {
                break;
            }

            var path = NormalizePath( context.Request.Url?.AbsolutePath ?? "/" );
            if ( responses.TryGetValue( path, out var response ) )
            {
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = response.ContentType;
                context.Response.ContentLength64 = response.Content.Length;
                await context.Response.OutputStream.WriteAsync(
                    response.Content,
                    cancellationTokenSource.Token
                );
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            }

            context.Response.Close();
        }
    }

    private static string NormalizePath( string path ) => path.StartsWith( '/' ) ? path : "/" + path;

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener( IPAddress.Loopback, 0 );
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record ResponseDefinition( string ContentType, byte[] Content );
}