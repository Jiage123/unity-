using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.Plastic.Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

[Serializable]
public class PackageEditorInfo
{
    // ��ǰ�������ƿ����ɿ������ڱ༭�����������ɶ���
    public string PackageName;

    // �����ڵ�ǰ���е���Դ�б������ɿ������ڱ༭�������ɶ���
    public List<UnityEngine.Object> AssetList = new List<UnityEngine.Object>();
}

public class AssetManagerEditor
{
    public class AssetBundleEdge
    {
        // ���û����õ�Nodes
        public List<AssetBundleNode> Nodes = new List<AssetBundleNode>();
    }

    public class AssetBundleNode
    {
        // ��ǰNode���������Դ���ƣ��������Դ��Ψһ�ԣ�����ʹ��GUID����
        public string AssetName;

        // Node��������SourceAsset >= 0
        public int SourceIndex = -1;

        // ��������Դ������SourceAsset��Index��SourceIndices��͸ÿһ��������ϵ���ֳ�����
        public List<int> SourceIndices = new List<int>();

        // ֻ��SourceAsset�ž��а���
        public string PackageName;

        // DerivedAsset��ֻ��PackageNames��������ù�ϵ
        public List<string> PackageNames = new List<string>();

        public AssetBundleEdge InEdge; // ���ø���Դ�Ľڵ㣬ֻ���ֳ�һ��������ϵ
        public AssetBundleEdge OutEdge;   // ����Դ���õĽڵ㣬ֻ���ֳ�һ��������ϵ
    }

    public class AssetBundleVersionDifference
    {
        public List<string> AddtionAssetBundles;  // ���ΰ汾�Ա������ӵ�AssetBundle
        public List<string> ReducesAssetBundels;  // ���ΰ汾�Ա��м��ٵ�AssetBundle
    }

    // AB�������·��
    public static string AssetBundleOutputPath;

    public static AssetManagerConfigScriptableObject AssetManagerConfig;
    public static AssetManagerEditorWindowConfig AssetManagerWindowConfig;

    // ��ΪAssets�µ������ű��ᱻ���뵽AssetmblyCharp.dll�и����ڰ�������ȥ��
    // ���Բ�����ʹ������UnityEditor�����ռ��µķ���,�����ܼ̳�MonoBehaviour
    [MenuItem(nameof(AssetManagerEditor) + "/" + nameof(BuildAssetBundle))]
    // ����[MenuItem("AssetManager/BuildAssetBundle")]
    static void BuildAssetBundle()
    {
        CheckBuildOutputPath();

        // Directory������PCƽ̨��Ŀ¼���в�������
        if (!Directory.Exists(AssetBundleOutputPath))
        {
            Directory.CreateDirectory(AssetBundleOutputPath);
        }

        // ��ͬƽ̨֮���AssetBundle������ͨ�ã��÷����������������������˰�����AB�����������Ŀ¼�������ʽ�����ƽ̨
        BuildPipeline.BuildAssetBundles(AssetBundleOutputPath, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows);
        AssetDatabase.Refresh(); // ˢ��
        Debug.Log($"AB�������ɣ�����·��Ϊ��{AssetBundleOutputPath}");
    }

    // ����MenuItem��������һ����̬����,�Դ���Window��
    [MenuItem("AssetManagerEditor/OpenAssetManagerWindow")]
    static void OpenAssetManagerEditorWindow()
    {
        // ���ʱִ�и÷���(���ð�),����һ�����ڣ�����һ����ΪAssetManagerName�Ĵ���
        AssetManagerEditorWindow window = EditorWindow.GetWindow<AssetManagerEditorWindow>("AssetManager");
    }

    // ͨ����̬����������ScriptableObject�ļ�
    [MenuItem("AssetManagerEditor/CreateConfigFile")]
    public static void CreateNewConfigScriptableObject()
    {
        // ����ScriptableObject ���͵�ʵ������������JSON�н�ĳ�����ʵ�����Ĺ���
        AssetManagerConfigScriptableObject assetManagerConfig = ScriptableObject.CreateInstance<AssetManagerConfigScriptableObject>();
        AssetDatabase.CreateAsset(assetManagerConfig, "Assets/Editor/AssetManagerConfig.asset");
        AssetDatabase.SaveAssets();  // ������Դ
        AssetDatabase.Refresh(); // ˢ�±���Ŀ¼
    }

