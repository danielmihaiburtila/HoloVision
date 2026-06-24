using System;
using System.Collections.Generic;
using UnityEngine;

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
using UnityEngine.Windows.WebCam;
#endif

public class LightBillModeHL2 : MonoBehaviour
{
    [Header("Dependencies")]
    public AzureTTS tts;
    public SpeechUIAnimator ui;

    [Header("Sampling")]
    [Tooltip("La cate secunde ia o captura pentru luminanta.")]
    public float sampleInterval = 0.8f;

    [Tooltip("Rezolutie mica = rapid, suficient pentru luminanta.")]
    public int requestWidth = 424;
    public int requestHeight = 240;

    [Header("Thresholds (tune on device)")]
    [Range(0f, 1f)] public float darkThreshold = 0.18f;
    [Range(0f, 1f)] public float brightThreshold = 0.30f;

    [Tooltip("Histerezis ca sa nu 'clipeasca' verdictul.")]
    [Range(0.01f, 0.20f)] public float hysteresis = 0.04f;

    [Header("Beep feedback (optional)")]
    public bool beepEnabled = false;
    public AudioSource beepSource;
    public AudioClip beepClip;

    public bool IsRunning => _running;

    private bool _running = false;
    private float _nextSampleAt = 0f;

    private enum LightState { Unknown, Dark, Dim, Bright }
    [Header("User-friendly stability")]
    [Tooltip("Câte cadre consecutive trebuie să confirme aceeași stare înainte de a vorbi.")]
    public int stableSamplesNeeded = 3;

    [Tooltip("După primul verdict stabil, oprește automat modul ca să nu țină camera ocupată.")]
    public bool autoStopAfterFirstStableAnswer = true;

    private readonly Queue<float> _lumHistory = new Queue<float>();
    private LightState _pendingState = LightState.Unknown;
    private int _pendingCount = 0;
    private LightState _state = LightState.Unknown;

    [Header("Interaction instructions")]
    public bool preferPhoneControlInstructions = false;

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
    private PhotoCapture _photoCapture;
    private CameraParameters _camParams;
    private bool _captureBusy = false;
#endif

    private void Awake()
    {
        if (tts == null) tts = FindObjectOfType<AzureTTS>(true);
        if (ui == null) ui = FindObjectOfType<SpeechUIAnimator>(true);
        if (beepSource == null) beepSource = GetComponent<AudioSource>();
    }

    public void StartMode()
    {
        if (_running)
        {
            StopMode(false);
            return;
        }

        _running = true;
        _state = LightState.Unknown;
        _nextSampleAt = Time.time;
        _lumHistory.Clear();
        _pendingState = LightState.Unknown;
        _pendingCount = 0;

        ui?.ShowSuccess("Mod lumină pornit.");
        tts?.Speak("Am pornit modul pentru lumină. Voi analiza luminozitatea camerei și îți spun dacă este întuneric sau lumină.");

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
        StartCamera();
#else
        ui?.ShowError("Modul lumină funcționează doar pe device (UWP).");
#endif
    }

    public void StopMode(bool speak = true)
    {
        if (!_running) return;
        _running = false;

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
        StopCamera();
#endif

        ui?.ShowSuccess("Mod lumină oprit.");
        if (speak) tts?.Speak("Am oprit modul pentru lumină.");
    }

    private void Update()
    {
        if (!_running) return;

        if (Time.time >= _nextSampleAt)
        {
            _nextSampleAt = Time.time + Mathf.Max(0.2f, sampleInterval);
#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
            if (!_captureBusy)
                TakeSample();
#endif
        }
    }

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
    private void StartCamera()
    {
        _captureBusy = false;

        PhotoCapture.CreateAsync(false, captureObject =>
        {
            if (!_running) { captureObject?.Dispose(); return; }

            _photoCapture = captureObject;

            _camParams = new CameraParameters
            {
                hologramOpacity = 0.0f,
                cameraResolutionWidth = requestWidth,
                cameraResolutionHeight = requestHeight,
                pixelFormat = CapturePixelFormat.BGRA32
            };

            _photoCapture.StartPhotoModeAsync(_camParams, result =>
            {
                if (!result.success)
                {
                    ui?.ShowError("Nu pot porni camera pentru modul lumină.");
                    tts?.Speak("Nu pot porni camera pentru modul lumină.");
                    StopMode(false);
                    return;
                }
            });
        });
    }

