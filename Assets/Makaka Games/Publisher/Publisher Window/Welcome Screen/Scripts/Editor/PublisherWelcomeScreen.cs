/*
===================================================================
Unity Assets by MAKAKA GAMES: https://makaka.org/o/all-unity-assets
===================================================================

Online Docs (Latest): https://makaka.org/unity-assets
Offline Docs: You have a PDF file in the package folder.

=======
SUPPORT
=======

First of all, read the docs. If it didn’t help, get the support.

Web: https://makaka.org/support
Email: info@makaka.org

If you find a bug or you can’t use the asset as you need, 
please first send email to info@makaka.org
before leaving a review to the asset store.

I am here to help you and to improve my products for the best.
*/

using System;

using UnityEditor;
using UnityEngine;

[HelpURL("https://makaka.org/unity-assets")]
[InitializeOnLoad]
public class PublisherWelcomeScreen : EditorWindow
{
	private static PublisherWelcomeScreen window;
	private static Vector2 headerSize = new(500f, 92f);
	private static Vector2 windowSize = new(500f, 310f);
	private Vector2 scrollPosition;

	private static readonly string windowHeaderText =
		"Unity Asset Store Publisher";

    private static readonly string iconHeaderName = "Icon Header";

    private static readonly string iconHeaderLink = "https://makaka.org";

    private readonly string copyright =
        "Copyright © " + DateTime.Now.Year + " Makaka Games  ";
	
	private const string isShowAtStartEditorPrefs = "WelcomeScreenShowAtStart";
	private static bool isShowAtStart = true;

	private static bool isInited;

	private static GUIStyle headerStyle;
	private static GUIStyle copyrightStyle;

    private static Texture2D iconDocs;
    private static readonly string iconDocsName = "Icon Docs";
    private static readonly string iconDocsLink =
		"https://makaka.org/unity-assets";
    private static readonly string iconDocsTitle =
        "Documentation — the latest docs online";
    private static readonly string iconDocsDescription =
		"You also have offline docs in the Package Folder.";

    private static Texture2D iconSupport;
    private static readonly string iconSupportName = "Icon Support";
	private static readonly string iconSupportLink =
		"https://makaka.org/support";
    private static readonly string iconSupportTitle =
		"Support — info@makaka.org";
    private static readonly string iconSupportDescription =
		"First of all, read the docs. If it didn't help, get the support.";

    private static Texture2D iconAssets;
    private static readonly string iconAssetsName = "Icon Map";
	private static readonly string iconAssetsLink =
		"https://makaka.org/o/map";
    private static readonly string iconAssetsTitle = "Map of Unity Assets";
    private static readonly string iconAssetsDescription =
        "How are my Assets related to each other? Explore the Map.";

    static PublisherWelcomeScreen()
	{
		EditorApplication.update -= GetShowAtStart;
		EditorApplication.update += GetShowAtStart;
	}

	private void OnGUI()
	{
		if (!isInited)
		{
			Init();
		}

		if (GUI.Button(new Rect(0f, 0f, headerSize.x, headerSize.y),
			string.Empty, headerStyle))
		{
			Application.OpenURL(iconHeaderLink);
		}

		GUILayoutUtility.GetRect(position.width, 110f);

		scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

		if (DrawButton(iconDocs, iconDocsTitle, iconDocsDescription))
		{
			Application.OpenURL(iconDocsLink);
		}

		if (DrawButton(iconSupport, iconSupportTitle, iconSupportDescription))
		{
			Application.OpenURL(iconSupportLink);
		}

		if (DrawButton(iconAssets, iconAssetsTitle, iconAssetsDescription))
		{
			Application.OpenURL(iconAssetsLink);
		}

		EditorGUILayout.EndScrollView();

		EditorGUILayout.LabelField(copyright, copyrightStyle);
    }

	private static bool Init()
	{
		try
		{
			headerStyle = new GUIStyle();
			headerStyle.normal.background =
				Resources.Load(iconHeaderName) as Texture2D;
			headerStyle.normal.textColor = Color.white;
			headerStyle.fontStyle = FontStyle.Bold;
			headerStyle.padding = new RectOffset(340, 0, 27, 0);
			headerStyle.margin = new RectOffset(0, 0, 0, 0);

			copyrightStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                alignment = TextAnchor.MiddleRight
            };

            iconDocs = Resources.Load(iconDocsName) as Texture2D;
			iconSupport = Resources.Load(iconSupportName) as Texture2D;
            iconAssets = Resources.Load(iconAssetsName) as Texture2D;

            isInited = true; 
		}
		catch (Exception e)
		{
			DebugPrinter.Print("WELCOME SCREEN INIT ERROR: " + e);

			return false;
		}

		return true;
	}

	private static bool DrawButton(
		Texture2D icon, string title, string description)
	{
		GUILayout.BeginHorizontal();

		GUILayout.Space(34f);
		GUILayout.Box(icon, GUIStyle.none,
			GUILayout.MaxWidth(48f), GUILayout.MaxHeight(48f));
		GUILayout.Space(10f);

		GUILayout.BeginVertical();

		GUILayout.Space(1f);
		GUILayout.Label(title, EditorStyles.boldLabel);
		GUILayout.Label(description);

		GUILayout.EndVertical();

		GUILayout.EndHorizontal();

		Rect rect = GUILayoutUtility.GetLastRect();
		EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

		GUILayout.Space(10f);

		return Event.current.type == EventType.MouseDown
			&& rect.Contains(Event.current.mousePosition);
	}

	private static void GetShowAtStart()
	{
		EditorApplication.update -= GetShowAtStart;
		
		isShowAtStart = EditorPrefs.GetBool(isShowAtStartEditorPrefs, true);

		if (isShowAtStart)
		{
			EditorApplication.update -= OpenAtStartup;
			EditorApplication.update += OpenAtStartup;
		}
	}

	private static void OpenAtStartup()
	{
		if (isInited && Init()) 
		{
			OpenWindow();

			EditorApplication.update -= OpenAtStartup;
		}
	}

	[MenuItem("Window/Makaka Games/Welcome Screen", false)]
	public static void OpenWindow()
	{
		if (window == null) 
		{
			window = GetWindow<PublisherWelcomeScreen>(
				true, windowHeaderText, true);

			window.maxSize = window.minSize = windowSize;
		}
	}

	private void OnEnable()
	{
		window = this;
	}

	private void OnDestroy()
	{
		window = null;

		EditorPrefs.SetBool(isShowAtStartEditorPrefs, false);
	}
}