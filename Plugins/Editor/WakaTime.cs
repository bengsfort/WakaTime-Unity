/*
 * Unity WakaTime Support
 *
 * WakaTime support for Unity.
 *
 * v0.1
 * Matt Bengston (@bengsfort) <bengston.matthew@gmail.com>
 */
namespace Bengsfort.Unity
{
    using UnityEngine;
    using UnityEditor;
    using UnityEditor.Callbacks;

    [InitializeOnLoad]
    public class WakaTime
    {
        /// <summary>
        /// The current plugin version.
        /// </summary>
        public const double Version = 0.1;

        /// <summary>
        /// The author of the plugin.
        /// </summary>
        public const string Author = "Matt Bengston (@bengsfort) <bengston.matthew@gmail.com>";

        public const string GithubRepo = "http://localhost/";

        #region properties
        /// <summary>
        /// Whether the plugin is enabled or not.
        /// </summary>
        public static bool Enabled
        {
            get
            {
                return EditorPrefs.GetBool("WakaTime_Enabled", false);
            }
            set
            {
                EditorPrefs.SetBool("WakaTime_Enabled", value);
            }
        }

        /// <summary>
        /// The users API Key.
        /// </summary>
        public static string ApiKey
        {
            get
            {
                return EditorPrefs.GetString("WakaTime_ApiKey", "");
            }
            set
            {
                EditorPrefs.SetString("WakaTime_ApiKey", value);
            }
        }
        #endregion

        static WakaTime()
        {
            Debug.Log("<WakaTime> Constructor being called, yo!");

            if (!Enabled)
                return;
            
            // Update prefs?
        }

        #region EventHandlers

        /// <summary>
        /// Detect when scripts are reloaded and restart heartbeats
        /// </summary>
        [DidReloadScripts()]
        static void OnScriptReload()
        {
            // We're back in the editor doing things! start new sesh?
            Debug.Log("<WakaTime> Back in the engine!");
        }

        /// <summary>
        /// Perhaps this can be used for more precise logging?
        /// </summary>
        static void OnPlaymodeStateChanged()
        {
            Debug.Log("<WakaTime> Play mode state has changed!");
            // Triggered by tapping into this event:
            // EditorApplication.playmodeStateChanged += OnPlaymodeStateChanged;
            if (Application.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                
            }
        }

        /// <summary>
        /// Unity Asset Open Callback. This gets called when an asset is opening.
        /// </summary>
        [OnOpenAsset]
        static bool OnOpenedAsset(int instanceID, int line)
        {
            Debug.Log("<WakaTime> Asset opening!");
            // Not using WakaTime? L8r
            if (!Enabled)
                return false;

            // @TODO: This could be used for logging?
            return false;
        }

        #endregion

        #region PreferencesView

        [PreferenceItem("WakaTime")]
        static void WakaTimePreferencesView()
        {
            // Plugin Meta
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox(string.Format("v{0:0.0} by @bengsfort", Version), MessageType.Info);
            var githubButton = GUILayout.Button("Github");

            if (githubButton)
                Application.OpenURL(GithubRepo);
        }

        #endregion
    }
}