    // ������ĸ����ģʽ
    public static string OutputBundleName;
    static void CheckBuildOutputPath()
    {
        switch (AssetManagerConfig.BuildingPattern)
        {
            case AssetBundlePattern.EditorSimulation:
                OutputBundleName = "Editor";
                break;
            case AssetBundlePattern.Local:
                OutputBundleName = "Local";
                AssetBundleOutputPath = Path.Combine(Application.streamingAssetsPath, "LocalAssets");
                break;
            case AssetBundlePattern.Remote:
                OutputBundleName = "Remote";
                AssetBundleOutputPath = Path.Combine(Application.persistentDataPath, "LocalAssets");
                break;
        }

        if (!Directory.Exists(AssetBundleOutputPath))
        {
            Directory.CreateDirectory(AssetBundleOutputPath);
            AssetDatabase.Refresh();
        }
    }

    static BuildAssetBundleOptions CheckCompressionPattern()
    {
        BuildAssetBundleOptions option = new BuildAssetBundleOptions();
        switch (AssetManagerConfig.CompressionPattern)
        {
            case AssetBundleCompressionPattern.LZMA:
                option = BuildAssetBundleOptions.None;
                break;
            case AssetBundleCompressionPattern.LZ4:
                option = BuildAssetBundleOptions.ChunkBasedCompression;
                break;
            case AssetBundleCompressionPattern.None:
                option = BuildAssetBundleOptions.UncompressedAssetBundle;
                break;
        }
        return option;
    }

    static BuildAssetBundleOptions CheckIncrementalMode()
    {
        BuildAssetBundleOptions options = BuildAssetBundleOptions.None;

        switch (AssetManagerConfig.BuildMode)
        {
            case IncrementalBuildMode.None:
                options = BuildAssetBundleOptions.None;
                break;
            case IncrementalBuildMode.UseIncrementalBuild:
                options = BuildAssetBundleOptions.DeterministicAssetBundle;
                break;
            case IncrementalBuildMode.ForceRebuild:
                options = BuildAssetBundleOptions.ForceRebuildAssetBundle;
                break;
        }
        return options;
    }

    // �ӵ�ǰѡ����Դ�������ȡ������Դ���ķ���
    public static string[] GetDependenciesFromAsset(string assetPath)
    {
        // ͨ���÷�����ȡ���������飬��������Ϊ�����б�L�е�ĳ��Ԫ�أ���������SourceAsset A�Լ���������Դ���Ƶļ���
        // һ��ȷ���� Source Asset������Unity�ڵ�API����ȡ�������� Derived Asset��
        string[] assetDependecies = AssetDatabase.GetDependencies(assetPath, true);
        foreach (string depName in assetDependecies)
        {
            Debug.Log($"����������Ϊ��{depName}");
        }
        return assetDependecies;
    }

    // ���ָ���ļ�����ѡ�е���Դ����������(����ͬ���õ���Դ����������)
    public static void BuildAssetBundleFromSets()
    {
        CheckBuildOutputPath();

        // �ڴ����б�ѡ�н�Ҫ�������Դ�б� SourceAsset�б�A
        List<string> selectedAssets = new List<string>();

        // ����SourceAsset�������ģ�����ΪDerivedAsset����DerivedAsset�ļ��������ɵ��б�
        List<List<GUID>> selectedAssetsDependencies = new List<List<GUID>>();

        // ��������ѡ���SourceAsset�Լ���������ȡ�����б�L
        foreach (string assetName in selectedAssets)
        {
            string[] depAssets = GetDependenciesFromAsset(assetName);
            List<GUID> depAssetsGUID = new List<GUID>();
            foreach (string depName in depAssets)
            {
                GUID assetGUID = AssetDatabase.GUIDFromAssetPath(depName);
                depAssetsGUID.Add(assetGUID);
            }
            // ����ǰ��Դ���������ϴ����б�L
            selectedAssetsDependencies.Add(depAssetsGUID);
        }

        // �Ա�����ļ���Ԫ��i��i+1
        for (int i = 0; i < selectedAssetsDependencies.Count; i++)
        {
            int nextCount = i + 1;
            if ((nextCount >= selectedAssetsDependencies.Count))
            {
                break;
            }

            for (int j = 0; j <= i; j++)
            {
                List<GUID> newDerivedAssets = ContrastDependenciesFromGUID(selectedAssetsDependencies[j],
                                             selectedAssetsDependencies[nextCount]);

                // ��Snew��ӵ������б�L�У������Ա�
                if (newDerivedAssets.Count > 0)
                {
                    selectedAssetsDependencies.Add(newDerivedAssets);
                }
            }
        }

        // ����ĳ��ȴ�����AB��������
        AssetBundleBuild[] assetBundleBuilds = new AssetBundleBuild[selectedAssetsDependencies.Count];

        for (int i = 0; i < selectedAssetsDependencies.Count; i++)
        {
            string[] assetNames = new string[selectedAssetsDependencies[i].Count];
            assetBundleBuilds[i].assetBundleName = i.ToString(); // ��������İ���
            for (int j = 0; j < assetNames.Length; j++)
            {
                string assetName = AssetDatabase.GUIDToAssetPath(selectedAssetsDependencies[i][j]);
                assetNames[j] = assetName;  // ��ȡ��ǰ�����е�J��Ԫ�ص���Դ·��
            }
            assetBundleBuilds[i].assetNames = assetNames; // ����AB����ֻ����Ҫ�������Դ
        }

        BuildPipeline.BuildAssetBundles(AssetBundleOutputPath, assetBundleBuilds, BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows);
        Debug.Log($"AB�������ɣ�����·��Ϊ��{AssetBundleOutputPath}");
        AssetDatabase.Refresh();   // ˢ��Project���棬������Ǵ��������������Ҫִ��
    }

