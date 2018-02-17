﻿using ExperimentObjects;
using JsonObjects;
using JsonObjects.Game;
using JsonObjects.Statistics;
using JsonObjects.Survey;
using Polimi.GameCollective.Connectivity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ExperimentManager allows to manage experiments. An experiment is composed of different studies 
/// (a set of maps), each one composed by cases (a set of map varaitions). Each time a new
/// experiment is requested, a list of cases from the less played study is provided to the user
/// to be played. A tutorial and a survey scene can be added at the beginning and at the end of
/// the experiment, respectevely. When ExperimentManager is used to log online data, before creating
/// a new list of cases or before saving data on the server, the experiment completion is retrieved
/// from the server. When sending data to the server this information is stored in the comment field 
/// of each entry as the sum of the retrieved completion and the completion progress stored locally.
/// </summary>
public class ExperimentManager : SceneSingleton<ExperimentManager> {

    private Case tutorial;
    private bool playTutorial;

    private List<Study> studies;
    private int casesPerUsers;
    private string experimentName;

    private Case survey;
    private bool playSurvey;

    private bool logOffline;
    private bool logOnline;
    private bool logGame;
    private bool logStatistics;

    // List of cases the current player has to play.
    private List<Case> caseList;
    // Directory for this esperiment files.
    private string experimentDirectory;

    // Label of the current game log.
    private string gameLabel;
    // Support object to format the log.
    private JsonGameLog jGameLog;
    // Label of the current statistic log.
    private string statisticsLabel;
    // Support object to format the log.
    private JsonStatisticsLog jStatisticsLog;
    // Start time of the log.
    private float logStart;

    // Completion trackers.
    private List<StudyCompletionTracker> studyCompletionTrackers;

    // Spawn time of current target.
    private float targetSpawn = 0;
    // Spawn time of last target.
    private float lastTargetSpawn = 0;
    // Current distance.
    private float currentDistance = 0;
    // Total distance.
    private float totalDistance = 0;
    // Total shots.
    private int shotCount = 0;
    // Total hits.
    private int hitCount = 0;
    // Total destoryed targets.
    private float killCount = 0;
    // Medium kill time.
    private float mediumKillTime = 0;
    // Medium distance covered to find a target.
    private float mediumKillDistance = 0;
    // Size of a maps tile.
    private float tileSize = 1;
    // Is the map flip?
    private bool flip = false;
    // Position of the player.
    private Vector3 lastPosition = new Vector3(-1, -1, -1);
    // Initial target position.
    private Vector3 initialTargetPosition = new Vector3(-1, -1, -1);
    // Initial player position.
    private Vector3 initialPlayerPosition = new Vector3(-1, -1, -1);

    private readonly float SERVER_CONNECTION_PERIOD = 0.1f;
    private readonly float SERVER_CONNECTION_TIMEOUT = 10f;
    private readonly string KEEP_COMPLETION = "KEEP_COMPLETION";
    private readonly string DISCARD_COMPLETION = "DISCARD_COMPLETION";

    private Queue<Entry> postQueue;
    private bool postCompletion;

    private int currentStudy = -1;
    private int currentCase = -1;
    private string testID;

    private bool loggingGame = false;
    private bool loggingStatistics = false;

    void Awake() {
        DontDestroyOnLoad(transform.gameObject);

        caseList = new List<Case>();

        if (logOffline && Application.isWebPlayer) {
            logOffline = false;
        }

        if (!logOffline && !logOnline) {
            logGame = false;
            logStatistics = false;
        } else {
            if (logOffline) {
                SetupDirectories();
                if (!logOnline) {
                    SetCompletionOffline();
                }
            }

            if (logGame) {
                jGameLog = new JsonGameLog();
            }
            if (logStatistics) {
                jStatisticsLog = new JsonStatisticsLog();
            }
        }
    }

    void Start() {
        if (logOnline) {
            postQueue = new Queue<Entry>();
            StartCoroutine(ContactServer());
        }
    }

    void OnEnable() {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        if ((logGame || logStatistics) && !(playTutorial && currentCase == 0) && !(playSurvey
            && currentCase == caseList.Count - 2) && currentCase != caseList.Count - 1) {
            SetupLogging();
        }
    }

