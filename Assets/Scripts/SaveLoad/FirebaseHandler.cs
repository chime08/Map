using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Runtime.InteropServices;

public class FirebaseHandler : MonoBehaviour
{
    public string CurrentMapName { get; private set; } = "";
    public int CurrentSlot { get; private set; } = 0;

    private static string GetSlotPath(int slot) =>
        Path.Combine(Application.persistentDataPath, UserSession.Username + "-SLOT" + slot + ".json");

    private static GameObject LoadPrefab(string prefabName)
    {
        string[] folders = { "Prefabs/PlaceableObjects/", "Prefabs/DepercatedObjects/", "Prefabs/" };
        foreach (string folder in folders)
        {
            GameObject prefab = Resources.Load<GameObject>(folder + prefabName);
            if (prefab != null) return prefab;
        }
        return null;
    }

    public void DestroyObjects()
    {
        foreach (GameObject obj in GameObject.FindGameObjectsWithTag("Selectable"))
            Destroy(obj);
        BuildingSystem.ObjectCount = 0;
        CurrentMapName = "";
    }

    private string Serialize(GameObjectData[] items, string mapName) =>
        JsonUtility.ToJson(new Environment(items, mapName), true);

    private Environment Deserialize(string json) =>
        JsonUtility.FromJson<Environment>(json);

    public void SaveEnvironment(int slot) => SaveEnvironment(slot, "Slot " + slot);

    public void SaveEnvironment(int slot, string mapName)
    {
        List<GameObjectData> objectData = new List<GameObjectData>();
        foreach (GameObject obj in GameObject.FindGameObjectsWithTag("Selectable"))
        {
            GameObjectData data = new(obj);
            if (data.IsValid) objectData.Add(data);
        }

        string json = Serialize(objectData.ToArray(), mapName);
        File.WriteAllText(GetSlotPath(slot), json);
        CurrentMapName = mapName;
        CurrentSlot = slot;
        Debug.Log("Saved to: " + GetSlotPath(slot));
    }

    public void LoadEnvironment(int slot)
    {
        DestroyObjects();

        if (slot == 0) return;

        string path = GetSlotPath(slot);
        if (!File.Exists(path))
        {
            Debug.LogWarning("No save found at: " + path);
            return;
        }

        string json = File.ReadAllText(path);
        Environment env = Deserialize(json);
        CurrentMapName = env.Name;
        CurrentSlot = slot;

        foreach (GameObjectData data in env.Items)
        {
            GameObject prefab = LoadPrefab(data.prefabName);
            if (prefab == null)
            {
                Debug.LogError("Prefab not found: " + data.prefabName);
                continue;
            }

            GameObject obj = Instantiate(prefab);

            PlaceableObject placeableObj = obj.GetComponent<PlaceableObject>();
            if (placeableObj != null)
            {
                placeableObj.prefabName = data.prefabName;
                placeableObj.Place();
            }

            obj.name = data.prefabName + " #" + BuildingSystem.ObjectCount++;
            obj.transform.SetPositionAndRotation(data.position, data.rotation);
            obj.transform.localScale = data.scale;
            obj.transform.SetParent(null);
            obj.SetActive(true);
        }
    }

    public void GetSlotName(int slot, Action<string> onResult)
    {
        string path = GetSlotPath(slot);
        if (!File.Exists(path))
        {
            onResult?.Invoke(null);
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            Environment env = Deserialize(json);

            int count = env.Items != null ? env.Items.Length : env.ObjectCount;
            string countLine = count + (count == 1 ? " object" : " objects");

            string display = env.Name;
            if (!string.IsNullOrEmpty(env.SavedAt)) display += "\n" + env.SavedAt;
            display += "\n" + countLine;

            onResult?.Invoke(display);
        }
        catch
        {
            onResult?.Invoke(null);
        }
    }

    #if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void DownloadTextFile(string filename, string content);
    #endif

    public bool SlotExists(int slot) => File.Exists(GetSlotPath(slot));

    public bool RenameCurrentMap(string newName)
    {
        if (CurrentSlot == 0) return false;
        return RenameEnvironment(CurrentSlot, newName);
    }

    public bool RenameEnvironment(int slot, string newName)
    {
        string path = GetSlotPath(slot);
        if (!File.Exists(path)) return false;

        string json = File.ReadAllText(path);
        Environment env = Deserialize(json);
        env.Name = string.IsNullOrWhiteSpace(newName) ? "Slot " + slot : newName.Trim();
        CurrentMapName = env.Name;
        File.WriteAllText(path, JsonUtility.ToJson(env, true));
        return true;
    }

    public bool DeleteEnvironment(int slot)
    {
        string path = GetSlotPath(slot);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        Debug.Log("Deleted slot " + slot + " at: " + path);
        return true;
    }

    public string DownloadMap(int slot)
    {
        string path = GetSlotPath(slot);
        if (!File.Exists(path))
        {
            Debug.LogWarning("No save found at slot " + slot);
            return "No map saved in slot " + slot;
        }

        string json     = File.ReadAllText(path);
        string filename = UserSession.Username + "-SLOT" + slot + ".json";

    #if UNITY_WEBGL && !UNITY_EDITOR
            DownloadTextFile(filename, json);
            return "Map downloaded";
    #elif UNITY_IOS && !UNITY_EDITOR
            GUIUtility.systemCopyBuffer = json;
            Debug.Log("Map JSON copied to clipboard");
            return "Map copied to clipboard";
    #else
        string dest = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop),
            filename
        );
        File.WriteAllText(dest, json);
        Debug.Log("Map saved to Desktop: " + dest);
        return "Map saved to Desktop";
    #endif
    }

}

[Serializable]
public class GameObjectData
{
    public string name;
    public string prefabName;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;

    public bool IsValid => !string.IsNullOrEmpty(prefabName);

    public GameObjectData(GameObject gameObject)
    {
        PlaceableObject obj = gameObject.GetComponent<PlaceableObject>();
        if (obj == null)
        {
            Debug.LogWarning("No PlaceableObject on: " + gameObject.name);
            return;
        }

        name = gameObject.name;
        prefabName = obj.prefabName;
        position = gameObject.transform.position;
        rotation = gameObject.transform.rotation;
        scale = gameObject.transform.localScale;
    }
}

[Serializable]
public class Environment
{
    public string Name;
    public string SavedAt;
    public int ObjectCount;
    public GameObjectData[] Items;

    public Environment(GameObjectData[] items, string mapName = "Untitled")
    {
        Name = string.IsNullOrWhiteSpace(mapName) ? "Untitled" : mapName;
        SavedAt = DateTime.Now.ToString("MMM d, yyyy · h:mm tt");
        Items = items;
        ObjectCount = BuildingSystem.ObjectCount;
    }
}