    public static List<GUID> ContrastDependenciesFromGUID(List<GUID> depA, List<GUID> depB)
    {
        // ������������ҪSnew���ϣ���Ϊ����ֵ����
        List<GUID> newDerivedAssets = new List<GUID>();

        // ȡ����
        foreach (GUID asset in depA)
        {
            if (depB.Contains(asset))
            {
                newDerivedAssets.Add(asset);
            }
        }

        // ȡ������������ᵽ�ģ���Si��Sj��ȡ��Snew�Ĳ
        foreach (GUID asset in newDerivedAssets)
        {
            if (depA.Contains(asset))
            {
                depA.Remove(asset);
            }
            if (depB.Contains(asset))
            {
                depB.Remove(asset);
            }
        }
        Debug.Log(newDerivedAssets.Count);
        return newDerivedAssets;
    }

    // ���ָ���ļ�����ѡ�е���Դ
    public static void BuildAssetBundleFromEditorWindow()
    {
        CheckBuildOutputPath();

        // ѡ���˶�����Դ���������ٸ�AB��
        List<string> selectedAssets = new List<string>();
        // ����ĳ��ȴ�����AB��������
        AssetBundleBuild[] builds = new AssetBundleBuild[selectedAssets.Count];
        for (int i = 0; i < selectedAssets.Count; i++)
        {
            //string directoryPath = AssetDatabase.GetAssetPath(AssetManagerConfig.AssetBundleDirectory);
            //�Ӵ���AssetsĿ¼����Դ���ƣ��滻��Ŀ¼����
            //string assetName = selectedAssets[i].Replace($@"{directoryPath}\", string.Empty);
            //assetName = assetName.Replace(".prefab", string.Empty);

            //builds[i].assetBundleName = assetName; // ʹ���ļ�����Ϊ��Դ����
            builds[i].assetNames = new string[] { selectedAssets[i] };  // ����AB����ֻ����Ҫ�������Դ
        }

        BuildPipeline.BuildAssetBundles(AssetBundleOutputPath, builds, BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows);
        Debug.Log($"AB�������ɣ�����·��Ϊ��{AssetBundleOutputPath}");
        AssetDatabase.Refresh();
    }

    // ���ָ���ļ�����������ԴΪAssetBundle
    public static void BuildAssetBundleFromDirectory()
    {
        CheckBuildOutputPath();

        // ��ȡ���봰���е�������Դ
        //AssetManagerConfig.GetCurrentAllAssetsFromDirectory();

        // ����ĳ��ȴ�����AB��������
        AssetBundleBuild[] assetBundleBuilds = new AssetBundleBuild[1];

        // ���ô��������AB������Ϊresource.ab
        assetBundleBuilds[0].assetBundleName = "Local";
        //assetBundleBuilds[0].assetNames = AssetManagerConfig.CurrentAllAssets.ToArray();

        if (string.IsNullOrEmpty(AssetBundleOutputPath))
        {
            Debug.LogError("���·��Ϊ��");
            return;
        }
        else if (!Directory.Exists(AssetBundleOutputPath))
        {
            Directory.CreateDirectory(AssetBundleOutputPath);
        }

        BuildPipeline.BuildAssetBundles(AssetBundleOutputPath, assetBundleBuilds, BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows);
        Debug.Log($"AB�������ɣ�����·��Ϊ��{AssetBundleOutputPath}");
        AssetDatabase.Refresh();
    }