    // Menages the post queue.
    private IEnumerator ContactServer() {
        while (true) {
            if (postQueue.Count > 0) {
                Entry currentEntry = postQueue.Dequeue();
                Debug.Log("I'm posting " + currentEntry.Label + "...");
                // Wait for the Data Manager to finish the current operation, if any.
                yield return StartCoroutine(WaitForDataManager());
                // Get the log count of the last log on the server.
                RemoteDataManager.Instance.GetLastEntry();
                yield return StartCoroutine(WaitForDataManager());
                JsonCompletionTracker jcp =
                    ExtractCompletionTracker(RemoteDataManager.Instance.Result);
                jcp.logsCount++;
                if (currentEntry.Comment == KEEP_COMPLETION) {
                    jcp.studyCompletionTrackers = studyCompletionTrackers;
                }
                // Post the data.
                RemoteDataManager.Instance.SaveData(new Entry(currentEntry.Label, currentEntry.Data,
                   JsonUtility.ToJson(jcp)));
            } else {
                Debug.Log("I'm doing nothing...");
                yield return new WaitForSeconds(SERVER_CONNECTION_PERIOD);
            }
        }
    }

    /* EXPERIMENT */

    // Sets up the experiment manager.
    public void Setup(Case tutorial, bool playTutorial, List<Study> studies, int casesPerUsers,
        string experimentName, Case survey, bool playSurvey, bool logOffline, bool logOnline,
        bool logGame, bool logStatistics) {
        this.tutorial = tutorial;
        this.playTutorial = playTutorial;
        this.studies = studies;
        this.casesPerUsers = casesPerUsers;
        this.experimentName = experimentName;
        this.survey = survey;
        this.playSurvey = playSurvey;
        this.logOffline = logOffline;
        this.logOnline = logOnline;
        this.logGame = logGame;
        this.logStatistics = logStatistics;
    }

    // Creates a new list of cases for the player to play.
    private void CreateNewList() {
        testID = GetTimeStamp();
        caseList.Clear();

        if (playTutorial) {
            caseList.Add(tutorial);
        }

        caseList.AddRange(GetCases(casesPerUsers));

        if (playSurvey) {
            caseList.Add(survey);
        }

        caseList.Add(new Case {
            scene = SceneManager.GetActiveScene().name
        });

        currentCase = -1;
    }

    // Returns a well formatted timestamp.
    private string GetTimeStamp() {
        return DateTime.Now.ToString("yy") + DateTime.Now.ToString("MM") +
            DateTime.Now.ToString("dd") + DateTime.Now.ToString("HH") +
            DateTime.Now.ToString("mm") + DateTime.Now.ToString("ss");
    }

    // Gets the cases to add in a round-robin fashion.
    private List<Case> GetCases(int count) {
        List<Case> lessPlayedCases = new List<Case>();

        // Get the least played study.
        int minValue = studyCompletionTrackers[0].studyCompletion;
        currentStudy = 0;

        for (int i = 0; i < studyCompletionTrackers.Count; i++) {
            if (studyCompletionTrackers[i].studyCompletion < minValue) {
                minValue = studyCompletionTrackers[i].studyCompletion;
                currentStudy = i;
            }
        }

        // Get the least played cases in the least played study.
        if (count < studies[currentStudy].cases.Count) {
            var sorted = studyCompletionTrackers[currentStudy].casesCompletion.Select((v, i) =>
                i).OrderBy(v => v).ToList();

            for (int i = 0; i < count; i++) {
                studies[currentStudy].cases[sorted[i]].RandomizeCurrentMap();
                lessPlayedCases.Add(studies[currentStudy].cases[sorted[i]]);
                studyCompletionTrackers[currentStudy].casesCompletion[sorted[i]]++;
                studyCompletionTrackers[currentStudy].studyCompletion++;
            }
        } else {
            for (int i = 0; i < studies[currentStudy].cases.Count; i++) {
                studies[currentStudy].cases[i].RandomizeCurrentMap();
                lessPlayedCases.Add(studies[currentStudy].cases[i]);
                studyCompletionTrackers[currentStudy].casesCompletion[i]++;
                studyCompletionTrackers[currentStudy].studyCompletion++;
            }
        }

        if (logOnline && postCompletion) {
            postQueue.Enqueue(new Entry("PA_COMPLETION", "", KEEP_COMPLETION));
            postCompletion = false;
        }

        // Randomize the play order.
        Shuffle(lessPlayedCases);

        return lessPlayedCases;
    }

