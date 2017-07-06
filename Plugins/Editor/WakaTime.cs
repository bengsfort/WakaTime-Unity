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
    using System;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.Networking;
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

        /// <summary>
        /// The Github repository for the plugin.
        /// </summary>
        public const string GithubRepo = "https://github.com/bengsfort/WakaTime-Unity";

        /// <summary>
        /// The base URL for all API calls.
        /// </summary>
        public const string ApiBase = "https://wakatime.com/api/v1/";

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

        /// <summary>
        /// Has the saved API Key been validated?
        /// </summary>
        public static bool ApiKeyValidated
        {
            get
            {
                return EditorPrefs.GetBool("WakaTime_ValidApiKey", false);
            }
            set
            {
                EditorPrefs.SetBool("WakaTime_ValidApiKey", value);
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

        #region ApiCalls
        /// <summary>
        /// Validates an API key.
        /// </summary>
        /// <remarks>
        /// API Key is validated by sending a GET for the current user using the
        /// provided key. If it is a valid key, it shouldn't return an error.
        /// </remarks>
        /// <param name="key">The API key.</param>
        static void ValidateApiKey()
        {
            // If the user has deliberatly entered nothing, then reset the key
            if (ApiKey == "")
            {
                ApiKeyValidated = false;
                return;
            }

            // Initialize a GET request
            UnityWebRequest auth = UnityWebRequest.Get(ApiBase + "users/current?api_key=" + ApiKey);
            var req = auth.Send();

            // Display a progress bar while the request is active
            while (!req.isDone)
            {
                EditorUtility.DisplayProgressBar(
                    "WakaTime Api Key Validation",
                    "Validating your Api Key...",
                    req.progress
                );
            }
            // Clear the progress bar and parse the result
            EditorUtility.ClearProgressBar();
            var result = JsonUtility.FromJson<UserResponseObject>(auth.downloadHandler.text);
            Debug.Log(result.ToString());
            // If the result returned an error, the key is likely no good
            if (result.error != null)
            {
                Debug.LogError("<WakaTime> Oh no! Couldn't validate the supplied Api Key. Try another one?");
                ApiKeyValidated = false;
            }
            else
            {
                // @todo: Store the information returned somewhere. Maybe the
                // name + photo can be displayed in prefs? dunno.
                Debug.Log("<WakaTime> Validated Api Key! You're good to go.");
                ApiKeyValidated = true;
            }
        }
        #endregion

        #region PreferencesView

        [PreferenceItem("WakaTime")]
        static void WakaTimePreferencesView()
        {
            if (EditorApplication.isCompiling)
            {
                EditorGUILayout.HelpBox(
                    "Hold up!\n" +
                    "Unity is compiling right now, so to prevent catastrophic " +
                    "failure of something you'll have to try again once it's done.",
                    MessageType.Warning
                );
                return;
            }

            // Plugin Meta
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox(string.Format("v{0:0.0} by @bengsfort", Version), MessageType.Info);
            var githubButton = GUILayout.Button("Github", GUILayout.Height(38));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Separator();

            // Main integration settings
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Integration Settings", EditorStyles.boldLabel);
            
            // Enabled checkbox
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Enable WakaTime");
            Enabled = GUILayout.Toggle(Enabled, "");
            EditorGUILayout.EndHorizontal();

            // API Key field
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("API Key (" + (ApiKeyValidated ? "Validated" : "Invalid") + ")");
            EditorGUILayout.BeginVertical();
            var newApiKey = EditorGUILayout.PasswordField(ApiKey);
            var validateKeyButton = GUILayout.Button("Validate key");
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            if (githubButton)
                Application.OpenURL(GithubRepo);

            if (GUI.changed)
            {
                // If the Api Key has changed, reset the validation
                if (newApiKey != ApiKey)
                {
                    ApiKey = newApiKey;
                    ApiKeyValidated = false;
                }

                if (validateKeyButton && !ApiKeyValidated)
                    ValidateApiKey();
            }
        }

        #endregion

        #region ApiResponseSchemas

        [Serializable]
        public struct UserResponseObject
        {
            public string error;
            public CurrentUserObject data;

            public override string ToString()
            {
                return
                    "UserResponseObject:\n" +
                    "\terror: " + error + "\n" +
                    "\tdata: " + data;
            }
        }

        [Serializable]
        public struct CurrentUserObject
        {
            public string username;
            public string display_name;
            public string full_name;
            public string id;
            public string photo;

            public override string ToString()
            {
                return
                    "CurrentUserObject:\n" +
                    "\tusername: " + username + "\n" +
                    "\tdisplay_name: " + display_name + "\n" +
                    "\tfull_name: " + full_name + "\n" +
                    "\tid: " + id + "\n" +
                    "\tphoto: " + photo;
            }
        }

        #endregion
    }


}