    // ��������ͼ����ʵallNodes �����ó�Ա���������棬�Ͳ���Ҫ�ٺ����д�����
    static void BuildDirectedGraph(AssetBundleNode lastNode, List<AssetBundleNode> allNodes)
    {
        if (lastNode == null)
        {
            Debug.Log("lastNodeΪ�գ���������ͼʧ��");
        }
        // ֻ��ȡֱ�ӵ�����
        string[] depends = AssetDatabase.GetDependencies(lastNode.AssetName, false);

        // ��������Դ��������0�������Ѿ��ߵ������ù�ϵ�����յ㣬Ҳ��������ͼ���յ㣬�������Ϸ���
        if (depends.Length <= 0)
        {
            return;
        }

        // OutEdgeΪ�մ���û������������������depends > 0 ����϶�����������Դ
        if (lastNode.OutEdge == null)
        {
            lastNode.OutEdge = new AssetBundleEdge();
        }

        // һ����Դ��ֱ����������Դ�����޵ģ���Ϊx
        foreach (var dependName in depends)
        {

            // ÿһ����Ч����Դ����Ϊһ���µ�Node
            AssetBundleNode currentNode = null;

            // ���Ѿ����ڵ�Node��һ���Ƚϴ��ֵ����Ϊn������ͼ���������У�Ƕ�ײ���Ϊ y
            // ���Ŀ��ܵı�������Ϊ y*x*n������ʵ���ϵı��������϶�����n*n
            foreach (AssetBundleNode existingNode in allNodes)
            {
                // �����Դ�Ѿ������ڵ㣬��ôֱ���������нڵ�
                if (existingNode.AssetName == dependName)
                {
                    currentNode = existingNode;
                    break;
                }
            }

            // ����������нڵ㣬������һ���½ڵ�
            if (currentNode == null)
            {
                currentNode = new AssetBundleNode();
                currentNode.AssetName = dependName;

                // ��Ϊ��ǰNode�ض���LastNode�������������Ա�Ȼ����InEdge��SourceIndices
                currentNode.InEdge = new AssetBundleEdge();
                currentNode.SourceIndices = new List<int>();
                allNodes.Add(currentNode);
            }

            currentNode.InEdge.Nodes.Add(lastNode);
            lastNode.OutEdge.Nodes.Add(currentNode);

            // �����Լ�������Դ�����ã�ͬ��Ҳͨ������ͼ���д���
            if (!string.IsNullOrEmpty(lastNode.PackageName))
            {
                if (!currentNode.PackageNames.Contains(lastNode.PackageName))
                {
                    currentNode.PackageNames.Add(lastNode.PackageName);
                }

            }
            else // ������DerivedAsset,ֱ�ӻ�ȡlastNOde��SourceIndices����
            {
                foreach (string packageNames in lastNode.PackageNames)
                {
                    if (!currentNode.PackageNames.Contains(packageNames))
                    {
                        currentNode.PackageNames.Add(packageNames);
                    }
                }
            }

            // ���lastNode��SourceAsset����ֱ��Ϊ��ǰ��Node���lastNode��Index
            // ��ΪList��һ���������ͣ�����SourceAsset��SourceIndices���º����ݺ�Drivedһ����Ҳ��Ϊһ���µ�List
            if (lastNode.SourceIndex >= 0)
            {
                if (!currentNode.SourceIndices.Contains(lastNode.SourceIndex))
                {
                    currentNode.SourceIndices.Add(lastNode.SourceIndex);
                }

            }
            else // DerivedAsset,ֱ�ӻ�ȡlastNOde��SourceIndices����
            {
                foreach (int index in lastNode.SourceIndices)
                {
                    if (!currentNode.SourceIndices.Contains(index))
                    {
                        currentNode.SourceIndices.Add(index);
                    }
                }
            }
            BuildDirectedGraph(currentNode, allNodes);
        }
    }