    // Starts a new experiment.
    public IEnumerator StartNewExperiment() {
        if (logOnline) {
            yield return StartCoroutine(SetCompletionOnline());
        }
        CreateNewList();
        LoadNextScene();
    }

    // Retuns the next scene to be played.
    public void LoadNextScene() {
        if (loggingGame || loggingStatistics) {
            StopLogging();
        }

        currentCase++;

        Case c = caseList[currentCase];

        ParameterManager.Instance.Flip = currentCase % 2 == 0 ? true : false;
        ParameterManager.Instance.GenerationMode = 4;
        ParameterManager.Instance.MapDNA = (c.maps == null || c.maps.Count == 0) ? "" :
            c.GetCurrentMap().text;

        SceneManager.LoadScene(c.scene);
    }

    /* LOGGING */

    // Sets up the directories.
    private void SetupDirectories() {
        experimentDirectory = Application.persistentDataPath + "/Export/" + experimentName;
        CreateDirectory(experimentDirectory);
        // Create the case directories if needed.
        foreach (Study s in studies) {
            CreateDirectory(experimentDirectory + "/" + s.studyName);
            CreateDirectory(experimentDirectory + "/" + s.studyName + "/Maps");
            foreach (Case c in s.cases) {
                foreach (TextAsset map in c.maps) {
                    File.WriteAllText(@experimentDirectory + "/" + s.studyName + "/Maps/" +
                       map.name + ".txt", map.text);
                }
            }
        }
    }

    // Sets the completion (online).
    private IEnumerator SetCompletionOnline() {
        int connectionAttempts = 0;

        // Wait for the Connection Manager.
        while (connectionAttempts * SERVER_CONNECTION_PERIOD < SERVER_CONNECTION_TIMEOUT &&
            postQueue.Count() > 0) {
            connectionAttempts++;
            yield return new WaitForSeconds(SERVER_CONNECTION_PERIOD);
        }

        // If the Connection Manager finished before the timeout I try to contact the server.
        if (connectionAttempts * SERVER_CONNECTION_PERIOD < SERVER_CONNECTION_TIMEOUT) {
            // Get the log count of the last log on the server.
            RemoteDataManager.Instance.GetLastEntry();
            yield return StartCoroutine(WaitForDataManager(SERVER_CONNECTION_TIMEOUT -
                connectionAttempts * SERVER_CONNECTION_PERIOD));
            if (RemoteDataManager.Instance.IsResultReady) {
                JsonCompletionTracker jcp =
                    ExtractCompletionTracker(RemoteDataManager.Instance.Result);
                studyCompletionTrackers = jcp.studyCompletionTrackers;
                postCompletion = true;
            } else {
                studyCompletionTrackers = GetRandomTracker();
                postCompletion = false;
            }
        } else {
            studyCompletionTrackers = GetRandomTracker();
            postCompletion = false;
        }
    }

    // Sets the completion (offline).
    private void SetCompletionOffline() {
        studyCompletionTrackers = new List<StudyCompletionTracker>();

        foreach (Study s in studies) {
            string[] allFiles = Directory.GetFiles(experimentDirectory + "/" + s.studyName);

            int studyCompletion = 0;
            List<int> casesCompletion = new List<int>();

            foreach (Case c in s.cases) {
                int gameCount = 0;
                int statisticsCount = 0;

                foreach (TextAsset map in c.maps) {
                    foreach (string file in allFiles) {
                        if (file.Contains(map.name.Replace(".map", "") + "_log")) {
                            gameCount++;
                        }
                        if (file.Contains(map.name.Replace(".map", "") + "_statistics")) {
                            statisticsCount++;
                        }
                    }
                }

                casesCompletion.Add((gameCount > statisticsCount) ? gameCount : statisticsCount);
                studyCompletion += casesCompletion.Last();
            }

            studyCompletionTrackers.Add(new StudyCompletionTracker(studyCompletion,
                casesCompletion.ToArray()));
        }
    }

