using UnityEditor;
using UnityEngine;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.Compilation;
using System;
using System.IO;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

[InitializeOnLoad]
public static class StudentDataLogger
{
    private static DateTime phaseStartTime;
    
    private static int sessionErrorCount = 0;
    private static int sessionWarningCount = 0;
    private static int sessionLogCount = 0;
    private static Dictionary<string, int> errorFrequencies = new Dictionary<string, int>();

    private static int hierarchyChangeCount = 0;
    private static int undoCount = 0;
    private static int perspectiveSwitchCount = 0;
    private static int recompileCount = 0;
    private static int assetImportCount = 0;
    private static string lastActiveWindowType = "";

    private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    private static string formPostUrl = "https://docs.google.com/forms/d/e/1FAIpQLScp67sEbjnG2AXpsT6QBscIjwLnLNhQ0eHMRhRajDWWHS9Ydg/formResponse";

    private static readonly string cacheFilePath = "Library/TelemetryOfflineCache.json";
    private static TelemetryQueue logQueue = new TelemetryQueue();
    private static bool isProcessingQueue = false;

    static StudentDataLogger()
    {
        phaseStartTime = DateTime.Now;

        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        Application.logMessageReceived += OnLogMessageReceived;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorApplication.update += OnEditorUpdate;
        EditorSceneManager.sceneSaved += OnSceneSaved;
        
        CompilationPipeline.compilationFinished += OnCompilationFinished;

        LoadCache();
        
        LogData("Editor_Acildi", 0, 0, 0, 0, 0, 0, "Yok", "Proje ve telemetry baslatildi.");
    }

    private static void OnCompilationFinished(object obj)
    {
        recompileCount++;
    }

    public static void AddAssetImportCount(int count)
    {
        assetImportCount += count;
    }

    private static void OnEditorUpdate()
    {
        if (Application.isPlaying) return;

        EditorWindow activeWindow = EditorWindow.focusedWindow;
        if (activeWindow != null)
        {
            string currentType = activeWindow.GetType().Name;
            
            if (currentType != lastActiveWindowType)
            {
                if ((currentType == "SceneView" && lastActiveWindowType == "GameView") ||
                    (currentType == "GameView" && lastActiveWindowType == "SceneView"))
                {
                    perspectiveSwitchCount++; 
                }
                lastActiveWindowType = currentType;
            }
        }
    }

    private static void OnHierarchyChanged()
    {
        if (!Application.isPlaying) hierarchyChangeCount++; 
    }

    private static void OnUndoRedo()
    {
        if (!Application.isPlaying) undoCount++; 
    }

    private static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
    {
        LogData("Sahne_Kaydedildi", 0, 0, 0, 0, 0, 0, "Yok", $"Sahne: {scene.name}");
    }

