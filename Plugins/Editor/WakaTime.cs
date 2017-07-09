/*
 * Unity WakaTime Support
 * WakaTime logging for the Unity Editor.
 *
 * Version
 * v0.5
 * Author:
 * Matt Bengston @bengsfort <bengston.matthew@gmail.com>
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
    using UnityEngine.SceneManagement;
    using UnityEditor;
    using UnityEditor.Callbacks;
    using UnityEditor.SceneManagement;

    [InitializeOnLoad]
    public static class WakaTime
    {
        /// <summary>
        /// The current plugin version.
        /// </summary>
        public const double Version = 0.5;

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

        /// <summary>
        /// The URL to the latest plugin file.
        /// </summary>
        public const string PluginFile = "https://raw.githubusercontent.com/bengsfort/WakaTime-Unity/master/Plugins/Editor/WakaTime.cs";

        #region properties
        /// <summary>
        /// The minimum time required to elapse before sending normal heartbeats.
        /// </summary>
        public const Int32 HeartbeatBuffer = 120;

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

        /// <summary>
        /// The last heartbeat sent.
        /// </summary>
        private static HeartbeatResponseSchema s_LastHeartbeat;
        #endregion

        static WakaTime()
        {
            if (!Enabled)
                return;
            
            // Initialize with a heartbeat
            PostHeartbeat();
            
            // Frame callback
            EditorApplication.update += OnUpdate;
            LinkCallbacks();
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
            PostHeartbeat();
            // Relink all of our callbacks
            LinkCallbacks(true);
        }

        /// <summary>
        /// Send a heartbeat every time the user enters or exits play mode.
        /// </summary>
        static void OnPlaymodeStateChanged()
        {
            PostHeartbeat();
        }

        /// <summary>
        /// Send a heartbeat every time the user clicks on the context menu.
        /// </summary>
        static void OnPropertyContextMenu(GenericMenu menu, SerializedProperty property)
        {
            PostHeartbeat();
        }

        /// <summary>
        /// Send a heartbet everytime the hierarchy changes.
        /// </summary>
        static void OnHierarchyWindowChanged()
        {
            PostHeartbeat();
        }

        /// <summary>
        /// Send a heartbeat every time the scene is saved.
        /// </summary>
        static void OnSceneSaved(Scene scene)
        {
            PostHeartbeat(true);
        }

        /// <summary>
        /// Send a heartbeat every time a scene is opened.
        /// </summary>
        static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            PostHeartbeat();
        }

        /// <summary>
        /// Send a heartbeat every time a scene is closed.
        /// </summary>
        static void OnSceneClosing(Scene scene, bool removingScene)
        {
            PostHeartbeat();
        }

        /// <summary>
        /// Send a heartbeat every time a scene is created.
        /// </summary>
        static void OnSceneCreated(Scene scene, NewSceneSetup setup, NewSceneMode mode)
        {
            PostHeartbeat();
        }
        #endregion

        #region ApiCalls
        static string FormatApiUrl(string path)
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
        static void GetCurrentUser()
        {
            // If the user has deliberatly entered nothing, then reset the key
            if (ApiKey == "")
            {
                ApiKeyValidated = false;
                return;
            }

            // Initialize a GET request
            UnityWebRequest auth = UnityWebRequest.Get(FormatApiUrl("users/current"));
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
            
            // Parse the result
            var result = JsonUtility.FromJson<ResponseSchema<CurrentUserSchema>>(auth.downloadHandler.text);
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

            // Clear the progress bar
            EditorUtility.ClearProgressBar();
        }

        /// <summary>
        /// Gets the list of the current users projects from the WakaTime API.
        /// </summary>
        static void GetUserProjects()
        {
            if (!ApiKeyValidated)
                return;
            
            s_RetrievingProjects = true;
            var www = UnityWebRequest.Get(FormatApiUrl("users/current/projects"));
            var request = www.Send();

            // Wait until we've finished, but allow the user to cancel
            while (!request.isDone)
            {
                var cancelled = EditorUtility.DisplayCancelableProgressBar(
                    "WakaTime Api",
                    "Getting your projects from WakaTime...",
                    request.progress
                );

                // Abort the operation and return if the user cancelled.
                if (cancelled)
                {
                    s_RetrievingProjects = false;
                    www.Abort();
                    EditorUtility.ClearProgressBar();
                    return;
                }
            }

            // Parse the result
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
            EditorUtility.ClearProgressBar();
        }

        /// <summary>
        /// Sends a heartbeat to the WakaTime API.
        /// </summary>
        /// <param name="fromSave">Was this triggered from a save?</param>
        static void PostHeartbeat(bool fromSave = false)
        {
            if (!ApiKeyValidated)
                return;
            
            // Create our heartbeat
            // If the current scene is empty it's an unsaved scene; so don't
            // try to determine exact file position in that instance.
            var currentScene = EditorSceneManager.GetActiveScene().path;
            var heartbeat = new HeartbeatSchema(
                currentScene != string.Empty ? Path.Combine(
                    Application.dataPath,
                    currentScene.Substring("Assets/".Length)
                ) : string.Empty,
                fromSave
            );

            // If it hasn't been longer than the last heartbeat buffer, ignore if
            // the heartbeat isn't triggered by a save or the scene changing.
            if ((heartbeat.time - s_LastHeartbeat.time < HeartbeatBuffer) && !fromSave
                && (heartbeat.entity == s_LastHeartbeat.entity))
                return;

            var heartbeatJson = JsonUtility.ToJson(heartbeat);
            var www = UnityWebRequest.Post(
                FormatApiUrl("users/current/heartbeats"),
                string.Empty
            );
            // Manually add an upload handler so the data isn't corrupted
            www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(heartbeatJson));
            // Set the content type to json since it defaults to www form data
            www.SetRequestHeader("Content-Type", "application/json");
            // Send the request
            AsyncHelper.Add(new RequestEnumerator(www.Send(), () =>
            {
                var result = JsonUtility.FromJson<ResponseSchema<HeartbeatResponseSchema>>(www.downloadHandler.text);

                if (result.error != null)
                {
                    UnityEngine.Debug.LogError(
                        "<WakaTime> Failed to send heartbeat to WakaTime. If this " +
                        "continues, please disable the plugin and submit an issue " +
                        "on Github.\n" + result.error
                    );
                }
                else
                {
                    // UnityEngine.Debug.Log("Sent heartbeat to WakaTime");
                    s_LastHeartbeat = result.data;
                }
            }));
        }
        #endregion

        #region Helpers
        /// <summary>
        /// Subscribes the plugin event handlers to the editor events.
        /// </summary>
        /// <param name="clean">Should we remove old callbacks before linking?</param>
        static void LinkCallbacks(bool clean = false)
        {
            // Remove old callbacks before adding them back again
            if (clean)
            {
                // Scene modification callbacks
                EditorApplication.playmodeStateChanged -= OnPlaymodeStateChanged;
                EditorApplication.contextualPropertyMenu -= OnPropertyContextMenu;
                EditorApplication.hierarchyWindowChanged -= OnHierarchyWindowChanged;
                // Scene state callbacks
                EditorSceneManager.sceneSaved -= OnSceneSaved;
                EditorSceneManager.sceneOpened -= OnSceneOpened;
                EditorSceneManager.sceneClosing -= OnSceneClosing;
                EditorSceneManager.newSceneCreated -= OnSceneCreated;
            }
            // Scene modification callbacks
            EditorApplication.playmodeStateChanged += OnPlaymodeStateChanged;
            EditorApplication.contextualPropertyMenu += OnPropertyContextMenu;
            EditorApplication.hierarchyWindowChanged += OnHierarchyWindowChanged;
            // Scene state callbacks
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            EditorSceneManager.newSceneCreated += OnSceneCreated;
        }

        /// <summary>
        /// Get all of the projects from WakaTime and then return a list of their names.
        /// </summary>
        static string[] GetProjectDropdownOptions()
        {
            var options = new List<string>();
            s_ActiveProjectIndex = 0;

            // Initialize a request to get the projects if there are none
            if (s_UserProjects.Length == 0 && !s_RetrievingProjects)
                GetUserProjects();
            
            // If we are trying to get the projects, let the user know
            if (s_RetrievingProjects)
            {
                options.Add("Retrieving projects...");
                return options.ToArray();
            }

            // Add a default no-project option first
            options.Add("Choose a project");
            
            // Iterate through the projects and add the names to the list
            for (int i = 0; i < s_UserProjects.Length; i++)
            {
                options.Add(s_UserProjects[i].name);
                if (ActiveProject.id == s_UserProjects[i].id)
                    s_ActiveProjectIndex = i + 1;
            }

            return options.ToArray();
        }

        /// <summary>
        /// Pulls the latest version of the plugin from Github then injects it
        /// into the project.
        /// </summary>
        /// <remarks>
        /// This pulls the latest file than parses the top comment to determine
        /// the version. It could also send a GET to Github for the latest release
        /// than parse that, but then it would need to make yet another GET to
        /// download the new file; so I decided to go with this way instead.
        /// </remarks>
        static void UpdatePlugin()
        {
            // Retrieve the plugin file from Github.
            var www = UnityWebRequest.Get(PluginFile);
            www.Send();
            
            // Show a cancelable progress bar while we're downloading the file.
            while (!www.isDone)
            {
                var cancelled = EditorUtility.DisplayCancelableProgressBar(
                    "WakaTime Plugin",
                    "Checking for updates...",
                    www.downloadProgress
                );
                // If the user cancels, abort and clear the progress bar.
                if (cancelled)
                {
                    www.Abort();
                    EditorUtility.ClearProgressBar();
                    return;
                }
            }
            
            // Clear the progress bar
            EditorUtility.ClearProgressBar();

            // If we have an error, log it
            if (www.error != null)
            {
                UnityEngine.Debug.LogError(
                    "<WakaTime> There was an error when trying to check for updates." +
                    " Please try again or file an issue on GitHub.\n" + www.error
                );
                return;
            }

            // Split the file by new line characters to make it easier to find
            // the version number in the top comments.
            var githubFile = www.downloadHandler.text;
            var fileLines = githubFile.Split('\n');
            
            // If we don't even have 5 lines, something went haywire and we should exit
            if (fileLines.Length < 5)
                return;
            
            // Parse the github version from the comments
            double githubVersion = Version;
            double.TryParse(
                fileLines[5].Substring(fileLines[5].IndexOf('v') + 1),
                out githubVersion
            );
            
            // Thar be an update!
            if (githubVersion > Version)
            {
                var updateDialog = EditorUtility.DisplayDialog(
                    "WakaTime Update " + Version + " -> " + githubVersion,
                    "There's a shiny new version of the plugin available! Would " +
                    "you like to update?",
                    "Sure!", "Nah"
                );
                
                // Don't bother if the user doesn't want to update.
                if (!updateDialog)
                    return;
                
                // Get the path of the asset so we can overwrite it on disk with
                // the new version of the plugin.
                var assetGUID = AssetDatabase.FindAssets("t:Script WakaTime")[0];
                var assetPath = AssetDatabase.GUIDToAssetPath(assetGUID);
                var path = Application.dataPath + assetPath
                    .Substring("Assets".Length)
                    .Replace('/', System.IO.Path.DirectorySeparatorChar);

                // Is the file writable?
                var fileInfo = new FileInfo(path);
                fileInfo.IsReadOnly = false;

                // Write to the file
                File.WriteAllText(path, githubFile);

                // Force Unity to update the file
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }
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
            EditorGUILayout.BeginVertical();
            var githubButton = GUILayout.Button("Github");
            var updateButton = GUILayout.Button("Update");
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (githubButton)
                Application.OpenURL(GithubRepo);
            
            if (updateButton)
                UpdatePlugin();

            EditorGUILayout.Separator();

            // Main integration settings
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Integration Settings", EditorStyles.boldLabel);
            
            // Don't show the rest of the items if its not even enabled
            Enabled = EditorGUILayout.BeginToggleGroup("Enable WakaTime", Enabled);

            EditorGUILayout.Separator();
            EditorGUILayout.Separator();

            // Should version control be enabled?
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Enable Version Control");
            EnableVersionControl = EditorGUILayout.Toggle(EnableVersionControl);
            EditorGUILayout.EndHorizontal();
            
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

            EditorGUILayout.Separator();
            EditorGUILayout.Separator();

            // Current settings information
            EditorGUILayout.LabelField("Current Details", EditorStyles.boldLabel);
            // User information
            if (ApiKeyValidated)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Logged in as: " + User.display_name);
                EditorGUILayout.EndHorizontal();
            }
            // Project information
            if (ActiveProject.name != null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Working on project: " + ActiveProject.name);
                EditorGUILayout.EndHorizontal();
            }

            // Git information
            if (Enabled && EnableVersionControl)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Currently on branch: " + GitHelper.branch);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndToggleGroup();
            EditorGUILayout.EndVertical();

            // Handle any changed data
            if (GUI.changed)
            {
                // Has the active project changed?
                if (s_ActiveProjectIndex != projectSelection)
                {
                    ActiveProject = s_UserProjects[Mathf.Max(projectSelection - 1, 0)];
                }

                // If the Api Key has changed, reset the validation
                if (newApiKey != ApiKey)
                {
                    ApiKey = newApiKey;
                    ApiKeyValidated = false;
                }

                if (validateKeyButton && !ApiKeyValidated)
                    GetCurrentUser();
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
                plugin = "Unity-WakaTime/" + Version;
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
                    // There shouldn't be any errors here since we are redirecting
                    // standard error
                    UnityEngine.Debug.LogError("<WakaTime> There was an error getting git branch.");
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
                        "version control support. It can be re-enabled from the preferences.\n" +
                        error
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