    // ������ͼ�й���AB��
    public static void BuildAssetBundleFromDirectedGraph()
    {
        CheckBuildOutputPath();

        List<AssetBundleNode> allNodes = new List<AssetBundleNode>();
        int sourceIndex = 0;
        Dictionary<string, PackageBuildInfo> packageInfoDic = new Dictionary<string, PackageBuildInfo>();

        #region ����ͼ����

        for (int i = 0; i < AssetManagerConfig.packageInfoEditors.Count; i++)
        {
            PackageBuildInfo packageBuildInfo = new PackageBuildInfo();
            packageBuildInfo.PackageName = AssetManagerConfig.packageInfoEditors[i].PackageName;
            packageBuildInfo.IsSourcePackage = true;
            packageInfoDic.Add(packageBuildInfo.PackageName, packageBuildInfo);

            // ��ǰ��ѡ�е���Դ������SourceAsset,�����������SourceAsset��Node
            foreach (UnityEngine.Object asset in AssetManagerConfig.packageInfoEditors[i].AssetList)
            {
                AssetBundleNode currentNode = null;

                // ����Դ�ľ���·������Ϊ��Դ����
                string assetNamePath = AssetDatabase.GetAssetPath(asset);

                foreach (AssetBundleNode node in allNodes)
                {
                    if (node.AssetName == assetNamePath)
                    {
                        currentNode = node;
                        currentNode.PackageName = packageBuildInfo.PackageName;
                        break;
                    }
                }

                if (currentNode == null)
                {
                    currentNode = new AssetBundleNode();
                    currentNode.AssetName = assetNamePath;

                    // ΪʲôSourceAsset������SourceIndex����Ҫʹ��SourceIndices��
                    // ������Ϊ����ʹ��OutEdgeֱ��ʹ��SourceAsset��SourceIndices
                    currentNode.SourceIndex = sourceIndex;
                    currentNode.SourceIndices = new List<int>() { sourceIndex };

                    currentNode.PackageName = packageBuildInfo.PackageName;
                    currentNode.PackageNames.Add(currentNode.PackageName);

                    currentNode.InEdge = new AssetBundleEdge();
                    allNodes.Add(currentNode);
                }

                BuildDirectedGraph(currentNode, allNodes);

                sourceIndex++;
            }
        }
        #endregion

        #region ����ͼ�������
        // key����SourceIndices��key��ͬ��Node��Ӧ����ӵ�ͬһ��������
        Dictionary<List<int>, List<AssetBundleNode>> assetBundleNodesDic = new Dictionary<List<int>, List<AssetBundleNode>>();

        foreach (AssetBundleNode node in allNodes)
        {
            StringBuilder packageNameString = new StringBuilder();

            // ������Ϊ�ջ��ޣ��������һ��SourceAsset��������Ѿ��ڱ༭�������������
            if (string.IsNullOrEmpty(node.PackageName))
            {
                for (int i = 0; i < node.PackageNames.Count; i++)
                {
                    packageNameString.Append(node.PackageNames[i]);
                    if (i < node.PackageNames.Count - 1)
                    {
                        packageNameString.Append("_");
                    }
                }

                string packageName = packageNameString.ToString();
                node.PackageName = packageName;

                // ��ʱֻ����˶�Ӧ�İ��Լ���������û�о�����Ӱ��ж�Ӧ��Asset
                // ��ΪAsset�������Ҫ����AssetBundleName������ֻ��������AssetBundleBuild�ĵط����Asset
                if (!packageInfoDic.ContainsKey(packageName))
                {
                    PackageBuildInfo packageBuildInfo = new PackageBuildInfo();
                    packageBuildInfo.PackageName = packageName;
                    packageBuildInfo.IsSourcePackage = false;
                    packageInfoDic.Add(packageBuildInfo.PackageName, packageBuildInfo);
                }
            }

            bool isEquals = false;
            List<int> keyList = new List<int>();

            // �������е�key��ͨ�������ķ�ʽ����ȷ������ͬ��List֮�䣬������һ�µ�
            foreach (List<int> key in assetBundleNodesDic.Keys)
            {
                // �ж�key�ĳ����Ƿ�͵�ǰnode��SourceIndices�ĳ������
                isEquals = node.SourceIndices.Count == key.Count && node.SourceIndices.All(p => key.Any(k => k.Equals(p)));

                if (isEquals)
                {
                    keyList = key;
                    break;
                }
            }
            if (!isEquals)
            {
                keyList = node.SourceIndices;
                assetBundleNodesDic.Add(node.SourceIndices, new List<AssetBundleNode>());
            }

            // Node�ڹ���ʱ���ܱ�֤�϶������ظ�
            assetBundleNodesDic[keyList].Add(node);
        }
        #endregion

        AssetBundleBuild[] assetBundleBuilds = new AssetBundleBuild[assetBundleNodesDic.Count];
        int buildIndex = 0;
        foreach (var key in assetBundleNodesDic.Keys)
        {
            assetBundleBuilds[buildIndex].assetBundleName = buildIndex.ToString();
            List<string> assetNames = new List<string>();
            foreach (var node in assetBundleNodesDic[key])
            {
                assetNames.Add(node.AssetName);
                //Debug.Log($"Keyֵ�ĳ���={key.Count}������Node��{node.AssetName}");

                // ���һ��SourceAsset��������PackageNameֻ������Լ�
                foreach (string packageName in node.PackageNames)
                {
                    if (packageInfoDic.ContainsKey(packageName))
                    {
                        if (!packageInfoDic[packageName].PackageDependecies.Contains(node.PackageName) &&
                            !string.Equals(node.PackageName, packageInfoDic[packageName].PackageName))
                        {
                            packageInfoDic[packageName].PackageDependecies.Add(node.PackageName);
                        }
                    }
                }
            }

            // ����Ĳ�����ÿһ��AssetBundle�����в�������Asset·��
            // �����������Asset·��û�з����ı䣬�����ð�û�и�������
            assetBundleBuilds[buildIndex].assetBundleName = ComputeAssetSetSignature(assetNames);
            assetBundleBuilds[buildIndex].assetNames = assetNames.ToArray();

            foreach (AssetBundleNode node in assetBundleNodesDic[key])
            {

                // ��Ϊ�����˵�DerivedPackage�����Դ˴�����ȷ����ÿһ��Node������һ������
                AssetBuildInfo assetBuildInfo = new AssetBuildInfo();

                assetBuildInfo.AssetName = node.AssetName;
                assetBuildInfo.AssetBundleName = assetBundleBuilds[buildIndex].assetBundleName;

                packageInfoDic[node.PackageName].AssetInfos.Add(assetBuildInfo);

            }
            buildIndex++;
        }

        string outputPath = Path.Combine(AssetBundleOutputPath, OutputBundleName);
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }
        BuildPipeline.BuildAssetBundles(outputPath, assetBundleBuilds, CheckIncrementalMode(), BuildTarget.StandaloneWindows);
        Debug.Log($"AB�������ɣ�����·��Ϊ��{AssetBundleOutputPath}");