    private void StopCamera()
    {
        if (_photoCapture == null) return;

        try
        {
            _photoCapture.StopPhotoModeAsync(result =>
            {
                try { _photoCapture?.Dispose(); } catch { }
                _photoCapture = null;
            });
        }
        catch
        {
            try { _photoCapture?.Dispose(); } catch { }
            _photoCapture = null;
        }
    }

    private void TakeSample()
    {
        if (_photoCapture == null) return;
        _captureBusy = true;

        _photoCapture.TakePhotoAsync(OnCapturedToMemory);
    }

    private void OnCapturedToMemory(PhotoCapture.PhotoCaptureResult result, PhotoCaptureFrame frame)
    {
        _captureBusy = false;
        if (!_running) return;

        if (!result.success || frame == null)
            return;

        var buffer = new List<byte>(requestWidth * requestHeight * 4);
        try
        {
            frame.CopyRawImageDataIntoBuffer(buffer);
        }
        catch { return; }

        float meanLum = ComputeMeanLuminanceBGRA(buffer);
        UpdateStateAndSpeak(meanLum);
    }

    private float ComputeMeanLuminanceBGRA(List<byte> bgra)
    {
        if (bgra == null || bgra.Count < 4) return 0f;

        long count = 0;
        double sum = 0;

        int step = 4 * 8;

        for (int i = 0; i + 3 < bgra.Count; i += step)
        {
            byte b = bgra[i + 0];
            byte g = bgra[i + 1];
            byte r = bgra[i + 2];

            double lum = (0.0722 * b + 0.7152 * g + 0.2126 * r) / 255.0;
            sum += lum;
            count++;
        }

        if (count <= 0) return 0f;
        return (float)(sum / count);
    }
#endif

    private void UpdateStateAndSpeak(float meanLum)
    {
        // Medie mobilă scurtă: reduce fluctuațiile camerei / auto-expunerea.
        _lumHistory.Enqueue(meanLum);
        while (_lumHistory.Count > Mathf.Max(1, stableSamplesNeeded))
            _lumHistory.Dequeue();

        float smoothLum = 0f;
        foreach (float v in _lumHistory)
            smoothLum += v;

        smoothLum /= Mathf.Max(1, _lumHistory.Count);

        LightState candidate;

        if (smoothLum <= darkThreshold)
            candidate = LightState.Dark;
        else if (smoothLum >= brightThreshold)
            candidate = LightState.Bright;
        else
            candidate = LightState.Dim;

        // Cerem aceeași stare de câteva ori la rând.
        if (candidate != _pendingState)
        {
            _pendingState = candidate;
            _pendingCount = 1;
            return;
        }

        _pendingCount++;

        if (_pendingCount < Mathf.Max(1, stableSamplesNeeded))
            return;

        if (candidate == _state)
            return;

        _state = candidate;

        if (_state == LightState.Dark)
        {
            ui?.ShowUI($"Luminozitate mică ({smoothLum:0.00}).");
            if (preferPhoneControlInstructions)
            {
                tts?.Speak("Camera pare întunecată. Pentru verificarea becului, apasă din nou pe opțiunea Factura curent din partea dreapta-jos a ecranului telefonului.");
            }
            else
            {
                tts?.Speak("Camera este întunecată.");
            }

        }
        else if (_state == LightState.Dim)
        {
            ui?.ShowUI($"Luminozitate intermediară ({smoothLum:0.00}).");
            if (preferPhoneControlInstructions)
            {
                tts?.Speak("Camera are lumină slabă sau intermediară. Pentru verificarea becului, apasă din nou pe opțiunea Factură curent de pe telefon.");
            }
            else
            {
                tts?.Speak("Camera are lumină slabă sau intermediară");
            }
        }
        else
        {
            ui?.ShowUI($"Luminozitate mare ({smoothLum:0.00}).");
            if (preferPhoneControlInstructions)
            {
                tts?.Speak("Camera pare luminată. Pentru verificarea becului, apasă din nou pe opțiunea Factura curent și lumină de pe telefon.");
            }
            else
            {
                tts?.Speak("Camera pare luminată. Dacă vrei să afli dacă becul este aprins, spune: verifică becul.");
            }
        }

        if (autoStopAfterFirstStableAnswer)
            StopMode(false);

        if (beepEnabled && beepSource != null && beepClip != null)
            beepSource.PlayOneShot(beepClip);
    }
}