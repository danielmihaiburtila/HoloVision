using UnityEngine;
using System.Collections;

public enum MenuState { MainMenu, StartSubMenu, SoundSettings }

public class HoloVisionBrain : MonoBehaviour
{
    [Header("Dependencies")]
    public AzureTTS tts;
    public AzureOCR ocr;
    public AzureSceneDescription vision;

    private MenuState currentState = MenuState.MainMenu;

    void Start()
    {
        // Întâmpinăm utilizatorul imediat ce aplicația se deschide
        StartCoroutine(WelcomeRoutine());
    }

    private IEnumerator WelcomeRoutine()
    {
        yield return new WaitForSeconds(1.0f);
        SpeakMainMenu();
    }

    // --- LOGICA DE NAVIGARE ---

    public void ProcessCommand(string cmd)
    {
        cmd = cmd.ToLower();
        Debug.Log("[Brain] Procesez: " + cmd);

        switch (currentState)
        {
            case MenuState.MainMenu:
                HandleMainMenu(cmd);
                break;
            case MenuState.StartSubMenu:
                HandleStartMenu(cmd);
                break;
            case MenuState.SoundSettings:
                HandleSoundMenu(cmd);
                break;
        }
    }

    private void HandleMainMenu(string cmd)
    {
        if (cmd.Contains("start") || cmd.Contains("porneste"))
        {
            currentState = MenuState.StartSubMenu;
            tts.Speak("Ai intrat în meniul Start. Opțiunile sunt: Citire text sau Detecție obiecte. Ce dorești?");
        }
        else if (cmd.Contains("sunet") || cmd.Contains("audio"))
        {
            currentState = MenuState.SoundSettings;
            tts.Speak("Meniu sunet. Spune 'Test' pentru probă sau 'Înapoi'.");
        }
        else if (cmd.Contains("parasire") || cmd.Contains("inchide") || cmd.Contains("stop"))
        {
            tts.Speak("Închiderea programului. La revedere!");
            Invoke("QuitApp", 2f);
        }
        else
        {
            SpeakMainMenu(); // Repetăm opțiunile dacă nu a înțeles
        }
    }

    private void HandleStartMenu(string cmd)
    {
        if (cmd.Contains("citire") || cmd.Contains("text") || cmd.Contains("lectura"))
        {
            ocr.ReadText(); // Aici Alina va zice singură "Citesc textul..."
        }
        else if (cmd.Contains("obiecte") || cmd.Contains("detectie") || cmd.Contains("vizualizare"))
        {
            vision.AnalyzeScene(); // Aici Alina va zice singură "Analizez scena..."
        }
        else if (cmd.Contains("inapoi") || cmd.Contains("meniu"))
        {
            currentState = MenuState.MainMenu;
            SpeakMainMenu();
        }
    }

    private void HandleSoundMenu(string cmd)
    {
        if (cmd.Contains("test"))
        {
            tts.Speak("Test de sunet reușit. Vocea Alinei este calibrată.");
        }
        else if (cmd.Contains("inapoi"))
        {
            currentState = MenuState.MainMenu;
            SpeakMainMenu();
        }
    }

    // --- AJUTĂTOARE ---

    private void SpeakMainMenu()
    {
        tts.Speak("Meniu principal. Opțiunile sunt: Start, Sunet, sau Părăsire program. Ce dorești?");
    }

    private void QuitApp() => Application.Quit();
}