        //string buildVersionPath=Path.Combine(outputPath)
        string assetBundleVersionPath = Path.Combine(AssetBundleOutputPath, AssetManagerConfig.CurrentBuildVersion.ToString());

        if (!Directory.Exists(assetBundleVersionPath))
        {
            Directory.CreateDirectory(assetBundleVersionPath);
        }

        BuildAssetBundleHashTable(assetBundleBuilds, outputPath, assetBundleVersionPath);

        CopyAssetBundleToVersionFolder(outputPath, assetBundleVersionPath);


        BuildPackageTable(packageInfoDic, assetBundleVersionPath);

        AssetManagerConfig.CurrentBuildVersion++;

        AssetDatabase.Refresh();
    }
    static void NotKnow(AssetBundleBuild[] assetBundleBuilds)
    {
        // �������BuildAssetBundleFromDirectedGraph()�е�������仰���棬
        // �������仰���ظ����֣�ֱ�ӽ����븴�ƹ�ȥ����ɾ��ԭ���ĺ����
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(AssetBundleOutputPath, assetBundleBuilds,
            CheckCompressionPattern(), BuildTarget.StandaloneWindows);
        Debug.Log($"AB�������ɣ�����·��Ϊ��{AssetBundleOutputPath}");

        // �õ���δ��������AssetBundle
        string[] assetBundleNames = manifest.GetAllAssetBundles();
        string[] assetBundleHashs = new string[assetBundleNames.Length];
        for (int i = 0; i < assetBundleNames.Length; i++)
        {
            string assetBundlePath = Path.Combine(AssetBundleOutputPath, assetBundleNames[i]);
            assetBundleHashs[i] = $"{ComputeAssetBundleSizeToMD5(assetBundlePath)}_{assetBundleNames[i]}";
        }

        StringBuilder assetBundleHashString = new StringBuilder();
        foreach (string hash in assetBundleHashs)
        {
            assetBundleHashString.Append($"{hash}\r\n");
        }

        // д���ļ�
        string assetBundleHashsFilePath = Path.Combine(AssetBundleOutputPath, "AssetBundleHashFile.json");
        FileStream fileStream = File.Open(assetBundleHashsFilePath, FileMode.OpenOrCreate);
        byte[] bytes = Encoding.ASCII.GetBytes(assetBundleHashString.ToString());
        fileStream.Write(bytes, 0, bytes.Length);

        // ˢ�¡��رա��ͷ��ļ�������
        fileStream.Flush();
        fileStream.Close();
        fileStream.Dispose();
        AssetDatabase.Refresh();
    }

    // ��������AssetBundle������Asset����AssetGUIDת����byte[]
    static string ComputeAssetSetSignature(IEnumerable<string> assetNames)
    {
        var assetGuids = assetNames.Select(AssetDatabase.AssetPathToGUID);
        MD5 md5 = MD5.Create();

        // ��������asset������·������ͬ����ô���Եõ���ͬ��MD5��ϣֵ
        foreach (string assetGuid in assetGuids.OrderBy(x => x))
        {
            byte[] buffer = Encoding.ASCII.GetBytes(assetGuid);
            md5.TransformBlock(buffer, 0, buffer.Length, null, 0);
        }

        md5.TransformFinalBlock(new byte[0], 0, 0);
        return BytesToHexString(md5.Hash);
    }

    // byteת16�����ַ���
    static string BytesToHexString(byte[] bytes)
    {
        StringBuilder byteString = new StringBuilder();
        foreach (byte aByte in bytes)
        {
            byteString.Append(aByte.ToString("x2"));
        }
        return byteString.ToString();
    }

    static string ComputeAssetBundleSizeToMD5(string assetBundlePath)
    {
        MD5 md5 = MD5.Create();
        FileInfo fileInfo = new FileInfo(assetBundlePath);
        byte[] buffer = Encoding.ASCII.GetBytes(fileInfo.Length.ToString());
        md5.TransformBlock(buffer, 0, buffer.Length, null, 0);
        md5.TransformFinalBlock(new byte[0], 0, 0);

        return BytesToHexString(md5.Hash);
    }

    // ����AB����hash������¼AB���Ĵ�С��hashֵ
    static string[] BuildAssetBundleHashTable(AssetBundleBuild[] assetBundleBuilds, string outputPath, string versionPath)
    {
        string[] assetBundleHashs = new string[assetBundleBuilds.Length];
        for (int i = 0; i < assetBundleBuilds.Length; i++)
        {
            string assetBundlePath = Path.Combine(outputPath, assetBundleBuilds[i].assetBundleName);
            FileInfo fileInfo = new FileInfo(assetBundlePath);

            // �µ���������Ϊ�������С_�����ڵ�AssetGUID��MD5��
            assetBundleHashs[i] = $"{fileInfo.Length}_{assetBundleBuilds[i].assetBundleName}";

        }

        // д���ļ�
        string hashString = JsonConvert.SerializeObject(assetBundleHashs);
        string hashFilePath = Path.Combine(versionPath, "AssetBundleHashs");
        File.WriteAllText(hashFilePath, hashString);

        return assetBundleHashs;
    }

    // ֮ǰ��BuildHashTable�����У����������ɵ�����AB������������ΰ汾����
    static AssetBundleVersionDifference ContrastAssetBundleVersion(string[] oldVersion, string[] newVersion)
    {
        AssetBundleVersionDifference difference = new AssetBundleVersionDifference();
        difference.AddtionAssetBundles = new List<string>();
        difference.ReducesAssetBundels = new List<string>();

        // �Ա�ÿһ���ϰ汾��ab��(hash��)������°汾�����ڸð�������Ϊ��Ҫ���ٵİ�
        foreach (var assetBundle in oldVersion)
        {
            if (!newVersion.Contains(assetBundle))
            {
                difference.ReducesAssetBundels.Add(assetBundle);
            }
        }

        // �Ա�ÿһ���°汾��ab��(hash��)������ϰ汾�����ڸð�������Ϊ��Ҫ�����İ�
        foreach (var assetBundle in newVersion)
        {
            if (!oldVersion.Contains(assetBundle))
            {
                difference.AddtionAssetBundles.Add(assetBundle);
            }
        }
        return difference;
    }

    static string[] ReadAssetBundleHashTable(string outpuePath)
    {
        string VersionHashTablePath = Path.Combine(outpuePath, "AssetBundleHashs");
        string VersionHashString = File.ReadAllText(VersionHashTablePath);
        string[] VersionAssetHashs = JsonConvert.DeserializeObject<string[]>(VersionHashString);
        return VersionAssetHashs;
    }
    static void CopyAssetBundleToVersionFolder(string oringinPath, string targetPath)
    {

        string[] assetNames = ReadAssetBundleHashTable(targetPath);

        // ���ƹ�ϣ��
        //string hashTableOriginPath = Path.Combine(outputPath, "AssetBundleHashs");
        //string hashTableVersionPath = Path.Combine(assetBundleVersionOutputPath, "AssetBundleHashs");
        //File.Copy(hashTableOriginPath, hashTableVersionPath, true);

        // ��������
        string mainBundleOriginPath = Path.Combine(oringinPath, OutputBundleName);
        string mainBundleVersionPath = Path.Combine(targetPath, OutputBundleName);
        File.Copy(mainBundleOriginPath, mainBundleVersionPath, true);

        // ����PackageInfos
        //string packageInfoPath = Path.Combine(outputPath, PackageTableName);
        //string packageInfoVersionPath = Path.Combine(assetBundleVersionOutputPath, PackageTableName);
        //File.Copy(packageInfoPath, packageInfoVersionPath, true);

        foreach (var assetName in assetNames)
        {
            string assetHashName = assetName.Substring(assetName.IndexOf("_") + 1);

            // file.Name�ǰ�������չ�����ļ���
            string assetOriginPath = Path.Combine(oringinPath, assetHashName);
            // file.FullName�ǰ�����Ŀ¼���ļ���������·��
            string assetVersionPath = Path.Combine(targetPath, assetHashName);

            File.Copy(assetOriginPath, assetVersionPath, true); // ��һ��true����ʾ����
        }
    }

    // ��ScriptableObject�����ݱ���ΪJson��ʽ
    public static void SaveConfigToJSON()
    {
        if (AssetManagerConfig != null)
        {
            string configString = JsonUtility.ToJson(AssetManagerConfig);
            string outputPath = Path.Combine(Application.dataPath, "Editor/AssetManagerConfig.json");
            File.WriteAllText(outputPath, configString);
            AssetDatabase.Refresh();
        }
    }

    // �ȼ���Config�ļ�
    public static void LoadAssetManagerConfig(AssetManagerEditorWindow window)
    {
        if (AssetManagerConfig == null)
        {
            // ʹ��AssetDataBase������Դ��ֻ��Ҫ����AssetsĿ¼�µ�·������
            AssetManagerConfig = AssetDatabase.LoadAssetAtPath<AssetManagerConfigScriptableObject>(
                                                                    "Assets/Editor/AssetManagerConfig.asset");
            window.VersionString = AssetManagerConfig.AssetManagerVersion.ToString();
            for (int i = window.VersionString.Length - 1; i >= 1; i--)
            {
                window.VersionString = window.VersionString.Insert(i, ".");
            }

        }
    }

    // �ٴ�Json�ж�ȡConfig
    public static void ReadConfigFromJSON()
    {
        string configPath = Path.Combine(Application.dataPath, "Editor/AssetManagerConfig.json");
        string configString = File.ReadAllText(configPath);
        JsonUtility.FromJsonOverwrite(configString, AssetManagerConfig);
    }

    public static void GetFolderAllAssets()
    {
        //if(AssetManagerConfig.AssetBundleDirectory == null)
        //{
        //    AssetManagerConfig.CurrentAllAssets.Clear();
        //    return;
        //}
        //string directoryPath = AssetDatabase.GetAssetPath(AssetManagerConfig.AssetBundleDirectory);
        //AssetManagerConfig.CurrentAllAssets = AssetManagerConfig.FindAllAssetPathFromDirectory(directoryPath);
        //AssetManagerConfig.CurrentSelectedAssets = new bool[AssetManagerConfig.CurrentAllAssets.Count];
    }

    // ����WindowConfig�ļ�
    public static void LoadAssetManagerWindowConfig(AssetManagerEditorWindow window)
    {
        if (window.WindowConfig == null)
        {
            // ʹ��AssetDataBase������Դ��ֻ��Ҫ����AssetsĿ¼�µ�·������
            window.WindowConfig = AssetDatabase.LoadAssetAtPath<AssetManagerEditorWindowConfig>(
                                                              "Assets/Editor/AssetManagerWindowConfig.asset");

            #region ����LOGO
            window.WindowConfig.LogoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/1.jpg");
            window.WindowConfig.LogoTextureStyle = new GUIStyle();
            window.WindowConfig.LogoTextureStyle.fixedWidth = window.WindowConfig.LogoTexture.width / 2;
            window.WindowConfig.LogoTextureStyle.fixedHeight = window.WindowConfig.LogoTexture.height / 2;
            #endregion

            #region ����
            window.WindowConfig.TitleTextStyle = new GUIStyle();
            window.WindowConfig.TitleTextStyle.fontSize = 24;
            window.WindowConfig.TitleTextStyle.normal.textColor = Color.red;
            window.WindowConfig.TitleTextStyle.alignment = TextAnchor.MiddleCenter;
            #endregion

            #region �汾��
            window.WindowConfig.VersionTextStyle = new GUIStyle();
            window.WindowConfig.VersionTextStyle.fontSize = 16;
            window.WindowConfig.VersionTextStyle.normal.textColor = Color.green;
            window.WindowConfig.VersionTextStyle.alignment = TextAnchor.MiddleCenter;
            #endregion
        }
    }

    public static void AddPackageInfoEditor()
    {
        AssetManagerConfig.packageInfoEditors.Add(new PackageEditorInfo());
    }

    public static void RemovePackageInfoEditors(PackageEditorInfo info)
    {
        if (AssetManagerConfig.packageInfoEditors.Contains(info))
        {
            AssetManagerConfig.packageInfoEditors.Remove(info);
        }
    }

    public static void AddAsset(PackageEditorInfo info)
    {
        info.AssetList.Add(null);
    }

    public static void RemoveAsset(PackageEditorInfo info, UnityEngine.Object asset)
    {
        if (info.AssetList.Contains(asset))
        {
            info.AssetList.Remove(asset);
        }
    }

    public static string PackageTableName = "AllPackages";
    static void BuildPackageTable(Dictionary<string, PackageBuildInfo> packages, string outputPath)
    {
        string packasgesPath = Path.Combine(outputPath, PackageTableName);

        // Package�ֵ䣬keyΪ����
        string packagesJSON = JsonConvert.SerializeObject(packages.Keys);

        File.WriteAllText(packasgesPath, packagesJSON);

        foreach (PackageBuildInfo package in packages.Values)
        {
            packasgesPath = Path.Combine(outputPath, package.PackageName);
            packagesJSON = JsonConvert.SerializeObject(package);

            File.WriteAllText(packasgesPath, packagesJSON);
        }
    }
}