    // Sets up logging.
    private void SetupLogging() {
        GameManager gm = FindObjectOfType(typeof(GameManager)) as GameManager;
        if (gm != null) {
            gm.LoggingHandshake();
        }

        if (logGame) {
            gameLabel = "PA_" + testID + "_" +
                caseList[currentCase].GetCurrentMap().name.Replace(".map", "") + "_log";
        }
        if (logStatistics) {
            statisticsLabel = "PA_" + testID + "_" +
                caseList[currentCase].GetCurrentMap().name.Replace(".map", "") + "_statistics";
        }
    }

    // Starts loggingGame.
    public void StartLogging() {
        logStart = Time.time;

        if (logGame) {
            jGameLog = new JsonGameLog();

            loggingGame = true;
        }

        if (logStatistics) {
            jStatisticsLog = new JsonStatisticsLog();

            targetSpawn = 0;
            lastTargetSpawn = 0;
            currentDistance = 0;
            totalDistance = 0;
            shotCount = 0;
            hitCount = 0;
            killCount = 0;
            mediumKillTime = 0;
            mediumKillDistance = 0;
            lastPosition = new Vector3(-1, -1, -1);
            initialTargetPosition = new Vector3(-1, -1, -1);
            initialPlayerPosition = new Vector3(-1, -1, -1);

            loggingStatistics = true;
        }

        foreach (MonoBehaviour monoBehaviour in FindObjectsOfType(typeof(MonoBehaviour))) {
            ILoggable logger = monoBehaviour as ILoggable;
            if (logger != null) {
                logger.SetupLogging();
            }
        }
    }

    // Stops loggingGame and saves the log.
    public void StopLogging() {
        string log = "";

        // Save the statistics log, if any.
        if (loggingStatistics) {
            LogGameStatistics();

            log = JsonUtility.ToJson(jStatisticsLog);

            if (logOffline) {
                File.WriteAllText(experimentDirectory + "/" + studies[currentStudy].studyName + "/"
                    + statisticsLabel + ".json", log);
            }
            if (logOnline) {
                postQueue.Enqueue(new Entry(statisticsLabel, log, DISCARD_COMPLETION));
            }
            loggingStatistics = false;
        }

        // Save the game log, if any.
        if (loggingGame) {
            log = JsonUtility.ToJson(jGameLog);

            if (logOffline) {
                File.WriteAllText(experimentDirectory + "/" + studies[currentStudy].studyName + "/"
                    + gameLabel + ".json", log);
            }
            if (logOnline) {
                postQueue.Enqueue(new Entry(gameLabel, log, DISCARD_COMPLETION));
            }
            loggingGame = false;
        }
    }

    // Logs reload.
    public void LogRelaod(int gunId, int ammoInCharger, int totalAmmo) {
        if (loggingGame) {
            jGameLog.reloadLogs.Add(new JsonReload(Time.time - logStart, gunId, ammoInCharger,
                totalAmmo));
        }
    }

    // Logs the shot.
    public void LogShot(float x, float z, float direction, int gunId, int ammoInCharger,
        int totalAmmo) {
        if (loggingGame) {
            Coord coord = NormalizeFlipCoord(x, z);

            jGameLog.shotLogs.Add(new JsonShot(Time.time - logStart,
                coord.x, coord.z,
                NormalizeFlipAngle(direction), gunId, ammoInCharger, totalAmmo));
        }
        if (loggingStatistics) {
            shotCount++;
        }
    }

    // Logs info about the maps.
    public void LogMapInfo(float height, float width, float ts, bool f) {
        tileSize = ts;
        flip = f;

        if (loggingGame) {
            jGameLog.mapInfo = new JsonMapInfo(height, width, tileSize, flip);
        }
        if (loggingStatistics) {
            jStatisticsLog.mapInfo = new JsonMapInfo(height, width, tileSize, flip);
        }
    }

    // Logs info about the maps.
    public void LogGameInfo(int gameDuration, string scene) {
        if (loggingGame) {
            jGameLog.gameInfo = new JsonGameInfo(gameDuration, scene);
        }
        if (loggingStatistics) {
            jStatisticsLog.gameInfo = new JsonGameInfo(gameDuration, scene); ;
        }
    }

