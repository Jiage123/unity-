using Unity.Plastic.Newtonsoft.Json;
using System;   
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum AssetBundlePattern
{
    // �༭ģ�������أ�Ӧʹ��AssetDataBase������Դ����
    EditorSimulation,
    // ���ؼ���ģʽ��Ӧ���������·����StreamAssets·���£��Ӹ�·������
    Local,
    // Զ�˼���ģʽ��Ӧ������������Դ��������ַ��Ȼ��ͨ�������������
    // ���ص�ɳ��·��persistentDataPath,�ٽ��м���
    Remote
}

public enum AssetBundleCompressionPattern
{
    // LZMA:����ѹ������С�����ǽ�ѹ�ٶ����������ص�ʱ������������
    LZMA,
    // LZ4:����ѹ���еȣ��ٶȽϿ죬�ٶȸ�None Compression��ࣨ�Ƽ�ʹ�ã�
    LZ4,
    // None Compression:��ѹ������������󣬵��Ǽ���������
    None
}

public enum IncrementalBuildMode
{
    None, // �������Ĭ�Ͼ����������
    UseIncrementalBuild,
    ForceRebuild
}

// Package������¼����Ϣ
public class PackageBuildInfo
{
    public string PackageName;
    public List<AssetBuildInfo> AssetInfos = new List<AssetBuildInfo>();
    public List<string> PackageDependecies = new List<string>();
    // �����Ƿ��ǳ�ʼ��
    public bool IsSourcePackage = false;
}

// Package�е�Assets���֮���¼����Ϣ
public class AssetBuildInfo
{
    // ��Դ���ƣ�����Ҫ������Դ�ǣ�Ӧ�ú͸��ַ�����ͬ
    public string AssetName;

    // ����Դ�����ĸ�AssetBundle
    public string AssetBundleName;
}
public class AssetPackage
{
    public PackageBuildInfo PackageInfo;

    public string PackageName { get { return PackageInfo.PackageName; } }
    Dictionary<string, UnityEngine.Object> LoadedAssets = new Dictionary<string, UnityEngine.Object>();

    public T LoadAsset<T>(string assetName) where T : UnityEngine.Object
    {
        T assetObject = default;
        foreach (AssetBuildInfo info in PackageInfo.AssetInfos)
        {
            if (info.AssetName == assetName)
            {
                if (LoadedAssets.ContainsKey(assetName))
                {
                    assetObject = LoadedAssets[assetName] as T;
                    return assetObject;
                }

                foreach (string dependAssetName in AssetManagerRuntime.Instance.Manifest.GetAllDependencies(info.AssetBundleName))
                {
                    string dependAssetBundlePath = Path.Combine(AssetManagerRuntime.Instance.AssetBundleLoadPath, dependAssetName);

                    AssetBundle.LoadFromFile(dependAssetBundlePath);
                }

                string assetBundlePath = Path.Combine(AssetManagerRuntime.Instance.AssetBundleLoadPath, info.AssetBundleName);

                AssetBundle bundle = AssetBundle.LoadFromFile(assetBundlePath);
                assetObject = bundle.LoadAsset<T>(assetName);
            }
        }

        if (assetObject == null)
        {
            Debug.LogError($"{assetName}δ��{PackageName}���ҵ�");
        }

        return assetObject;
    }
}


public class AssetManagerRuntime
{
    // ��ǰ��ĵ���
    public static AssetManagerRuntime Instance;

    // ��ǰ��Դ��ģʽ
    AssetBundlePattern CurrentPattern;

    // ���б���Asset������·����ӦΪAssetBundleLoadPath����һ��
    string LocalAssetPath;

    // AssetBundle����·��
    public string AssetBundleLoadPath;

    // ��Դ����·����������ɺ�Ӧ����Դ���õ�LocalAssetPath��
    string DownloadPath;

    // ���ڶԱȱ�����Դ�汾��Զ����Դ�汾��
    int LocalAssetVersion;

    // �������е�Package��Ϣ
    Dictionary<string, PackageBuildInfo> PackageDic = new Dictionary<string, PackageBuildInfo>();

    // ���������Ѽ��ص�Package
    Dictionary<string, AssetPackage> LoadedAssetPackages = new Dictionary<string, AssetPackage>();

    public AssetBundleManifest Manifest;
    public static void AssetManagerInit(AssetBundlePattern pattern)
    {
        if (Instance == null)
        {
            Instance = new AssetManagerRuntime();
            Instance.CurrentPattern = pattern;
            Instance.CheckLocalAssetPath();
            Instance.CheckLocalAssetVersion();
            Instance.CheckAssetBundleLoadPath();
        }
    }

    void CheckLocalAssetPath()
    {
        switch (CurrentPattern)
        {
            case AssetBundlePattern.EditorSimulation:
                break;
            case AssetBundlePattern.Local:
                LocalAssetPath = Path.Combine(Application.streamingAssetsPath, "LocalAssets");
                break;
            case AssetBundlePattern.Remote:
                DownloadPath = Path.Combine(Application.persistentDataPath, "DownloadAssets");
                LocalAssetPath = Path.Combine(Application.persistentDataPath, "LocalAssets");
                break;
        }
    }

    void CheckLocalAssetVersion()
    {
        // asset.version ���������Զ�����չ�����ı��ļ�
        string versionFilePath = Path.Combine(LocalAssetPath, "asset.version");

        if (!File.Exists(versionFilePath))
        {
            LocalAssetVersion = 100;
            File.WriteAllText(versionFilePath, LocalAssetVersion.ToString());
            return;
        }
        LocalAssetVersion = int.Parse(File.ReadAllText(versionFilePath));
    }

    void CheckAssetBundleLoadPath()
    {
        AssetBundleLoadPath = Path.Combine(LocalAssetPath, LocalAssetVersion.ToString());
    }

    public AssetPackage LoadPackage(string packageName)
    {
        if (PackageDic.Count == 0)
        {
            string packagePath = Path.Combine(AssetBundleLoadPath, "Packages");
            string packageString = File.ReadAllText(packagePath);

            PackageDic = JsonConvert.DeserializeObject<Dictionary<string, PackageBuildInfo>>(packageString);
            Debug.Log($"Package����·��Ϊ��{packagePath}");
        }

        if (Manifest == null)
        {
            string mainBundlePath = Path.Combine(AssetBundleLoadPath, "Local");

            AssetBundle mainBundle = AssetBundle.LoadFromFile(mainBundlePath);
            Manifest = mainBundle.LoadAsset<AssetBundleManifest>(nameof(AssetBundleManifest));
        }

        AssetPackage assetPackage = null;
        if (LoadedAssetPackages.ContainsKey(packageName))
        {
            assetPackage = LoadedAssetPackages[packageName];
            Debug.LogWarning($"{packageName}�Ѿ�����");
            return assetPackage;
        }

        assetPackage = new AssetPackage();
        assetPackage.PackageInfo = PackageDic[packageName];
        LoadedAssetPackages.Add(assetPackage.PackageName, assetPackage);

        foreach (string dependName in assetPackage.PackageInfo.PackageDependecies)
        {
            LoadPackage(dependName);
        }
        return assetPackage;
    }
}
