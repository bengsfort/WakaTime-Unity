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
    using System.Threading;
    using System.Collections;
    using System.Collections.Generic;
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

        // @TODO: Don't store this. Should probably instead just store the active
        // one and only get the full list of projects when showing the preferences.
        /// <summary>
        /// The list of the current users projects.
        /// </summary>
        public static ProjectSchema[] Projects
        {
            get
            {
                var json = EditorPrefs.GetString("WakaTime_Projects", "[]");
                return JsonUtility.FromJson<ProjectSchema[]>(json);
            }
            set
            {
                var json = JsonUtility.ToJson(value);
                EditorPrefs.SetString("WakaTime_Projects", json);
            }
        }

        /// <summary>
        /// The project to log time against.
        /// </summary>
        public static int ActiveProject
        {
            get
            {
                return EditorPrefs.GetInt("WakaTime_ActiveProject", 0);
            }
            set
            {
                EditorPrefs.SetInt("WakaTime_ActiveProject", value);
            }
        }

        /// <summary>
        /// Are we currently retrieving projects?
        /// </summary>
        private static bool s_RetrievingProjects;
        #endregion

        static WakaTime()
        {
            Debug.Log("<WakaTime> Constructor being called, yo!");

            if (!Enabled)
                return;
            
            // Update prefs?
            EditorApplication.update += OnUpdate;
            EditorApplication.playmodeStateChanged += OnPlaymodeStateChanged;
        }

        #region EventHandlers
        /// <summary>
        /// Callback that fires every frame.
        /// </summary>
        static void OnUpdate()
        {
            AsyncHelper.Execute();
        }

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
            var result = JsonUtility.FromJson<ResponseSchema<CurrentUserSchema>>(auth.downloadHandler.text);
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

        /// <summary>
        /// Gets the list of the current users projects from the WakaTime API.
        /// </summary>
        static void GetUserProjects()
        {
            if (!ApiKeyValidated)
                return;
            
            s_RetrievingProjects = true;
            var www = UnityWebRequest.Get(ApiBase + "users/current/projects?api_key=" + ApiKey);

            // Enqueue handling of the request
            AsyncHelper.Add(new RequestEnumerator(www.Send(), () =>
            {
                var result = JsonUtility.FromJson<ResponseSchema<ProjectSchema[]>>(www.downloadHandler.text);

                Debug.Log(result.ToString());
                // If the result returned an error, the key is likely no good
                if (result.error != null)
                {
                    Debug.LogError("<WakaTime> Failed to get projects from WakaTime API.");
                }
                else
                {
                    Debug.Log("<WakaTime> Successfully retrieved project list from WakaTime API.");

                    foreach(ProjectSchema project in result.data)
                    {
                        Debug.Log(project.ToString());
                    }

                    Projects = result.data;
                }

                // s_RetrievingProjects = false;
            }));
        }
        #endregion

        #region ViewHelpers
        static string[] GetProjectDropdownOptions()
        {
            var projects = Projects;
            var options = new List<string>();
            // Initialize a request to get the projects if there are none
            if (projects.Length == 0 && !s_RetrievingProjects)
            {
                GetUserProjects();
            }

            options.Add("Choose a project");
            foreach(ProjectSchema project in projects)
            {
                options.Add(project.name);
            }

            return options.ToArray();
        }

        #endregion

        #region PreferencesView
        /// <summary>
        /// Render function for the preferences view.
        /// </summary>
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

            if (githubButton)
                Application.OpenURL(GithubRepo);

            EditorGUILayout.Separator();

            // Main integration settings
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Integration Settings", EditorStyles.boldLabel);
            
            // Don't show the rest of the items if its not even enabled
            Enabled = EditorGUILayout.BeginToggleGroup("Enable WakaTime", Enabled);

            // API Key field
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("API Key (" + (ApiKeyValidated ? "Validated" : "Invalid") + ")");
            EditorGUILayout.BeginVertical();
            var newApiKey = EditorGUILayout.PasswordField(ApiKey);
            var validateKeyButton = GUILayout.Button("Validate key");
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            // Project selection
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Active Project");
            ActiveProject = EditorGUILayout.Popup(ActiveProject, GetProjectDropdownOptions());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndToggleGroup();

            EditorGUILayout.EndVertical();

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

        #region HelperClasses
        public class RequestEnumerator
        {
            public IEnumerator status;

            AsyncOperation m_Request;

            Action m_Callback;

            public IEnumerator Start()
            {
                while (!m_Request.isDone)
                {
                    // Not done yet..
                    yield return null;
                }
                // It's done!
                m_Callback();
            }

            public RequestEnumerator(AsyncOperation req, Action cb)
            {
                m_Request = req;
                m_Callback = cb;
                status = Start();
            }
        }

        public static class AsyncHelper
        {
            private static readonly Queue<RequestEnumerator> s_Requests = new Queue<RequestEnumerator>();

            public static void Add(RequestEnumerator action)
            {
                lock(s_Requests)
                {
                    s_Requests.Enqueue(action);
                }
            }

            public static void Execute()
            {
                if (s_Requests.Count > 0)
                {
                    RequestEnumerator action = null;

                    lock(s_Requests)
                    {
                        action = s_Requests.Dequeue();
                    }

                    // Re-queue the action if it is not complete
                    if (action.status.MoveNext())
                    {
                        Add(action);
                    }
                }
            }
        }
        #endregion

        #region ApiResponseSchemas

        /// <summary>
        /// User query API response object.
        /// </summary>
        [Serializable]
        public struct ResponseSchema<T>
        {
            public string error;
            public T data;

            public override string ToString()
            {
                var dataStr = "";
                return
                    "UserResponseObject:\n" +
                    "\terror: " + error + "\n" +
                    "\tdata: " + data.ToString();
            }
        }

        /// <summary>
        /// User schema from WakaTime API.
        /// </summary>
        [Serializable]
        public struct CurrentUserSchema
        {
            public string username;
            public string display_name;
            public string full_name;
            public string id;
            public string photo;

            public override string ToString()
            {
                return
                    "CurrentUserSchema:\n" +
                    "\tusername: " + username + "\n" +
                    "\tdisplay_name: " + display_name + "\n" +
                    "\tfull_name: " + full_name + "\n" +
                    "\tid: " + id + "\n" +
                    "\tphoto: " + photo;
            }
        }

        [Serializable]
        public struct ProjectSchema
        {
            public string id;
            public string name;

            public override string ToString()
            {
                return "ProjectSchema:\n" +
                    "\tid: " + id + "\n" + 
                    "\tname: " + name + "\n";
            }
        }
        #endregion
    }


}