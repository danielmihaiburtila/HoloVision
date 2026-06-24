using System;
using System.Text;
using UnityEngine;

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.Threading.Tasks;
#endif

public class RemoteHttpCommandListenerHL2 : MonoBehaviour
{
    [Header("Router (existing)")]
    public VoiceCommandRouter router;

    [Header("Server")]
    public int port = 8080;

    [Header("Debug")]
    public bool logRequests = true;

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
    private StreamSocketListener _listener;
#endif

    private void Awake()
    {
        EnsureRouter();
    }

    private void OnEnable()
    {
#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
        _ = StartServer();
#else
        Debug.Log("[RemoteHttp] Runs only on device (UWP).");
#endif
    }

    private void OnDisable()
    {
#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
        StopServer();
#endif
    }

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
    private async Task StartServer()
    {
        try
        {
            StopServer();

            _listener = new StreamSocketListener();
            _listener.Control.KeepAlive = true;
            _listener.ConnectionReceived += OnConnection;

            await _listener.BindServiceNameAsync(port.ToString());
            Debug.Log("[RemoteHttp] Listening on port " + port);
        }
        catch (Exception e)
        {
            Debug.LogError("[RemoteHttp] StartServer error: " + e);
        }
    }

    private void StopServer()
    {
        try
        {
            if (_listener != null)
            {
                _listener.ConnectionReceived -= OnConnection;
                _listener.Dispose();
                _listener = null;
            }
        }
        catch { }
    }

  private async void OnConnection(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
{
    try
    {
        using (var input = args.Socket.InputStream)
        using (var reader = new DataReader(input))
        {
            reader.InputStreamOptions = InputStreamOptions.Partial;

            uint bytesLoaded = await reader.LoadAsync(4096);
            if (bytesLoaded == 0)
            {
                await WriteHttpResponse(args, 400, "Empty request");
                return;
            }

            string request = reader.ReadString(bytesLoaded);

            if (logRequests) Debug.Log("[RemoteHttp] Request:\n" + request);

            string[] lines = request.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
            {
                await WriteHttpResponse(args, 400, "Invalid request");
                return;
            }

            string firstLine = lines[0].Trim();
            string path = ExtractPathFromFirstLine(firstLine);

            if (string.IsNullOrEmpty(path))
            {
                await WriteHttpResponse(args, 404, "Not Found");
                return;
            }
if (path.StartsWith("/status", StringComparison.OrdinalIgnoreCase))
{
    if (!EnsureRouter())
    {
        Debug.LogWarning("[RemoteHttp] /status -> router missing");
        await WriteHttpResponse(args, 503, "ROUTER_MISSING");
        return;
    }

    string status = router.GetModeStatus();
    Debug.Log("[RemoteHttp] /status -> " + status);

    await WriteHttpResponse(args, 200, status);
    return;
}
    if (path.StartsWith("/user_name_status", StringComparison.OrdinalIgnoreCase))
{
    if (!EnsureRouter())
    {
        Debug.LogWarning("[RemoteHttp] /user_name_status -> router missing");
        await WriteHttpResponse(args, 503, "ROUTER_MISSING");
        return;
    }

    string savedName = router.GetSavedUserNameForPhone();
    string result = "MISSING";

    if (!string.IsNullOrWhiteSpace(savedName))
        result = "SAVED:" + savedName.Trim();

    Debug.Log("[RemoteHttp] /user_name_status -> " + result);

    await WriteHttpResponse(args, 200, result);
    return;
}

            if (!path.StartsWith("/command", StringComparison.OrdinalIgnoreCase))
            {
                await WriteHttpResponse(args, 404, "Not Found");
                return;
            }

            string cmd = GetQueryParam(path, "cmd");
            string arg = GetQueryParam(path, "arg");

            if (string.IsNullOrWhiteSpace(cmd))
            {
                await WriteHttpResponse(args, 400, "Missing cmd");
                return;
            }

            UnityEngine.WSA.Application.InvokeOnAppThread(() =>
            {
                HandleCommand(cmd, arg);
            }, false);

            await WriteHttpResponse(args, 200, "OK");
        }
    }
    catch (Exception e)
    {
        Debug.LogError("[RemoteHttp] OnConnection error: " + e);
        try { await WriteHttpResponse(args, 500, "Server error"); } catch { }
    }
}

    private static string ExtractPathFromFirstLine(string firstLine)
    {
        var parts = firstLine.Split(' ');
        if (parts.Length < 2) return null;
        return parts[1];
    }

    private static string GetQueryParam(string path, string key)
    {
        int q = path.IndexOf('?');
        if (q < 0) return null;

        string query = path.Substring(q + 1);
        var pairs = query.Split('&');

        foreach (var p in pairs)
        {
            var kv = p.Split(new[] { '=' }, 2);
            if (kv.Length == 2 && string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kv[1].Replace('+', ' '));
            }
        }
        return null;
    }