    private static string GetMostFrequentError()
    {
        if (errorFrequencies.Count == 0) return "Yok";
        return errorFrequencies.OrderByDescending(x => x.Value).First().Key;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            double editorDuration = (DateTime.Now - phaseStartTime).TotalSeconds;
            LogData("Editor_Tasarim_Fazi", editorDuration, hierarchyChangeCount, undoCount, perspectiveSwitchCount, recompileCount, assetImportCount, "Yok", "Play tusuna basilmadan onceki editor eforu");
            
            sessionErrorCount = 0;
            sessionWarningCount = 0;
            sessionLogCount = 0;
            errorFrequencies.Clear();
            phaseStartTime = DateTime.Now; 
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            double playDuration = (DateTime.Now - phaseStartTime).TotalSeconds;
            string mostFrequent = GetMostFrequentError();
            LogData("PlayMode_Test_Fazi", playDuration, sessionErrorCount, sessionWarningCount, sessionLogCount, 0, 0, mostFrequent, "Bir oyun deneme seansi tamamlandi");
            
            hierarchyChangeCount = 0;
            undoCount = 0;
            perspectiveSwitchCount = 0;
            recompileCount = 0;
            assetImportCount = 0;
            phaseStartTime = DateTime.Now; 
        }
    }

    private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        if (!Application.isPlaying) return; 

        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) 
        {
            sessionErrorCount++;
            string shortError = condition.Split('\n')[0];
            
            if (errorFrequencies.ContainsKey(shortError))
                errorFrequencies[shortError]++;
            else
                errorFrequencies[shortError] = 1;
        }
        else if (type == LogType.Warning) 
            sessionWarningCount++;
        else 
            sessionLogCount++;
    }

    public static void LogData(string eventType, double durationSec, int metric1, int metric2, int metric3, int metrik4_derleme, int metrik5_asset, string metrik6_hata, string details)
    {
        string projectName = Application.productName;
        string userName = Environment.UserName; 
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // Veriyi doğrudan göndermek yerine önce yapılandırıp kuyruğa ekliyoruz
        TelemetryData newData = new TelemetryData
        {
            timestamp = timestamp,
            projectUser = $"{projectName} ({userName})",
            eventType = eventType,
            durationSec = durationSec.ToString("F1"),
            metric1 = metric1.ToString(),
            metric2 = metric2.ToString(),
            metric3 = metric3.ToString(),
            metric4 = metrik4_derleme.ToString(),
            metric5 = metrik5_asset.ToString(),
            metric6 = metrik6_hata,
            details = details
        };

        logQueue.items.Add(newData);
        SaveCache();
        
        ProcessQueue();
    }

    private static void LoadCache()
    {
        if (File.Exists(cacheFilePath))
        {
            try
            {
                string json = File.ReadAllText(cacheFilePath);
                logQueue = JsonUtility.FromJson<TelemetryQueue>(json) ?? new TelemetryQueue();
            }
            catch
            {
                logQueue = new TelemetryQueue();
            }
        }
    }

    private static void SaveCache()
    {
        try
        {
            string json = JsonUtility.ToJson(logQueue, true);
            File.WriteAllText(cacheFilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Telemetry önbelleğe yazılamadı: " + e.Message);
        }
    }

    private static async void ProcessQueue()
    {
        // Aynı anda birden fazla gönderim döngüsü çalışmasın diye kontrol
        if (isProcessingQueue) return;
        isProcessingQueue = true;

        bool internetAvailable = true;

        // Kuyrukta eleman oldukça ve internet kesilmedikçe dön
        while (logQueue.items.Count > 0 && internetAvailable)
        {
            TelemetryData currentItem = logQueue.items[0];
            bool success = await SendDataAsync(currentItem);

            if (success)
            {
                // Gönderim başarılıysa listeden çıkar ve dosyayı güncelle (çift gönderimi önler)
                logQueue.items.RemoveAt(0);
                SaveCache();
            }
            else
            {
                // Gönderim başarısızsa internet yok demektir, döngüyü durdur
                internetAvailable = false;
            }
        }

        isProcessingQueue = false;
    }

    private static async Task<bool> SendDataAsync(TelemetryData data)
    {
        var formValues = new Dictionary<string, string>
        {
            { "entry.48785763", data.timestamp },
            { "entry.1276301359", data.projectUser },
            { "entry.850803661", data.eventType },
            { "entry.743518417", data.durationSec },
            { "entry.1499987907", data.metric1 },
            { "entry.683247394", data.metric2 },
            { "entry.889263307", data.metric3 },
            { "entry.610732536", data.metric4 },
            { "entry.1223948761", data.metric5 },
            { "entry.106432271", data.metric6 },
            { "entry.1596730027", data.details }
        };

        var content = new FormUrlEncodedContent(formValues);

        try 
        {
            HttpResponseMessage response = await client.PostAsync(formPostUrl, content);
            return response.IsSuccessStatusCode;
        } 
        catch 
        {
            // Hata fırlatılırsa (örn: Timeout, ağ yok) false dön
            return false;
        }
    }
}

// JSON Serialization için veri modelleri
[Serializable]
public class TelemetryData
{
    public string timestamp;
    public string projectUser;
    public string eventType;
    public string durationSec;
    public string metric1;
    public string metric2;
    public string metric3;
    public string metric4;
    public string metric5;
    public string metric6;
    public string details;
}

[Serializable]
public class TelemetryQueue
{
    public List<TelemetryData> items = new List<TelemetryData>();
}

class TelemetryAssetPostprocessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (importedAssets != null && importedAssets.Length > 0)
        {
            StudentDataLogger.AddAssetImportCount(importedAssets.Length);
        }
    }
}

class BuildLogger : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        double buildTime = report.summary.totalTime.TotalSeconds;
        int errors = report.summary.totalErrors;
        int warnings = report.summary.totalWarnings;

        if (report.summary.result == BuildResult.Failed)
        {
            StudentDataLogger.LogData("Build_Basarisiz", buildTime, errors, warnings, 0, 0, 0, "Yok", "Derleme sirasinda coktu");
        }
        else if (report.summary.result == BuildResult.Succeeded)
        {
            StudentDataLogger.LogData("Build_Basarili", buildTime, errors, warnings, 0, 0, 0, "Yok", "Derleme basarili");
        }
    }
}