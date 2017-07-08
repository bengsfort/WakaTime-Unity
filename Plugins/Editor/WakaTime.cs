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
    using System.IO;
    using System.Diagnostics;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Networking;
    using UnityEditor;
    using UnityEditor.Callbacks;
    using UnityEditor.SceneManagement;

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
        /// Should version control be used?
        /// </summary>
        public static bool EnableVersionControl
        {
            get
            {
                return EditorPrefs.GetBool("WakaTime_GitEnabled", true);
            }
            set
            {
                EditorPrefs.SetBool("WakaTime_GitEnabled", value);
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

        /// <summary>
        /// The information of the user retrieved when validating the Api key.
        /// </summary>
        /// <remarks>
        /// This is currently unused. In the future it would be cool to have info
        /// on the preferences view so you get a visual confirmation you're
        /// correctly authenticated.
        /// </remarks>
        public static CurrentUserSchema User
        {
            get
            {
                var json = EditorPrefs.GetString("WakaTime_User", "{}");
                return JsonUtility.FromJson<CurrentUserSchema>(json);
            }
            set
            {
                var json = JsonUtility.ToJson(value);
                EditorPrefs.SetString("WakaTime_User", json);
            }
        }

        /// <summary>
        /// The project to log time against.
        /// </summary>
        public static ProjectSchema ActiveProject
        {
            get
            {
                var json = EditorPrefs.GetString("WakaTime_ActiveProject", "{}");
                return JsonUtility.FromJson<ProjectSchema>(json);
            }
            set
            {
                var json = JsonUtility.ToJson(value);
                EditorPrefs.SetString("WakaTime_ActiveProject", json);
            }
        }

        /// <summary>
        /// Array of the users current projects.
        /// </summary>
        private static ProjectSchema[] s_UserProjects = new ProjectSchema[0];

        /// <summary>
        /// The index of the currently active project in the array of projects.
        /// </summary>
        private static int s_ActiveProjectIndex;

        /// <summary>
        /// Are we currently retrieving projects?
        /// </summary>
        private static bool s_RetrievingProjects;
        #endregion

        static WakaTime()
        {
            UnityEngine.Debug.Log("<WakaTime> Constructor being called, yo!");
            SendHeartbeatPost();

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
            UnityEngine.Debug.Log("<WakaTime> Back in the engine!");
        }

        /// <summary>
        /// Perhaps this can be used for more precise logging?
        /// </summary>
        static void OnPlaymodeStateChanged()
        {
            UnityEngine.Debug.Log("<WakaTime> Play mode state has changed!");
            // Triggered by tapping into this event:
            if (Application.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode)
            {

            }
        }

        #endregion

        #region ApiCalls
        static string GetApiUrl(string path)
        {
            return ApiBase + path + "?api_key=" + ApiKey;
        }

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
            UnityWebRequest auth = UnityWebRequest.Get(GetApiUrl("users/current"));
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
            UnityEngine.Debug.Log(result.ToString());
            // If the result returned an error, the key is likely no good
            if (result.error != null)
            {
                UnityEngine.Debug.LogError("<WakaTime> Oh no! Couldn't validate the supplied Api Key. Try another one?");
                ApiKeyValidated = false;
            }
            else
            {
                UnityEngine.Debug.Log("<WakaTime> Validated Api Key! You're good to go.");
                User = result.data;
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
            var www = UnityWebRequest.Get(GetApiUrl("users/current/projects"));

            // Enqueue handling of the request
            AsyncHelper.Add(new RequestEnumerator(www.Send(), () =>
            {
                var result = JsonUtility.FromJson<ResponseSchema<ProjectSchema[]>>(www.downloadHandler.text);

                // If the result returned an error, the key is likely no good
                if (result.error != null)
                {
                    UnityEngine.Debug.LogError("<WakaTime> Failed to get projects from WakaTime API.");
                }
                else
                {
                    UnityEngine.Debug.Log("<WakaTime> Successfully retrieved project list from WakaTime API.");

                    foreach(ProjectSchema project in result.data)
                    {
                        if (Application.productName == project.name)
                        {
                            UnityEngine.Debug.Log("<WakaTime> Found a project with identical name to current Unity project; setting it to active.");
                            ActiveProject = project;
                        }
                    }

                    s_UserProjects = result.data;
                }

                s_RetrievingProjects = false;
            }));
        }

        static void SendHeartbeatPost()
        {
            if (!ApiKeyValidated)
                return;
            
            // Create our heartbeat
            var heartbeat = JsonUtility.ToJson(new HeartbeatSchema(
                Path.Combine(
                    Application.dataPath,
                    EditorSceneManager.GetActiveScene().path.Substring("Assets/".Length)
                ),
                false
            ));
            var www = UnityWebRequest.Post(
                GetApiUrl("users/current/heartbeats"),
                string.Empty
            );
            // Manually add an upload handler so the data isn't corrupted
            www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(heartbeat));
            // Set the content type to json since it defaults to www form data
            www.SetRequestHeader("Content-Type", "application/json");
            UnityEngine.Debug.Log(www.GetRequestHeader("Content-Type"));

            AsyncHelper.Add(new RequestEnumerator(www.Send(), () =>
            {
                UnityEngine.Debug.Log("result!");
                UnityEngine.Debug.Log(www.downloadHandler.text);
                var result = JsonUtility.FromJson<ResponseSchema<HeartbeatResponseSchema>>(www.downloadHandler.text);

                if (result.error != null)
                {
                    UnityEngine.Debug.LogError("<WakaTime> Failed to send heartbeat to WakaTime. :(\n" + result.error);
                }
                else
                {
                    UnityEngine.Debug.Log("Sent heartbeat to WakaTime!! :D");
                }
            }));
        }
        #endregion

        #region ViewHelpers
        static string[] GetProjectDropdownOptions()
        {
            var options = new List<string>();
            s_ActiveProjectIndex = 0; //

            // Initialize a request to get the projects if there are none
            if (s_UserProjects.Length == 0 && !s_RetrievingProjects)
            {
                GetUserProjects();
                options.Add("Retrieving projects...");
                return options.ToArray();
            }

            options.Add("Choose a project");
            for (int i = 0; i < s_UserProjects.Length; i++)
            {
                options.Add(s_UserProjects[i].name);
                if (ActiveProject.id == s_UserProjects[i].id)
                    s_ActiveProjectIndex = i + 1;
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
            var projects = GetProjectDropdownOptions();
            var projectSelection = EditorGUILayout.Popup(s_ActiveProjectIndex, projects);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndToggleGroup();
            EditorGUILayout.EndVertical();

            // Handle any changed data
            if (GUI.changed)
            {
                // Has the active project changed?
                if (s_ActiveProjectIndex != projectSelection)
                {
                    ActiveProject = s_UserProjects[projectSelection];
                }

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
        /// <summary>
        /// A container class for async request enumerators and their 'done' callbacks.
        /// </summary>
        public class RequestEnumerator
        {
            /// <summary>
            /// The initialized enumerator for this request.
            /// </summary>
            public IEnumerator status;

            /// <summary>
            /// The operation representing this request.
            /// </summary>
            AsyncOperation m_Request;

            /// <summary>
            /// The callback to be fired on completion of the request.
            /// </summary>
            Action m_Callback;

            /// <summary>
            /// Instantiates an enumerator that waits for the request to finish.
            /// </summary>
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

        /// <summary>
        /// A Helper class for dealing with async web calls since coroutines are
        /// not really an option in edit mode.
        /// </summary>
        public static class AsyncHelper
        {
            /// <summary>
            /// A queue of async request enumerators.
            /// </summary>
            private static readonly Queue<RequestEnumerator> s_Requests = new Queue<RequestEnumerator>();

            /// <summary>
            /// Adds a request enumerator to the queue.
            /// </summary>
            public static void Add(RequestEnumerator action)
            {
                lock(s_Requests)
                {
                    s_Requests.Enqueue(action);
                }
            }

            /// <summary>
            /// If there are queued requests, it will dequeue and fire them. If
            /// the request is not finished, it will be added back to the queue.
            /// </summary>
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

        #region ApiSchemas
        /// <summary>
        /// Generic API response object with configurable data type.
        /// </summary>
        [Serializable]
        public struct ResponseSchema<T>
        {
            public string error;
            public T data;

            public override string ToString()
            {
                return
                    "UserResponseObject:\n" +
                    "\terror: " + error + "\n" +
                    "\tdata: " + data.ToString();
            }
        }

        /// <summary>
        /// User schema from WakaTime API.
        /// </summary>
        /// <remarks>
        /// https://wakatime.com/developers#users
        /// </remarks>
        [Serializable]
        public struct CurrentUserSchema
        {
            public string username;
            public string display_name;
            public string full_name;
            public string id;
            public string photo;
            public string last_plugin; // used for debugging
            public string last_heartbeat; // used for debugging

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

        /// <summary>
        /// Project schema from WakaTime API.
        /// </summary>
        /// <remarks>
        /// https://wakatime.com/developers#projects
        /// </remarks>
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

        /// <summary>
        /// Heartbeat response schema from WakaTime API.
        /// </summary>
        /// <remarks>
        /// https://wakatime.com/developers#heartbeats
        /// </remarks>
        [Serializable]
        public struct HeartbeatResponseSchema
        {
            public string id;
            public string entity;
            public string type;
            public Int32 time;
        }

        /// <summary>
        /// Schema for heartbeat postdata.
        /// </summary>
        /// <remarks>
        /// https://wakatime.com/developers#heartbeats
        /// </remarks>
        [Serializable]
        public struct HeartbeatSchema
        {
            // default to current scene?
            public string entity;
            // type of entity (app)
            public string type;
            // unix epoch timestamp
            public Int32 time;
            // project name
            public string project;
            // version control branch
            public string branch;
            // language (unity)
            public string language;
            // is this triggered by saving a file?
            public bool is_write;
            // What plugin sent this (duh, it us \m/)
            public string plugin;

            public HeartbeatSchema(string file, bool save = false)
            {
                entity = (file == string.Empty ? "Unsaved Scene" : file);
                type = "app";
                time = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                project = ActiveProject.name;
                branch = GitHelper.branch;
                language = "Unity";
                is_write = save;
                plugin = "Unity-WakaTime";
            }
        }
        #endregion

        #region VersionControl
        static class GitHelper
        {
            public static string branch
            {
                get
                {
                    if (EnableVersionControl)
                        return GetCurrentBranch();
                    else
                        return "master";
                }
            }

        static string GetGitPath()
        {
            var pathVar = Process.GetCurrentProcess().StartInfo.EnvironmentVariables["PATH"];
            var pathSeparator = (Application.platform == RuntimePlatform.WindowsEditor ? ';' : ':');
            var paths = pathVar.Split(new char[] { pathSeparator });
            foreach (string path in paths)
            {
                if (File.Exists(Path.Combine(path, "git")))
                    return path;
            }
            return String.Empty;
        }

        static string GetCurrentBranch()
        {
            var path = GetGitPath();
            
            if (string.IsNullOrEmpty(path))
            {
                UnityEngine.Debug.LogError(
                    "<WakaTime> You don't have git installed. Disabling Version " +
                    "Control support for you. It can be re-enabled from the preferences."
                );
                EnableVersionControl = false;
                return "master";
            }

            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --abbrev-ref HEAD",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            var gitProcess = new Process();
            gitProcess.StartInfo = processInfo;
            
            string output = String.Empty;
            string error = String.Empty;
            try
            {
                gitProcess.Start();
                output = gitProcess.StandardOutput.ReadToEnd().Trim();
                error = gitProcess.StandardError.ReadToEnd().Trim();
                gitProcess.WaitForExit();
            }
            catch
            {
                // silence is golden
            }
            finally
            {
                if (gitProcess != null)
                {
                    gitProcess.Close();
                }
            }

            if (!string.IsNullOrEmpty(error))
            {
                UnityEngine.Debug.LogError(
                    "<WakaTime> There was an error getting your git branch. Disabling " +
                    "version control support. It can be re-enabled from the preferences."
                );
                EnableVersionControl = false;
                return "master";
            }
            
            return output;
        }
        }
        #endregion
    }


}