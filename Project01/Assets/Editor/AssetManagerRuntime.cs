using Unity.Plastic.Newtonsoft.Json;
using System;   
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum AssetBundlePattern
{
    // 编辑模拟器加载，应使用AssetDataBase进行资源加载
    EditorSimulation,
    // 本地加载模式，应打包到本地路径或StreamAssets路径下，从该路径加载
    Local,
    // 远端加载模式，应打包到任意的资源服务器地址，然后通过网络进行下载
    // 下载到沙盒路径persistentDataPath,再进行加载
    Remote
}

public enum AssetBundleCompressionPattern
{
    // LZMA:包体压缩到最小，但是解压速度最慢，加载的时候会加载整个包
    LZMA,
    // LZ4:包体压缩中等，速度较快，速度跟None Compression差不多（推荐使用）
    LZ4,
    // None Compression:不压缩包，包体最大，但是加载是最快的
    None
}

public enum IncrementalBuildMode
{
    None, // 打包方法默认就是增量打包
    UseIncrementalBuild,
    ForceRebuild
}

// Package打包后记录的信息
public class PackageBuildInfo
{
    public string PackageName;
    public List<AssetBuildInfo> AssetInfos = new List<AssetBuildInfo>();
    public List<string> PackageDependecies = new List<string>();
    // 代表是否是初始包
    public bool IsSourcePackage = false;
}

// Package中的Assets打包之后记录的信息
public class AssetBuildInfo
{
    // 资源名称，当需要加载资源是，应该和该字符串相同
    public string AssetName;

    // 该资源属于哪个AssetBundle
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
            Debug.LogError($"{assetName}未在{PackageName}中找到");
        }

        return assetObject;
    }
}


public class AssetManagerRuntime
{
    // 当前类的单例
    public static AssetManagerRuntime Instance;

    // 当前资源包模式
    AssetBundlePattern CurrentPattern;

    // 所有本地Asset所处的路径，应为AssetBundleLoadPath的上一层
    string LocalAssetPath;

    // AssetBundle加载路径
    public string AssetBundleLoadPath;

    // 资源下载路径，下载完成后应将资源放置到LocalAssetPath中
    string DownloadPath;

    // 用于对比本地资源版本和远端资源版本号
    int LocalAssetVersion;

    // 本地所有的Package信息
    Dictionary<string, PackageBuildInfo> PackageDic = new Dictionary<string, PackageBuildInfo>();

    // 代表所有已加载的Package
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
        // asset.version 是由我们自定义拓展名的文本文件
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
            Debug.Log($"Package包的路径为：{packagePath}");
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
            Debug.LogWarning($"{packageName}已经加载");
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