    private async Task WriteHttpResponse(StreamSocketListenerConnectionReceivedEventArgs args, int code, string body)
    {
        string status = code == 200 ? "OK" :
                        code == 400 ? "Bad Request" :
                        code == 404 ? "Not Found" : "Internal Server Error";

        string payload = body ?? "";
        string resp =
            $"HTTP/1.1 {code} {status}\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(payload)}\r\n" +
            "Connection: close\r\n\r\n" +
            payload;

        using (var output = args.Socket.OutputStream)
        using (var writer = new DataWriter(output))
        {
            writer.WriteString(resp);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }
    }
#endif

   
    private void HandleCommand(string cmdRaw, string arg)
    {
        if (!EnsureRouter())
        {
            Debug.LogError("[RemoteHttp] Router missing.");
            return;
        }

        string c = (cmdRaw ?? "").Trim().ToUpperInvariant();

        Debug.Log("[RemoteHttp] Phone command: " + c + " arg=" + arg);

        if (c == "STOP")
        {
            router.StopAllFromGesture("Am oprit.", speakConfirmation: true);
            return;
        }

        switch (c)
        {
            case "START":
                router.AcceptPhoneCommand("start", "start");
                break;

            case "DESCRIERE":
                router.AcceptPhoneCommand("descriere", "descriere");
                break;

            case "TEXT":
                router.AcceptPhoneCommand("text", "text");
                break;

            case "SCAN_PRODUCT":
                router.AcceptPhoneCommand("scaneaza produs", "scaneaza produs");
                break;

            case "FACTURA_CURENT":
                router.AcceptPhoneCommand("factura curent", "factura curent");
                break;
            case "CHECK_BULB":
            case "VERIFICA_BECUL":
                router.AcceptPhoneCommand("verifica becul", "verifica becul");
                break;

            case "FIND":
                if (string.IsNullOrWhiteSpace(arg))
                {
                    router.AcceptPhoneCommand("cauta", "cauta");
                }
                else
                {
                    string phrase = "cauta " + arg.Trim();
                    router.AcceptPhoneCommand(phrase, phrase);
                }
                break;
            case "PREPARE_PHONE_FIND":
                router.PrepareForPhoneFind();
                break;
            case "PHONE_NAME_CAPTURE_START":
                router.PrepareForPhoneNameCapture();
                break;
            case "PHONE_NAME":
                if (!string.IsNullOrWhiteSpace(arg))
                {
                    string payload = "PHONE_NAME:" + arg.Trim();
                    router.AcceptPhoneCommand(payload, payload);
                }
                else
                {
                    Debug.LogWarning("[RemoteHttp] PHONE_NAME fără arg.");
                }
                break;

            case "ARM_SOS":
                if (router.azureTTS != null)
                {
                    router.azureTTS.StopNow();
                    router.azureTTS.Speak("Dorești să trimit o alertă de urgență. Scutură încă o dată telefonul pentru confirmare.");
                }
                break;

            case "SOS":
            case "AJUTOR":
                if (router.sos != null)
                {
                    router.sos.TriggerImmediateHelp("phone_shake");
                }
                else
                {
                    router.AcceptPhoneCommand("ajutor", "ajutor");
                }
                break;

            case "SET_MODE_VOCAL":
                router.ActivateVocalMode(speakConfirmation: true);
                break;

            case "SET_MODE_DISCREET":
                router.ActivateDiscreetMode(speakConfirmation: true);
                break;

            default:
                router.AcceptPhoneCommand(cmdRaw, cmdRaw);
                break;
        }
    }
    private bool EnsureRouter()
    {
        if (router == null)
            router = FindObjectOfType<VoiceCommandRouter>(true);

        return router != null;
    }


}