    // Logs the position (x and z respectively correspond to row and column in matrix notation).
    public void LogPosition(float x, float z, float direction) {
        Coord coord = NormalizeFlipCoord(x, z);

        if (loggingGame) {
            jGameLog.positionLogs.Add(new JsonPosition(Time.time - logStart, coord.x, coord.z,
            NormalizeFlipAngle(direction)));
        }
        if (loggingStatistics) {
            if (lastPosition.x != -1) {
                float delta = EulerDistance(coord.x, coord.z, lastPosition.x, lastPosition.z);
                totalDistance += delta;
                currentDistance += delta;
            }
            lastPosition.x = coord.x;
            lastPosition.z = coord.z;
        }
    }

    // Logs spawn.
    public void LogSpawn(float x, float z, string spawnedEntity) {
        Coord coord = NormalizeFlipCoord(x, z);

        if (loggingGame) {
            jGameLog.spawnLogs.Add(new JsonSpawn(Time.time - logStart, coord.x, coord.z, spawnedEntity));
        }
        if (loggingStatistics) {
            targetSpawn = Time.time - logStart;
            initialTargetPosition.x = coord.x;
            initialTargetPosition.z = coord.z;
            initialPlayerPosition = lastPosition;
        }
    }

    // Logs a kill.
    public void LogKill(float x, float z, string killedEnitiy, string killerEntity) {
        Coord coord = NormalizeFlipCoord(x, z);

        if (loggingGame) {
            jGameLog.killLogs.Add(new JsonKill(Time.time - logStart, coord.x, coord.z, killedEnitiy,
            killerEntity));
        }
        if (loggingStatistics) {
            LogTargetStatistics(coord.x, coord.z);
            killCount++;
            mediumKillTime += (Time.time - logStart - lastTargetSpawn - mediumKillTime) / killCount;
            mediumKillDistance += (currentDistance - mediumKillDistance) / killCount;
            currentDistance = 0;
            lastTargetSpawn = targetSpawn;
        }
    }

    // Logs a hit.
    public void LogHit(float x, float z, string hittedEntity, string hitterEntity, int damage) {
        Coord coord = NormalizeFlipCoord(x, z);

        if (loggingGame) {
            jGameLog.hitLogs.Add(new JsonHit(Time.time - logStart, coord.x, coord.z, hittedEntity, hitterEntity,
            damage));
        }
        if (loggingStatistics) {
            hitCount++;
        }
    }

    // Logs statistics about the performance of the player finding the target.
    private void LogTargetStatistics(float x, float z) {
        if (loggingStatistics) {
            jStatisticsLog.targetStatisticsLogs.Add(new JsonTargetStatistics(Time.time - logStart,
            initialPlayerPosition.x, initialPlayerPosition.z, lastPosition.x, lastPosition.z, x,
            z, currentDistance, (Time.time - logStart - lastTargetSpawn),
            currentDistance / (Time.time - logStart - lastTargetSpawn)));
        }
    }

    // Logs statistics about the game.
    private void LogGameStatistics() {
        if (loggingStatistics) {
            jStatisticsLog.finalStatistics = new JsonFinalStatistics(shotCount, hitCount,
            (shotCount > 0) ? (hitCount / (float)shotCount) : 0,
            totalDistance, mediumKillTime,
            mediumKillTime);
        }
    }

    /* SURVEY*/

    // Tells if I need to save the survey.
    public bool MustSaveSurvey() {
        return logOffline && (logGame || logStatistics) &&
            !File.Exists(experimentDirectory + "/survey.json");
    }

    // Save survey. This has to be done once.
    public void SaveSurvey(List<JsonQuestion> questions) {
        File.WriteAllText(experimentDirectory + "/survey.json",
            JsonUtility.ToJson(new JsonSurvey(questions)));
    }

    // Saves answers and informations about the experiment.
    public void SaveAnswers(List<JsonAnswer> answers) {
        if (logGame || logStatistics) {
            string log = JsonUtility.ToJson(new JsonAnswers(experimentName, GetCurrentCasesArray(),
            answers));

            if (logOnline) {
                postQueue.Enqueue(new Entry("PA_" + testID + "_survey.json", log,
                    DISCARD_COMPLETION));
            }
            if (logOffline) {
                File.WriteAllText(experimentDirectory + "/" + studies[currentStudy].studyName +
                    "/PA_" + testID + "_survey.json", log);
            }
        }
    }

    // Returns the played cases in an array.
    public string[] GetCurrentCasesArray() {
        try {
            string[] maps = new string[casesPerUsers];
            for (int i = 0; i < casesPerUsers; i++) {
                maps[i] = playTutorial ? caseList[i + 1].GetCurrentMap().name :
                    caseList[i].GetCurrentMap().name;
            }
            return maps;
        } catch {
            return null;
        }
    }

    /* SUPPORT FUNCTIONS */

    // Creates a directory if needed.
    private void CreateDirectory(string directory) {
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    // Returns the euler distance.
    private float EulerDistance(float x1, float y1, float x2, float y2) {
        return Mathf.Sqrt(Mathf.Pow(x1 - x2, 2) + Mathf.Pow(y1 - y2, 2));
    }

    // Normalizes the coordinates and flips them if needed.
    private Coord NormalizeFlipCoord(float x, float z) {
        x /= tileSize;
        z /= tileSize;

        if (flip) {
            return new Coord {
                x = z,
                z = x
            };
        } else {
            return new Coord {
                x = x,
                z = z
            };
        }
    }

    // Normalizes and, if needed, flips an angle with respect to the y = -x axis.
    private float NormalizeFlipAngle(float angle) {
        angle = NormalizeAngle(angle);

        if (flip) {
            angle = NormalizeAngle(angle + 45);
            angle = NormalizeAngle(-1 * angle - 45);
        }

        return angle;
    }

    // If an angle is negative it makes it positive.
    private float NormalizeAngle(float angle) {
        return (angle < 0) ? (360 + angle % 360) : (angle % 360);
    }

    // Shuffles a list.
    private void Shuffle<T>(IList<T> list) {
        var random = new System.Random();
        int n = list.Count;

        while (n > 1) {
            n--;
            int k = random.Next(n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    // Waits the Data Manager to complete the current operation.
    private IEnumerator WaitForDataManager() {
        while (!RemoteDataManager.Instance.IsResultReady) {
            yield return new WaitForSeconds(SERVER_CONNECTION_PERIOD);
        }
    }

    // Waits the Data Manager to complete the current operation, but with a timeout.
    private IEnumerator WaitForDataManager(float timeout) {
        int connectionAttempts = 0;

        while (connectionAttempts * SERVER_CONNECTION_PERIOD < timeout &&
            !RemoteDataManager.Instance.IsResultReady) {
            yield return new WaitForSeconds(SERVER_CONNECTION_PERIOD);
        }
    }

    // Returns a tracker with all the values set to zero.
    private List<StudyCompletionTracker> GetZeroTracker() {
        List<StudyCompletionTracker> zeroTracker = new List<StudyCompletionTracker>();

        foreach (Study s in studies) {
            int[] casesCompletion = new int[s.cases.Count];
            zeroTracker.Add(new StudyCompletionTracker(0, casesCompletion));
        }

        return zeroTracker;
    }

    // Returns a tracker with random completions.
    private List<StudyCompletionTracker> GetRandomTracker() {
        List<StudyCompletionTracker> randomTracker = new List<StudyCompletionTracker>();

        foreach (Study s in studies) {
            int[] casesCompletion = new int[s.cases.Count];
            // Assign a random completion to the cases.
            for (int i = 0; i < s.cases.Count; i++) {
                casesCompletion[i] = UnityEngine.Random.Range(0, s.cases.Count);
            }
            // Assign a random completion to the study.
            randomTracker.Add(new StudyCompletionTracker(UnityEngine.Random.Range(0, studies.Count),
                casesCompletion));
        }

        return randomTracker;
    }

    // Extracts the completion tracker from a log.
    private JsonCompletionTracker ExtractCompletionTracker(string result) {
        string[] splittedResult = result.Split('|');

        if (splittedResult.Length == 6) {
            try {
                JsonCompletionTracker jct =
                    JsonUtility.FromJson<JsonCompletionTracker>(splittedResult[4]);
                if (jct != null && jct.studyCompletionTrackers.Count > 0) {
                    return jct;
                }
            } catch {
            }
        }

        return new JsonCompletionTracker(0, GetZeroTracker());
    }

}